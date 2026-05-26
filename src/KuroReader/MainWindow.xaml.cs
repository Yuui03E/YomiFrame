using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media.Animation;
using Microsoft.Win32;
using SkiaSharp;
using SkiaSharp.Views.Desktop;
using SkiaSharp.Views.WPF;
using KuroReader.Core.Archives;
using KuroReader.Core.Cache;
using KuroReader.Core.Config;
using KuroReader.Core.Navigation;
using KuroReader.Rendering;
using KuroReader.Views;

namespace KuroReader;

/// <summary>
/// Main window for KuroReader — frameless, black, fast.
/// </summary>
public partial class MainWindow : Window
{
    // ── State ──────────────────────────────────────────────────
    private IArchiveReader? _currentArchive;
    private CacheManager? _cacheManager;
    private int _currentPageIndex;
    private int _totalPages;
    private string? _currentFilePath;

    // ── Display ────────────────────────────────────────────────
    private readonly List<SKBitmap> _activeBitmaps = new();
    private FitMode _fitMode = FitMode.FitScreen;
    private double _zoomLevel = 1.0;
    private int _rotationDegrees;
    private SKPoint _panOffset = SKPoint.Empty;
    private bool _isPanning;
    private SKPoint _panStart;

    // ── Config ─────────────────────────────────────────────────
    private readonly SettingsManager _settingsManager;
    private readonly RecentFiles _recentFiles;
    private readonly Bookmarks _bookmarks;

    // Convenience accessor
    private AppSettings Settings => _settingsManager.Settings;

    // ── Animation ──────────────────────────────────────────────
    private System.Windows.Threading.DispatcherTimer? _spinnerTimer;
    private double _spinnerAngle;
    private System.Windows.Threading.DispatcherTimer? _pageInfoTimer;

    // ── Supported file extensions ──────────────────────────────
    private static readonly HashSet<string> ArchiveExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".zip", ".cbz", ".rar", ".cbr", ".7z", ".cb7"
    };

    private static readonly HashSet<string> ImageExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".jpg", ".jpeg", ".png", ".webp", ".gif", ".bmp", ".tif", ".tiff"
    };

    public MainWindow()
    {
        InitializeComponent();
        DataContext = this;

        // Initialize managers (loading happens async in Loaded event)
        _settingsManager = new SettingsManager();
        _recentFiles = new RecentFiles();
        _bookmarks = new Bookmarks();

        // Wire up events
        KeyDown += OnKeyDown;
        MouseWheel += OnMouseWheel;
        MouseLeftButtonDown += OnMouseLeftButtonDown;
        MouseLeftButtonUp += OnMouseLeftButtonUp;
        MouseMove += OnMouseMove;
        Drop += OnFileDrop;
        DragEnter += OnDragEnter;
        StateChanged += OnWindowStateChanged;
        SizeChanged += OnWindowSizeChanged;
        Closing += OnWindowClosing;

        // Async initialization on Loaded
        Loaded += async (_, _) =>
        {
            // Load all config async
            await _settingsManager.LoadAsync();
            await _recentFiles.LoadAsync();
            await _bookmarks.LoadAsync();

            // Apply saved window state & settings
            ApplyWindowState();
            
            if (Settings.ViewMode == ViewMode.Single) _fitMode = Settings.FitModeSingle;
            else if (Settings.ViewMode == ViewMode.Double) _fitMode = Settings.FitModeDouble;
            else _fitMode = Settings.FitModeWebtoon;
            _zoomLevel = Settings.ZoomLevel;
            _rotationDegrees = Settings.RotationDegrees;

            // Build context menu (needs settings loaded first)
            BuildContextMenu();

            // Check for startup file (command-line arg)
            if (Application.Current.Properties["StartupFile"] is string startupFile)
            {
                await LoadFileAsync(startupFile);
            }
        };
    }

    // ═══════════════════════════════════════════════════════════
    // TITLE BAR BUTTONS
    // ═══════════════════════════════════════════════════════════

    private void BtnMinimize_Click(object sender, RoutedEventArgs e) => WindowState = WindowState.Minimized;

    private void BtnMaximize_Click(object sender, RoutedEventArgs e)
    {
        WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;
    }

    private void BtnClose_Click(object sender, RoutedEventArgs e) => Close();

    private void OnWindowStateChanged(object? sender, EventArgs e)
    {
        MaximizeIcon.Text = WindowState == WindowState.Maximized ? "\uE923" : "\uE922";
    }

    // ═══════════════════════════════════════════════════════════
    // CONTEXT MENU
    // ═══════════════════════════════════════════════════════════

    private void BuildContextMenu()
    {
        MainContextMenu.Items.Clear();

        // ── FILE ───────────────────────────────────────────────
        var fileMenu = new MenuItem { Header = "_File" };

        fileMenu.Items.Add(CreateMenuItem("_Load Files", "Ctrl+O", async () => await ShowLoadFileDialog()));
        fileMenu.Items.Add(CreateMenuItem("Resume _Last File", "Ctrl+Shift+R", async () => await ResumeLastFile()));

        var openRecentMenu = new MenuItem { Header = "Open _Recent" };
        RefreshRecentMenu(openRecentMenu, resumeAtPage: false);
        fileMenu.Items.Add(openRecentMenu);

        var resumeRecentMenu = new MenuItem { Header = "Resu_me Recent" };
        RefreshRecentMenu(resumeRecentMenu, resumeAtPage: true);
        fileMenu.Items.Add(resumeRecentMenu);

        fileMenu.Items.Add(new Separator());
        fileMenu.Items.Add(CreateMenuItem("Load _Next File", "Ctrl+]", async () => await LoadAdjacentFile(1)));
        fileMenu.Items.Add(CreateMenuItem("Load _Previous File", "Ctrl+[", async () => await LoadAdjacentFile(-1)));
        fileMenu.Items.Add(CreateMenuItem("Open _Folder", "Ctrl+Shift+O", async () => await ShowOpenFolderDialog()));

        fileMenu.Items.Add(new Separator());
        fileMenu.Items.Add(CreateMenuItem("_Save to File", "Ctrl+S", SaveCurrentPageToFile));
        fileMenu.Items.Add(CreateMenuItem("Save to _Clipboard", "Ctrl+C", SaveCurrentPageToClipboard));

        fileMenu.Items.Add(new Separator());
        fileMenu.Items.Add(CreateMenuItem("Full _Screen", "F11", ToggleFullScreen));
        fileMenu.Items.Add(CreateMenuItem("Full _Desktop", "F12", ToggleFullDesktop));
        fileMenu.Items.Add(CreateMenuItem("Mi_nimize", "", () => WindowState = WindowState.Minimized));
        fileMenu.Items.Add(new Separator());
        fileMenu.Items.Add(CreateMenuItem("E_xit", "Alt+F4", Close));

        MainContextMenu.Items.Add(fileMenu);

        // ── READ ───────────────────────────────────────────────
        var readMenu = new MenuItem { Header = "_Read" };

        readMenu.Items.Add(CreateMenuItem("Go to _First Page", "Home", () => GoToPage(0)));
        readMenu.Items.Add(CreateMenuItem("Go to _Last Page", "End", () => GoToPage(_totalPages - 1)));
        readMenu.Items.Add(CreateMenuItem("Go to _Next Page", "→ / PgDn", () => NavigatePage(1)));
        readMenu.Items.Add(CreateMenuItem("Go to _Previous Page", "← / PgUp", () => NavigatePage(-1)));
        readMenu.Items.Add(CreateMenuItem("_Go to Page...", "Ctrl+G", ShowGoToPageDialog));

        readMenu.Items.Add(new Separator());
        readMenu.Items.Add(CreateMenuItem("Scroll _Up", "↑", () => Scroll(0, -50)));
        readMenu.Items.Add(CreateMenuItem("Scroll _Down", "↓", () => Scroll(0, 50)));
        readMenu.Items.Add(CreateMenuItem("Scroll _Left", "Shift+←", () => Scroll(-50, 0)));
        readMenu.Items.Add(CreateMenuItem("Scroll _Right", "Shift+→", () => Scroll(50, 0)));

        readMenu.Items.Add(new Separator());

        MainContextMenu.Items.Add(readMenu);

        // ── OPTIONS ────────────────────────────────────────────
        var optionsMenu = new MenuItem { Header = "_Options" };

        // Reading Direction
        var dirMenu = new MenuItem { Header = "Reading _Direction" };
        dirMenu.Items.Add(CreateCheckMenuItem("_Right to Left", Settings.ReadingDirection == ReadingDirection.RightToLeft,
            () => SetReadingDirection(ReadingDirection.RightToLeft)));
        dirMenu.Items.Add(CreateCheckMenuItem("_Left to Right", Settings.ReadingDirection == ReadingDirection.LeftToRight,
            () => SetReadingDirection(ReadingDirection.LeftToRight)));
        optionsMenu.Items.Add(dirMenu);

        // View Mode
        var viewModeMenu = new MenuItem { Header = "_View Mode" };
        viewModeMenu.Items.Add(CreateCheckMenuItem("Single Page", Settings.ViewMode == ViewMode.Single, () => SetViewMode(ViewMode.Single)));
        viewModeMenu.Items.Add(CreateCheckMenuItem("Double Page", Settings.ViewMode == ViewMode.Double, () => SetViewMode(ViewMode.Double)));
        viewModeMenu.Items.Add(CreateCheckMenuItem("Webtoon", Settings.ViewMode == ViewMode.Webtoon, () => SetViewMode(ViewMode.Webtoon)));
        
        viewModeMenu.Items.Add(new Separator());
        viewModeMenu.Items.Add(CreateCheckMenuItem("_Cover", Settings.DoublePage_Cover, () => ToggleSetting(s => s.DoublePage_Cover = !s.DoublePage_Cover)));
        viewModeMenu.Items.Add(CreateCheckMenuItem("_Shadow", Settings.DoublePage_Shadow, () => ToggleSetting(s => s.DoublePage_Shadow = !s.DoublePage_Shadow)));
        viewModeMenu.Items.Add(CreateCheckMenuItem("_Forward One Page", Settings.DoublePage_ForwardOne, () => ToggleSetting(s => s.DoublePage_ForwardOne = !s.DoublePage_ForwardOne)));
        viewModeMenu.Items.Add(CreateCheckMenuItem("S_plit", Settings.DoublePage_Split, () => ToggleSetting(s => s.DoublePage_Split = !s.DoublePage_Split)));
        optionsMenu.Items.Add(viewModeMenu);

        // Zoom
        var zoomMenu = new MenuItem { Header = "_Zoom" };
        zoomMenu.Items.Add(CreateMenuItem("Zoom _In", "Ctrl++", () => SetZoom(_zoomLevel * 1.25)));
        zoomMenu.Items.Add(CreateMenuItem("Zoom _Out", "Ctrl+-", () => SetZoom(_zoomLevel / 1.25)));
        zoomMenu.Items.Add(new Separator());
        foreach (var pct in new[] { 100, 125, 150, 175, 200, 400 })
        {
            var z = pct / 100.0;
            zoomMenu.Items.Add(CreateMenuItem($"{pct}%", "", () => SetZoom(z)));
        }
        zoomMenu.Items.Add(new Separator());
        zoomMenu.Items.Add(CreateMenuItem("_Custom...", "", ShowCustomZoomDialog));
        optionsMenu.Items.Add(zoomMenu);

        // Rotate
        var rotateMenu = new MenuItem { Header = "_Rotate" };
        rotateMenu.Items.Add(CreateMenuItem("0°", "", () => SetRotation(0)));
        rotateMenu.Items.Add(CreateMenuItem("90° CW", "", () => SetRotation(90)));
        rotateMenu.Items.Add(CreateMenuItem("180°", "", () => SetRotation(180)));
        rotateMenu.Items.Add(CreateMenuItem("90° CCW", "", () => SetRotation(270)));
        optionsMenu.Items.Add(rotateMenu);

        // Fit Mode
        var fitMenu = new MenuItem { Header = "_Fit Mode" };
        foreach (var mode in Enum.GetValues<FitMode>())
        {
            var m = mode;
            fitMenu.Items.Add(CreateCheckMenuItem(FitModeToString(m), _fitMode == m, () => SetFitMode(m)));
        }
        optionsMenu.Items.Add(fitMenu);

        optionsMenu.Items.Add(new Separator());
        optionsMenu.Items.Add(CreateCheckMenuItem("Show _Page Number", Settings.ShowPageNumber, () => ToggleSetting(s => s.ShowPageNumber = !s.ShowPageNumber)));
        optionsMenu.Items.Add(CreateCheckMenuItem("_Auto Colors", Settings.AutoColors, () => ToggleSetting(s => s.AutoColors = !s.AutoColors)));
        optionsMenu.Items.Add(CreateCheckMenuItem("Scroll _Bars", Settings.ShowScrollBars, () => ToggleSetting(s => s.ShowScrollBars = !s.ShowScrollBars)));

        // Rendering
        var renderMenu = new MenuItem { Header = "Re_ndering" };
        renderMenu.Items.Add(CreateCheckMenuItem("_CPU (Default)", !Settings.UseGpuRendering, () => ToggleSetting(s => s.UseGpuRendering = false)));
        renderMenu.Items.Add(CreateCheckMenuItem("_GPU (SkiaSharp GL)", Settings.UseGpuRendering, () => ToggleSetting(s => s.UseGpuRendering = true)));
        optionsMenu.Items.Add(renderMenu);

        // optionsMenu.Items.Add(new Separator());
        // optionsMenu.Items.Add(CreateMenuItem("_Configure", "", () => { /* Future */ }));
        optionsMenu.Items.Add(CreateMenuItem("_Keyboard Shortcuts", "", () => 
        { 
            var dlg = new ShortcutConfigDialog(Settings) { Owner = this };
            dlg.ShowDialog();
            _settingsManager.QueueSave();
        }));
        optionsMenu.Items.Add(new Separator());

        MainContextMenu.Items.Add(optionsMenu);
    }

    private static MenuItem CreateMenuItem(string header, string shortcutHint, Action action)
    {
        var item = new MenuItem { Header = header, InputGestureText = shortcutHint };
        item.Click += (_, _) => action();
        return item;
    }

    private static MenuItem CreateCheckMenuItem(string header, bool isChecked, Action action)
    {
        var item = new MenuItem { Header = header, IsCheckable = true, IsChecked = isChecked };
        item.Click += (_, _) => action();
        return item;
    }

    private void RefreshRecentMenu(MenuItem menu, bool resumeAtPage)
    {
        menu.Items.Clear();
        var entries = _recentFiles.Entries;
        if (entries.Count == 0)
        {
            menu.Items.Add(new MenuItem { Header = "(No recent files)", IsEnabled = false });
            return;
        }
        foreach (var entry in entries.Take(15))
        {
            var e = entry;
            var name = Path.GetFileName(e.FilePath);
            var item = new MenuItem { Header = name, ToolTip = e.FilePath };
            item.Click += async (_, _) =>
            {
                await LoadFileAsync(e.FilePath, resumeAtPage ? e.LastPageIndex : 0);
            };
            menu.Items.Add(item);
        }
    }

    private static string FitModeToString(FitMode mode) => mode switch
    {
        FitMode.FitScreen => "Fit to _Screen",
        FitMode.FitHeight => "Fit _Height",
        FitMode.FitWidth => "Fit _Width",
        FitMode.FitPortrait => "Fit to _Portrait",
        FitMode.FitLandscape => "Fit to _Landscape",
        FitMode.OverHeight => "Over _Height",
        FitMode.OverWidth => "Over _Width",
        _ => mode.ToString()
    };

    // ═══════════════════════════════════════════════════════════
    // FILE LOADING
    // ═══════════════════════════════════════════════════════════

    private async Task ShowLoadFileDialog()
    {
        var dlg = new OpenFileDialog
        {
            Title = "Open Manga / Images",
            Filter = "All Supported|*.zip;*.cbz;*.rar;*.cbr;*.7z;*.cb7;*.jpg;*.jpeg;*.png;*.webp;*.gif;*.bmp;*.tif;*.tiff|" +
                     "Archives|*.zip;*.cbz;*.rar;*.cbr;*.7z;*.cb7|" +
                     "Images|*.jpg;*.jpeg;*.png;*.webp;*.gif;*.bmp;*.tif;*.tiff|" +
                     "All Files|*.*",
            Multiselect = true
        };

        if (dlg.ShowDialog(this) == true)
        {
            await LoadFilesAsync(dlg.FileNames);
        }
    }

    private async Task ShowOpenFolderDialog()
    {
        var dlg = new OpenFolderDialog { Title = "Open Folder of Images" };
        if (dlg.ShowDialog(this) == true)
            await LoadFolderAsync(dlg.FolderName);
    }

    private Task LoadFileAsync(string filePath, int startPage = 0)
    {
        return LoadFilesAsync(new[] { filePath }, startPage);
    }

    private async Task LoadFilesAsync(IEnumerable<string> filePaths, int startPage = 0)
    {
        try
        {
            var paths = filePaths.ToList();
            if (paths.Count == 0) return;

            if (paths.Count == 1)
            {
                var filePath = paths[0];
                var ext = Path.GetExtension(filePath);
                if (ImageExtensions.Contains(ext))
                {
                    // Single image — treat containing folder as virtual archive
                    var folder = Path.GetDirectoryName(filePath);
                    if (folder != null)
                    {
                        await LoadFolderAsync(folder);
                        if (_currentArchive != null)
                        {
                            var pages = await _currentArchive.GetPageListAsync();
                            var idx = pages.ToList().FindIndex(p =>
                                string.Equals(Path.GetFileName(p), Path.GetFileName(filePath), StringComparison.OrdinalIgnoreCase));
                            if (idx >= 0) GoToPage(idx);
                        }
                    }
                    return;
                }
            }

            string primaryPath = paths[0];
            string displayName = paths.Count > 1 ? $"{Path.GetFileName(primaryPath)} (+{paths.Count - 1} more)" : Path.GetFileName(primaryPath);
            ShowLoadingToast($"Loading \"{displayName}\"...");

            CleanupCurrentArchive();

            _currentArchive = ArchiveFactory.OpenMultiple(paths);
            _currentFilePath = primaryPath;

            var pageList = await _currentArchive.GetPageListAsync();
            _totalPages = pageList.Count;

            if (_totalPages == 0)
            {
                ShowSuccessToast("No images found in archive");
                return;
            }

            // Initialize cache and load archive
            var progress = new Progress<(int current, int total, string message)>(p =>
            {
                Dispatcher.Invoke(() => ShowLoadingToast(p.message));
            });

            _cacheManager = new CacheManager(Settings.CacheMemoryBudgetMB);

            // Determine start page: explicit > bookmark > 0
            var bookmarkedPage = _bookmarks.Get(primaryPath);
            var resumePage = startPage > 0 ? startPage : (bookmarkedPage ?? 0);
            resumePage = Math.Clamp(resumePage, 0, _totalPages - 1);

            await _cacheManager.LoadArchiveAsync(_currentArchive, resumePage, progress);

            GoToPage(resumePage);

            // Update recent files
            await _recentFiles.AddAsync(primaryPath, _currentPageIndex);

            ShowSuccessToast("Ready");
        }
        catch (Exception ex)
        {
            ShowSuccessToast($"Error: {ex.Message}");
        }
    }

    private async Task LoadFolderAsync(string folderPath)
    {
        ShowLoadingToast("Loading folder...");

        CleanupCurrentArchive();

        _currentArchive = new FolderReader(folderPath);
        _currentFilePath = folderPath;

        var pageList = await _currentArchive.GetPageListAsync();
        _totalPages = pageList.Count;

        if (_totalPages == 0)
        {
            ShowSuccessToast("No images found in folder");
            return;
        }

        var progress = new Progress<(int current, int total, string message)>(p =>
        {
            Dispatcher.Invoke(() => ShowLoadingToast(p.message));
        });

        _cacheManager = new CacheManager(Settings.CacheMemoryBudgetMB);
        await _cacheManager.LoadArchiveAsync(_currentArchive, 0, progress);

        GoToPage(0);
        ShowSuccessToast("Ready");
    }

    private async Task ResumeLastFile()
    {
        if (!string.IsNullOrEmpty(Settings.LastOpenedFile) && File.Exists(Settings.LastOpenedFile))
            await LoadFileAsync(Settings.LastOpenedFile, Settings.LastPageIndex);
    }

    private async Task LoadAdjacentFile(int direction)
    {
        if (string.IsNullOrEmpty(_currentFilePath)) return;

        try
        {
            var navigator = new FileNavigator(_currentFilePath);
            var adjacentFile = direction > 0 ? navigator.GetNextFile() : navigator.GetPreviousFile();
            if (adjacentFile != null)
                await LoadFileAsync(adjacentFile);
        }
        catch (DirectoryNotFoundException) { /* File's directory no longer exists */ }
    }

    private void CleanupCurrentArchive()
    {
        if (_currentFilePath != null)
        {
            _ = _bookmarks.SetAsync(_currentFilePath, _currentPageIndex);
            Settings.LastOpenedFile = _currentFilePath;
            Settings.LastPageIndex = _currentPageIndex;
        }

        _cacheManager?.Dispose();
        _cacheManager = null;
        _currentArchive = null;
        _activeBitmaps.Clear();
        _totalPages = 0;
        _currentPageIndex = 0;
        _panOffset = SKPoint.Empty;
    }

    // ═══════════════════════════════════════════════════════════
    // PAGE NAVIGATION
    // ═══════════════════════════════════════════════════════════

    private void NavigatePage(int delta)
    {
        if (_totalPages == 0) return;
        
        int step = delta;
        if (Settings.ViewMode == ViewMode.Double)
        {
            if (delta > 0 && !(Settings.DoublePage_Cover && _currentPageIndex == 0))
                step = 2;
            else if (delta < 0 && !(Settings.DoublePage_Cover && _currentPageIndex == 1))
                step = -2;
            else if (delta < 0 && Settings.DoublePage_Cover && _currentPageIndex == 1)
                step = -1;
            else if (delta > 0 && Settings.DoublePage_Cover && _currentPageIndex == 0)
                step = 1;
        }
        else if (Settings.ViewMode == ViewMode.Webtoon)
        {
            // Webtoon jumps by 3 pages if navigated via keyboard
            step = delta * 3;
        }

        GoToPage(_currentPageIndex + step);
    }

    private bool _isNavigating = false;
    private int _navigationGeneration = 0;

    private async void GoToPage(int pageIndex)
    {
        if (_totalPages == 0 || _cacheManager == null) return;
        if (_isNavigating) return;

        _isNavigating = true;
        try
        {
            pageIndex = Math.Clamp(pageIndex, 0, _totalPages - 1);
            _currentPageIndex = pageIndex;
            
            int currentGen = ++_navigationGeneration;
            var currentCache = _cacheManager;

            // Reset pan 
            _panOffset = SKPoint.Empty;
            
            var newBitmaps = new List<SKBitmap>();

            if (Settings.ViewMode == ViewMode.Single)
            {
                var bitmap = await currentCache.GetPageAsync(pageIndex);
                if (bitmap != null) newBitmaps.Add(bitmap);
            }
            else if (Settings.ViewMode == ViewMode.Double)
            {
                bool isCover = Settings.DoublePage_Cover && pageIndex == 0;
                var b1 = await currentCache.GetPageAsync(pageIndex);
                if (b1 != null) newBitmaps.Add(b1);

                if (currentGen != _navigationGeneration || _cacheManager != currentCache) return;

                if (!isCover && pageIndex + 1 < _totalPages)
                {
                    var b2 = await currentCache.GetPageAsync(pageIndex + 1);
                    if (b2 != null) newBitmaps.Add(b2);
                }
            }
            else if (Settings.ViewMode == ViewMode.Webtoon)
            {
                // Buffer 3 pages for Webtoon
                for (int i = 0; i < 3 && pageIndex + i < _totalPages; i++)
                {
                    var b = await currentCache.GetPageAsync(pageIndex + i);
                    if (b != null) newBitmaps.Add(b);
                    if (currentGen != _navigationGeneration || _cacheManager != currentCache) return;
                }
            }

            if (currentGen != _navigationGeneration || _cacheManager != currentCache) return;

            _activeBitmaps.Clear();
            _activeBitmaps.AddRange(newBitmaps);

            if (_activeBitmaps.Count > 0)
                SkiaCanvas.InvalidateVisual();

            ShowPageInfo();

            if (_currentFilePath != null)
                _ = _bookmarks.SetAsync(_currentFilePath, _currentPageIndex);
        }
        finally
        {
            _isNavigating = false;
        }
    }

    private void ShowGoToPageDialog()
    {
        if (_totalPages == 0) return;
        var dialog = new GoToPageDialog(_currentPageIndex + 1, _totalPages) { Owner = this };
        if (dialog.ShowDialog() == true)
            GoToPage(dialog.PageNumber - 1);
    }

    // ═══════════════════════════════════════════════════════════
    // SKIA RENDERING
    // ═══════════════════════════════════════════════════════════

    private void SkiaCanvas_PaintSurface(object? sender, SKPaintSurfaceEventArgs e)
    {
        var canvas = e.Surface.Canvas;
        var info = e.Info;

        canvas.Clear(SKColors.Black);

        if (_activeBitmaps.Count == 0) return;

        using var paint = new SKPaint
        {
            FilterQuality = SKFilterQuality.High,
            IsAntialias = true,
            IsDither = true
        };

        if (_rotationDegrees != 0)
        {
            canvas.Save();
            canvas.RotateDegrees(_rotationDegrees, info.Width / 2f, info.Height / 2f);
        }

        if (Settings.AutoColors)
        {
            // Boost contrast slightly and enhance darks for manga
            float c = 1.2f; // contrast
            float t = (1.0f - c) / 2.0f; // translation
            paint.ColorFilter = SKColorFilter.CreateColorMatrix(new float[]
            {
                c, 0, 0, 0, t,
                0, c, 0, 0, t,
                0, 0, c, 0, t,
                0, 0, 0, 1, 0
            });
        }

        if (Settings.ViewMode == ViewMode.Single)
        {
            var bmp = _activeBitmaps[0];
            var destRect = CalculateFitRect(info.Width, info.Height, bmp.Width, bmp.Height);
            ApplyZoomAndPan(ref destRect, info);
            canvas.DrawBitmap(bmp, new SKRect(0, 0, bmp.Width, bmp.Height), destRect, paint);
        }
        else if (Settings.ViewMode == ViewMode.Double)
        {
            if (_activeBitmaps.Count == 1)
            {
                var bmp = _activeBitmaps[0];
                var destRect = CalculateFitRect(info.Width, info.Height, bmp.Width, bmp.Height);
                ApplyZoomAndPan(ref destRect, info);
                canvas.DrawBitmap(bmp, new SKRect(0, 0, bmp.Width, bmp.Height), destRect, paint);
            }
            else
            {
                var b1 = _activeBitmaps[0];
                var b2 = _activeBitmaps[1];
                
                if (Settings.ReadingDirection == ReadingDirection.RightToLeft)
                    (b1, b2) = (b2, b1);

                float splitGap = Settings.DoublePage_Split ? 20f * (float)_zoomLevel : 0f;
                int combinedW = b1.Width + b2.Width;
                int maxH = Math.Max(b1.Height, b2.Height);

                var destRect = CalculateFitRect(info.Width, info.Height, combinedW, maxH);
                ApplyZoomAndPan(ref destRect, info);

                float b1Ratio = (float)b1.Width / combinedW;
                float b1DestW = destRect.Width * b1Ratio;
                
                var r1 = new SKRect(destRect.Left, destRect.Top, destRect.Left + b1DestW - splitGap / 2f, destRect.Bottom);
                var r2 = new SKRect(r1.Right + splitGap, destRect.Top, destRect.Right + splitGap, destRect.Bottom);

                canvas.DrawBitmap(b1, new SKRect(0, 0, b1.Width, b1.Height), r1, paint);
                canvas.DrawBitmap(b2, new SKRect(0, 0, b2.Width, b2.Height), r2, paint);

                if (Settings.DoublePage_Shadow)
                {
                    // Draw a book spine shadow at the crease
                    float creaseX = r1.Right + (splitGap / 2f);
                    float shadowWidth = 60f * (float)_zoomLevel;
                    
                    using var shadowPaint = new SKPaint();
                    shadowPaint.Shader = SKShader.CreateLinearGradient(
                        new SKPoint(creaseX - shadowWidth, 0),
                        new SKPoint(creaseX + shadowWidth, 0),
                        new[] { SKColors.Transparent, new SKColor(0, 0, 0, 150), new SKColor(0, 0, 0, 150), SKColors.Transparent },
                        new[] { 0f, 0.45f, 0.55f, 1f },
                        SKShaderTileMode.Clamp);
                        
                    canvas.DrawRect(creaseX - shadowWidth, Math.Min(r1.Top, r2.Top), shadowWidth * 2, Math.Max(r1.Height, r2.Height), shadowPaint);
                }

                if (Settings.DoublePage_Split)
                {
                    // Draw a dark line to clearly separate the pages
                    using var splitPaint = new SKPaint { Color = new SKColor(15, 15, 15), StrokeWidth = 2 };
                    float creaseX = r1.Right + (splitGap / 2f);
                    canvas.DrawLine(creaseX, Math.Min(r1.Top, r2.Top), creaseX, Math.Max(r1.Bottom, r2.Bottom), splitPaint);
                }
            }
        }
        else if (Settings.ViewMode == ViewMode.Webtoon)
        {
            float currentY = _panOffset.Y;
            float viewCenterX = info.Width / 2f;

            foreach (var bmp in _activeBitmaps)
            {
                float scale = (float)info.Width / bmp.Width; // Webtoon defaults to FitWidth logic
                if (_fitMode == FitMode.FitScreen || _fitMode == FitMode.FitHeight)
                    scale = (float)info.Height / bmp.Height;

                float drawW = bmp.Width * scale * (float)_zoomLevel;
                float drawH = bmp.Height * scale * (float)_zoomLevel;

                var destRect = new SKRect(viewCenterX - drawW / 2f, currentY, viewCenterX + drawW / 2f, currentY + drawH);
                destRect.Offset(_panOffset.X, 0); // X pan only

                canvas.DrawBitmap(bmp, new SKRect(0, 0, bmp.Width, bmp.Height), destRect, paint);
                currentY += drawH;
            }
        }

        if (_rotationDegrees != 0)
            canvas.Restore();
    }

    private void ApplyZoomAndPan(ref SKRect rect, SKImageInfo info)
    {
        if (Math.Abs(_zoomLevel - 1.0) > 0.001)
        {
            var cx = info.Width / 2f;
            var cy = info.Height / 2f;
            var nw = rect.Width * (float)_zoomLevel;
            var nh = rect.Height * (float)_zoomLevel;
            rect = new SKRect(cx - nw / 2f, cy - nh / 2f, cx + nw / 2f, cy + nh / 2f);
        }
        rect.Offset(_panOffset.X, _panOffset.Y);
    }

    private SKRect CalculateFitRect(int viewW, int viewH, int imgW, int imgH)
    {
        float scale = _fitMode switch
        {
            FitMode.FitScreen => Math.Min((float)viewW / imgW, (float)viewH / imgH),
            FitMode.FitWidth => (float)viewW / imgW,
            FitMode.FitHeight => (float)viewH / imgH,
            FitMode.FitPortrait => imgH > imgW
                ? Math.Min((float)viewW / imgW, (float)viewH / imgH)
                : (float)viewH / imgH,
            FitMode.FitLandscape => imgW > imgH
                ? Math.Min((float)viewW / imgW, (float)viewH / imgH)
                : (float)viewW / imgW,
            FitMode.OverHeight => (float)viewH / imgH,
            FitMode.OverWidth => (float)viewW / imgW,
            _ => 1f
        };

        var w = imgW * scale;
        var h = imgH * scale;
        return new SKRect((viewW - w) / 2f, (viewH - h) / 2f, (viewW + w) / 2f, (viewH + h) / 2f);
    }

    // ═══════════════════════════════════════════════════════════
    // ZOOM & FIT
    // ═══════════════════════════════════════════════════════════

    private void SetZoom(double zoom)
    {
        _zoomLevel = Math.Clamp(zoom, 0.1, 10.0);
        Settings.ZoomLevel = _zoomLevel;
        ClampPan();
        _settingsManager.QueueSave();
        SkiaCanvas.InvalidateVisual();
    }

    private void SetFitMode(FitMode mode)
    {
        _fitMode = mode;
        if (Settings.ViewMode == ViewMode.Single) Settings.FitModeSingle = mode;
        else if (Settings.ViewMode == ViewMode.Double) Settings.FitModeDouble = mode;
        else Settings.FitModeWebtoon = mode;
        
        _zoomLevel = 1.0;
        _panOffset = SKPoint.Empty;
        _settingsManager.QueueSave();
        SkiaCanvas.InvalidateVisual();
        BuildContextMenu();
    }

    private void SetRotation(int degrees)
    {
        _rotationDegrees = degrees;
        Settings.RotationDegrees = degrees;
        _settingsManager.QueueSave();
        SkiaCanvas.InvalidateVisual();
    }

    private void SetReadingDirection(ReadingDirection dir)
    {
        Settings.ReadingDirection = dir;
        _settingsManager.QueueSave();
        BuildContextMenu();
    }

    private void SetViewMode(ViewMode mode)
    {
        Settings.ViewMode = mode;
        if (mode == ViewMode.Single) _fitMode = Settings.FitModeSingle;
        else if (mode == ViewMode.Double) _fitMode = Settings.FitModeDouble;
        else _fitMode = Settings.FitModeWebtoon;

        _settingsManager.QueueSave();
        BuildContextMenu();
        GoToPage(_currentPageIndex);
    }

    private void ToggleSetting(Action<AppSettings> toggle)
    {
        toggle(Settings);
        _settingsManager.QueueSave();
        BuildContextMenu();
        SkiaCanvas.InvalidateVisual();
    }

    private void ShowCustomZoomDialog()
    {
        var dialog = new CustomZoomDialog(_zoomLevel * 100) { Owner = this };
        if (dialog.ShowDialog() == true)
            SetZoom(dialog.ZoomPercent / 100.0);
    }

    // ═══════════════════════════════════════════════════════════
    // SCROLLING & PANNING
    // ═══════════════════════════════════════════════════════════

    private void Scroll(float dx, float dy)
    {
        float oldY = _panOffset.Y;
        _panOffset = new SKPoint(_panOffset.X + dx, _panOffset.Y + dy);
        ClampPan();

        // If scrolling up/down hits the boundary in single/double mode, navigate pages
        if (dy < 0 && _panOffset.Y == oldY && Settings.ViewMode != ViewMode.Webtoon)
        {
            NavigatePage(1);
        }
        else if (dy > 0 && _panOffset.Y == oldY && Settings.ViewMode != ViewMode.Webtoon)
        {
            NavigatePage(-1);
        }
        else if (Settings.ViewMode == ViewMode.Webtoon && _activeBitmaps.Count > 0)
        {
            UpdateWebtoonPageInfo();
        }

        SkiaCanvas.InvalidateVisual();
    }

    private void UpdateWebtoonPageInfo()
    {
        float viewW = (float)SkiaCanvas.ActualWidth;
        float viewH = (float)SkiaCanvas.ActualHeight;
        if (viewW == 0 || viewH == 0) return;

        float currentY = 0;
        int topIndex = _currentPageIndex;
        foreach (var bmp in _activeBitmaps)
        {
            float scale = viewW / bmp.Width;
            if (_fitMode == FitMode.FitScreen || _fitMode == FitMode.FitHeight)
                scale = viewH / bmp.Height;

            float h = bmp.Height * scale * (float)_zoomLevel;
            // If viewport top is past the middle of this image, we consider the next image as active
            if (-_panOffset.Y > currentY + h / 2)
            {
                topIndex++;
            }
            currentY += h;
        }
        
        // Show info if page changed
        string currentText = $"{topIndex + 1} / {_totalPages}";
        if (PageInfoText.Text != currentText)
        {
            PageInfoText.Text = currentText;
            PageInfoBadge.BeginAnimation(OpacityProperty, new System.Windows.Media.Animation.DoubleAnimation(1, TimeSpan.FromMilliseconds(150)));
            
            _pageInfoTimer?.Stop();
            _pageInfoTimer = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromSeconds(2) };
            _pageInfoTimer.Tick += (_, _) =>
            {
                _pageInfoTimer.Stop();
                PageInfoBadge.BeginAnimation(OpacityProperty, new System.Windows.Media.Animation.DoubleAnimation(0, TimeSpan.FromMilliseconds(400)));
            };
            _pageInfoTimer.Start();
        }
    }

    private void ClampPan()
    {
        if (_activeBitmaps.Count == 0) return;

        float viewW = (float)SkiaCanvas.ActualWidth;
        float viewH = (float)SkiaCanvas.ActualHeight;
        if (viewW == 0 || viewH == 0) return;

        if (Settings.ViewMode == ViewMode.Single || (Settings.ViewMode == ViewMode.Double && _activeBitmaps.Count == 1))
        {
            var bmp = _activeBitmaps[0];
            var destRect = CalculateFitRect((int)viewW, (int)viewH, bmp.Width, bmp.Height);
            float drawW = destRect.Width * (float)_zoomLevel;
            float drawH = destRect.Height * (float)_zoomLevel;

            float maxX = Math.Max(0, (drawW - viewW) / 2f);
            float maxY = Math.Max(0, (drawH - viewH) / 2f);

            _panOffset.X = Math.Clamp(_panOffset.X, -maxX, maxX);
            _panOffset.Y = Math.Clamp(_panOffset.Y, -maxY, maxY);
        }
        else if (Settings.ViewMode == ViewMode.Double && _activeBitmaps.Count == 2)
        {
            int combinedW = _activeBitmaps[0].Width + _activeBitmaps[1].Width;
            int maxH = Math.Max(_activeBitmaps[0].Height, _activeBitmaps[1].Height);
            var destRect = CalculateFitRect((int)viewW, (int)viewH, combinedW, maxH);
            
            float drawW = destRect.Width * (float)_zoomLevel;
            float drawH = destRect.Height * (float)_zoomLevel;

            float maxX = Math.Max(0, (drawW - viewW) / 2f);
            float maxY = Math.Max(0, (drawH - viewH) / 2f);

            _panOffset.X = Math.Clamp(_panOffset.X, -maxX, maxX);
            _panOffset.Y = Math.Clamp(_panOffset.Y, -maxY, maxY);
        }
        else if (Settings.ViewMode == ViewMode.Webtoon)
        {
            float totalH = 0;
            float maxW = 0;
            foreach (var bmp in _activeBitmaps)
            {
                float scale = viewW / bmp.Width;
                if (_fitMode == FitMode.FitScreen || _fitMode == FitMode.FitHeight)
                    scale = viewH / bmp.Height;

                totalH += bmp.Height * scale * (float)_zoomLevel;
                maxW = Math.Max(maxW, bmp.Width * scale * (float)_zoomLevel);
            }

            float maxX = Math.Max(0, (maxW - viewW) / 2f);
            _panOffset.X = Math.Clamp(_panOffset.X, -maxX, maxX);

            // Webtoon pan Y: 0 is top. Negative values move camera down.
            // When totalH > viewH, panY can go down to -(totalH - viewH).
            float minY = totalH > viewH ? -(totalH - viewH) : 0;
            
            // Allow a small overscroll to trigger page navigation later if needed, but for now strict clamp
            _panOffset.Y = Math.Clamp(_panOffset.Y, minY, 0);
        }
    }

    private SKPoint _clickDownPos;
    private bool _isPotentialWindowDrag;

    private void OnMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ClickCount == 2)
        {
            if (_zoomLevel > 1.05) SetZoom(1.0);
            else SetZoom(2.0);
            e.Handled = true;
            return;
        }

        if (_zoomLevel > 1.05)
        {
            _isPanning = true;
            _panStart = new SKPoint((float)e.GetPosition(this).X - _panOffset.X,
                                    (float)e.GetPosition(this).Y - _panOffset.Y);
            CaptureMouse();
            e.Handled = true;
            return;
        }

        _isPotentialWindowDrag = true;
        _clickDownPos = new SKPoint((float)e.GetPosition(this).X, (float)e.GetPosition(this).Y);
        CaptureMouse();
        e.Handled = true;
    }

    private void OnMouseMove(object sender, MouseEventArgs e)
    {
        if (_isPanning && e.LeftButton == MouseButtonState.Pressed)
        {
            var pos = e.GetPosition(this);
            _panOffset = new SKPoint((float)pos.X - _panStart.X, (float)pos.Y - _panStart.Y);
            ClampPan();
            
            if (Settings.ViewMode == ViewMode.Webtoon && _activeBitmaps.Count > 0)
            {
                UpdateWebtoonPageInfo();
            }

            SkiaCanvas.InvalidateVisual();
        }
        else if (_isPotentialWindowDrag && e.LeftButton == MouseButtonState.Pressed)
        {
            var pos = e.GetPosition(this);
            var dx = pos.X - _clickDownPos.X;
            var dy = pos.Y - _clickDownPos.Y;
            if (Math.Abs(dx) > SystemParameters.MinimumHorizontalDragDistance ||
                Math.Abs(dy) > SystemParameters.MinimumVerticalDragDistance)
            {
                _isPotentialWindowDrag = false;
                ReleaseMouseCapture();
                
                if (WindowState == WindowState.Maximized)
                {
                    // Convert to normal window on drag from maximized
                    var pct = pos.X / ActualWidth;
                    WindowState = WindowState.Normal;
                    Top = pos.Y - 16;
                    Left = pos.X - (Width * pct);
                }
                
                try { DragMove(); } catch { }
            }
        }
    }

    private void OnMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (_isPanning) 
        { 
            _isPanning = false; 
            ReleaseMouseCapture(); 
        }
        else if (_isPotentialWindowDrag)
        {
            _isPotentialWindowDrag = false;
            ReleaseMouseCapture();
        }
    }

    // ═══════════════════════════════════════════════════════════
    // KEYBOARD INPUT
    // ═══════════════════════════════════════════════════════════

    private void OnKeyDown(object sender, KeyEventArgs e)
    {
        var ctrl = Keyboard.Modifiers.HasFlag(ModifierKeys.Control);
        var shift = Keyboard.Modifiers.HasFlag(ModifierKeys.Shift);

        string keyStr = e.Key.ToString();
        var actionMapping = Settings.KeyBindings.FirstOrDefault(x => string.Equals(x.Value, keyStr, StringComparison.OrdinalIgnoreCase));
        string action = actionMapping.Key;

        if (!string.IsNullOrEmpty(action) && !ctrl && !shift)
        {
            switch (action)
            {
                case "NextPage": NavigatePage(Settings.ReadingDirection == ReadingDirection.RightToLeft ? -1 : 1); break;
                case "PrevPage": NavigatePage(Settings.ReadingDirection == ReadingDirection.RightToLeft ? 1 : -1); break;
                case "PageDown": NavigatePage(1); break;
                case "PageUp": NavigatePage(-1); break;
                case "Home": GoToPage(0); break;
                case "End": GoToPage(_totalPages - 1); break;
                case "ScrollUp": Scroll(0, 50); break;
                case "ScrollDown": Scroll(0, -50); break;
                case "Fullscreen": ToggleFullScreen(); break;
                case "Maximize": ToggleFullDesktop(); break;
                case "TogglePageNumber": ToggleSetting(s => s.ShowPageNumber = !s.ShowPageNumber); break;
                case "ViewMode_Single": SetViewMode(ViewMode.Single); break;
                case "ViewMode_Double": SetViewMode(ViewMode.Double); break;
                case "ViewMode_Webtoon": SetViewMode(ViewMode.Webtoon); break;
                case "FitMode_Width": SetFitMode(FitMode.FitWidth); break;
                case "FitMode_Height": SetFitMode(FitMode.FitHeight); break;
                case "FitMode_Screen": SetFitMode(FitMode.FitScreen); break;
                case "Direction_RTL": SetReadingDirection(ReadingDirection.RightToLeft); break;
                case "Direction_LTR": SetReadingDirection(ReadingDirection.LeftToRight); break;
                case "ZoomIn": SetZoom(_zoomLevel * 1.25); break;
                case "ZoomOut": SetZoom(_zoomLevel / 1.25); break;
                case "OpenFile": _ = ShowLoadFileDialog(); break;
                case "NextArchive": _ = LoadAdjacentFile(1); break;
                case "PrevArchive": _ = LoadAdjacentFile(-1); break;
            }
            e.Handled = true;
            return;
        }

        // Hardcoded advanced combinations (modifiers)
        switch (e.Key)
        {
            case Key.Left when shift: Scroll(50, 0); break;
            case Key.Right when shift: Scroll(-50, 0); break;

            case Key.OemPlus when ctrl:
            case Key.Add when ctrl:
                SetZoom(_zoomLevel * 1.25); break;
            case Key.OemMinus when ctrl:
            case Key.Subtract when ctrl:
                SetZoom(_zoomLevel / 1.25); break;

            case Key.O when ctrl && shift: _ = ShowOpenFolderDialog(); break;
            case Key.O when ctrl: _ = ShowLoadFileDialog(); break;
            case Key.S when ctrl: SaveCurrentPageToFile(); break;
            case Key.C when ctrl: SaveCurrentPageToClipboard(); break;
            case Key.G when ctrl: ShowGoToPageDialog(); break;
            case Key.R when ctrl && shift: _ = ResumeLastFile(); break;

            case Key.OemCloseBrackets when ctrl: _ = LoadAdjacentFile(1); break;
            case Key.OemOpenBrackets when ctrl: _ = LoadAdjacentFile(-1); break;

            case Key.Escape:
                if (_isFullScreen) ToggleFullScreen();
                break;

            default: return; // Don't mark as handled
        }

        e.Handled = true;
    }

    private long _lastMouseWheelTime;

    private void OnMouseWheel(object sender, MouseWheelEventArgs e)
    {
        if (Keyboard.Modifiers.HasFlag(ModifierKeys.Control))
        {
            SetZoom(e.Delta > 0 ? _zoomLevel * 1.1 : _zoomLevel / 1.1);
        }
        else if (_zoomLevel > 1.05)
        {
            Scroll(0, e.Delta > 0 ? -40 : 40);
        }
        else
        {
            var now = Environment.TickCount64;
            if (now - _lastMouseWheelTime > 150)
            {
                _lastMouseWheelTime = now;
                NavigatePage(e.Delta > 0 ? -1 : 1);
            }
        }
    }

    // ═══════════════════════════════════════════════════════════
    // DRAG & DROP
    // ═══════════════════════════════════════════════════════════

    private void OnDragEnter(object sender, DragEventArgs e)
    {
        e.Effects = e.Data.GetDataPresent(DataFormats.FileDrop) ? DragDropEffects.Copy : DragDropEffects.None;
        e.Handled = true;
    }

    private async void OnFileDrop(object sender, DragEventArgs e)
    {
        if (e.Data.GetDataPresent(DataFormats.FileDrop))
        {
            var files = (string[])e.Data.GetData(DataFormats.FileDrop);
            if (files.Length > 0)
            {
                await LoadFilesAsync(files);
            }
        }
    }

    // ═══════════════════════════════════════════════════════════
    // FULLSCREEN
    // ═══════════════════════════════════════════════════════════

    private WindowState _preFullscreenState;
    private double _preFsLeft, _preFsTop, _preFsWidth, _preFsHeight;
    private bool _isFullScreen;

    private void ToggleFullScreen()
    {
        if (_isFullScreen)
        {
            WindowState = _preFullscreenState;
            Left = _preFsLeft; Top = _preFsTop;
            Width = _preFsWidth; Height = _preFsHeight;
            ResizeMode = ResizeMode.CanResizeWithGrip;
            Topmost = false;
            _isFullScreen = false;
        }
        else
        {
            _preFullscreenState = WindowState;
            _preFsLeft = Left; _preFsTop = Top;
            _preFsWidth = Width; _preFsHeight = Height;
            WindowState = WindowState.Normal;
            ResizeMode = ResizeMode.NoResize;
            Topmost = true;
            WindowState = WindowState.Maximized;
            _isFullScreen = true;
        }
    }

    private void ToggleFullDesktop()
    {
        if (_isFullScreen)
        {
            ToggleFullScreen(); // Restore from full screen first
        }

        WindowState = WindowState == WindowState.Maximized 
            ? WindowState.Normal 
            : WindowState.Maximized;
    }

    // ═══════════════════════════════════════════════════════════
    // SAVE
    // ═══════════════════════════════════════════════════════════

    private void SaveCurrentPageToFile()
    {
        if (_activeBitmaps.Count == 0) return;
        var bmp = _activeBitmaps[0];

        var dlg = new SaveFileDialog
        {
            Title = "Save Current Page",
            Filter = "PNG|*.png|JPEG|*.jpg|WebP|*.webp",
            FileName = $"page_{_currentPageIndex + 1}"
        };

        if (dlg.ShowDialog(this) == true)
        {
            var format = Path.GetExtension(dlg.FileName).ToLowerInvariant() switch
            {
                ".jpg" or ".jpeg" => SKEncodedImageFormat.Jpeg,
                ".webp" => SKEncodedImageFormat.Webp,
                _ => SKEncodedImageFormat.Png
            };

            using var image = SKImage.FromBitmap(bmp);
            using var data = image.Encode(format, 95);
            using var stream = File.OpenWrite(dlg.FileName);
            data.SaveTo(stream);
            ShowSuccessToast($"Saved to {Path.GetFileName(dlg.FileName)}");
        }
    }

    private void SaveCurrentPageToClipboard()
    {
        if (_activeBitmaps.Count == 0) return;
        var bmp = _activeBitmaps[0];

        using var image = SKImage.FromBitmap(bmp);
        using var data = image.Encode(SKEncodedImageFormat.Png, 100);
        using var stream = new MemoryStream(data.ToArray());

        var bitmapImage = new System.Windows.Media.Imaging.BitmapImage();
        bitmapImage.BeginInit();
        bitmapImage.CacheOption = System.Windows.Media.Imaging.BitmapCacheOption.OnLoad;
        bitmapImage.StreamSource = stream;
        bitmapImage.EndInit();
        bitmapImage.Freeze();

        Clipboard.SetImage(bitmapImage);
        ShowSuccessToast("Copied to clipboard");
    }

    // ═══════════════════════════════════════════════════════════
    // TOAST NOTIFICATIONS
    // ═══════════════════════════════════════════════════════════

    private void ShowLoadingToast(string message)
    {
        LoadingText.Text = message;
        SuccessToast.Opacity = 0;
        var fadeIn = new DoubleAnimation(1, TimeSpan.FromMilliseconds(200));
        LoadingToast.BeginAnimation(OpacityProperty, fadeIn);
        StartSpinner();
    }

    private void ShowSuccessToast(string message)
    {
        StopSpinner();
        LoadingToast.BeginAnimation(OpacityProperty, null);
        LoadingToast.Opacity = 0;

        SuccessText.Text = message;
        var fadeIn = new DoubleAnimation(1, TimeSpan.FromMilliseconds(200));
        SuccessToast.BeginAnimation(OpacityProperty, fadeIn);

        var timer = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromMilliseconds(1500) };
        timer.Tick += (_, _) =>
        {
            timer.Stop();
            SuccessToast.BeginAnimation(OpacityProperty, new DoubleAnimation(0, TimeSpan.FromMilliseconds(400)));
        };
        timer.Start();
    }

    private void ShowPageInfo()
    {
        if (!Settings.ShowPageNumber) return;

        PageInfoText.Text = $"{_currentPageIndex + 1} / {_totalPages}";
        PageInfoBadge.BeginAnimation(OpacityProperty, new DoubleAnimation(1, TimeSpan.FromMilliseconds(150)));

        _pageInfoTimer?.Stop();
        _pageInfoTimer = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromSeconds(2) };
        _pageInfoTimer.Tick += (_, _) =>
        {
            _pageInfoTimer.Stop();
            PageInfoBadge.BeginAnimation(OpacityProperty, new DoubleAnimation(0, TimeSpan.FromMilliseconds(400)));
        };
        _pageInfoTimer.Start();
    }

    private void StartSpinner()
    {
        _spinnerTimer ??= new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromMilliseconds(16) };
        _spinnerTimer.Tick += SpinnerTick;
        _spinnerTimer.Start();
    }

    private void StopSpinner()
    {
        if (_spinnerTimer != null) { _spinnerTimer.Tick -= SpinnerTick; _spinnerTimer.Stop(); }
    }

    private void SpinnerTick(object? sender, EventArgs e)
    {
        _spinnerAngle = (_spinnerAngle + 6) % 360;
        SpinnerRotation.Angle = _spinnerAngle;
    }

    // ═══════════════════════════════════════════════════════════
    // WINDOW STATE PERSISTENCE
    // ═══════════════════════════════════════════════════════════

    private void ApplyWindowState()
    {
        if (Settings.WindowWidth > 0 && Settings.WindowHeight > 0)
        {
            Left = Settings.WindowX;
            Top = Settings.WindowY;
            Width = Settings.WindowWidth;
            Height = Settings.WindowHeight;
        }
        if (Settings.IsMaximized) WindowState = WindowState.Maximized;
    }

    private void OnWindowSizeChanged(object sender, SizeChangedEventArgs e) => SkiaCanvas.InvalidateVisual();

    private async void OnWindowClosing(object? sender, System.ComponentModel.CancelEventArgs e)
    {
        if (WindowState == WindowState.Normal)
        {
            Settings.WindowX = (int)Left;
            Settings.WindowY = (int)Top;
            Settings.WindowWidth = (int)Width;
            Settings.WindowHeight = (int)Height;
        }
        Settings.IsMaximized = WindowState == WindowState.Maximized;
        Settings.FitMode = _fitMode;
        Settings.ZoomLevel = _zoomLevel;
        Settings.RotationDegrees = _rotationDegrees;

        if (_currentFilePath != null)
        {
            Settings.LastOpenedFile = _currentFilePath;
            Settings.LastPageIndex = _currentPageIndex;
            await _bookmarks.SetAsync(_currentFilePath, _currentPageIndex);
        }

        await _settingsManager.SaveNowAsync();

        _cacheManager?.Dispose();
        _cacheManager = null;
        _activeBitmaps.Clear();
        _settingsManager.Dispose();
    }
}
