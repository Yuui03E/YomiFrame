using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace KuroReader.Views;

/// <summary>
/// Shared helpers for building dark-themed dialog UIs.
/// </summary>
internal static class DialogHelper
{
    private static readonly SolidColorBrush BgBrush = new(Color.FromRgb(26, 26, 26));
    private static readonly SolidColorBrush BorderBr = new(Color.FromRgb(51, 51, 51));
    private static readonly SolidColorBrush InputBg = new(Color.FromRgb(42, 42, 42));
    private static readonly SolidColorBrush InputBorder = new(Color.FromRgb(80, 80, 80));
    private static readonly SolidColorBrush AccentBrush = new(Color.FromRgb(108, 99, 255));
    private static readonly SolidColorBrush ErrorBrush = new(Color.FromRgb(232, 17, 35));

    public static void StyleDialog(Window w, double width = 280, double height = 130)
    {
        w.Width = width;
        w.Height = height;
        w.WindowStartupLocation = WindowStartupLocation.CenterOwner;
        w.WindowStyle = WindowStyle.None;
        w.AllowsTransparency = true;
        w.Background = BgBrush;
        w.ResizeMode = ResizeMode.NoResize;
        w.ShowInTaskbar = false;
        w.MouseLeftButtonDown += (_, _) => w.DragMove();
    }

    public static Border CreateDialogBorder()
    {
        return new Border
        {
            BorderBrush = BorderBr,
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(8),
            Padding = new Thickness(20),
            Background = BgBrush
        };
    }

    public static TextBlock CreateLabel(string text)
    {
        return new TextBlock
        {
            Text = text,
            Foreground = Brushes.White,
            FontFamily = new FontFamily("Segoe UI"),
            FontSize = 13,
            Margin = new Thickness(0, 0, 0, 10)
        };
    }

    public static TextBox CreateInput(string value)
    {
        return new TextBox
        {
            Text = value,
            FontSize = 14,
            FontFamily = new FontFamily("Segoe UI"),
            Background = InputBg,
            Foreground = Brushes.White,
            BorderBrush = InputBorder,
            BorderThickness = new Thickness(1),
            Padding = new Thickness(8, 4, 8, 4),
            CaretBrush = Brushes.White,
            SelectionBrush = AccentBrush
        };
    }

    public static Button CreateButton(string text, Action action)
    {
        var btn = new Button
        {
            Content = text,
            Padding = new Thickness(16, 6, 16, 6),
            Margin = new Thickness(6, 0, 0, 0),
            FontSize = 12,
            FontFamily = new FontFamily("Segoe UI"),
            Background = InputBg,
            Foreground = Brushes.White,
            BorderBrush = InputBorder,
            BorderThickness = new Thickness(1),
            Cursor = Cursors.Hand
        };
        btn.Click += (_, _) => action();
        return btn;
    }

    public static void MarkError(TextBox input)
    {
        input.BorderBrush = ErrorBrush;
    }
}

/// <summary>
/// Jump to a specific page number.
/// </summary>
public class GoToPageDialog : Window
{
    private readonly TextBox _pageInput;
    private readonly int _totalPages;
    public int PageNumber { get; private set; }

    public GoToPageDialog(int currentPage, int totalPages)
    {
        _totalPages = totalPages;
        DialogHelper.StyleDialog(this);

        var border = DialogHelper.CreateDialogBorder();
        var stack = new StackPanel();

        stack.Children.Add(DialogHelper.CreateLabel($"Go to page (1 – {totalPages}):"));

        _pageInput = DialogHelper.CreateInput(currentPage.ToString());
        _pageInput.KeyDown += (_, e) =>
        {
            if (e.Key == Key.Enter) TryAccept();
            else if (e.Key == Key.Escape) DialogResult = false;
        };
        stack.Children.Add(_pageInput);

        var btnPanel = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            Margin = new Thickness(0, 12, 0, 0)
        };
        btnPanel.Children.Add(DialogHelper.CreateButton("Cancel", () => DialogResult = false));
        btnPanel.Children.Add(DialogHelper.CreateButton("Go", TryAccept));
        stack.Children.Add(btnPanel);

        border.Child = stack;
        Content = border;

        Loaded += (_, _) => { _pageInput.Focus(); _pageInput.SelectAll(); };
    }

    private void TryAccept()
    {
        if (int.TryParse(_pageInput.Text, out var num) && num >= 1 && num <= _totalPages)
        {
            PageNumber = num;
            DialogResult = true;
        }
        else DialogHelper.MarkError(_pageInput);
    }
}

/// <summary>
/// Custom zoom percentage input.
/// </summary>
public class CustomZoomDialog : Window
{
    private readonly TextBox _zoomInput;
    public double ZoomPercent { get; private set; }

    public CustomZoomDialog(double currentPercent)
    {
        DialogHelper.StyleDialog(this);

        var border = DialogHelper.CreateDialogBorder();
        var stack = new StackPanel();

        stack.Children.Add(DialogHelper.CreateLabel("Zoom percentage (10 – 1000):"));

        _zoomInput = DialogHelper.CreateInput(((int)currentPercent).ToString());
        _zoomInput.KeyDown += (_, e) =>
        {
            if (e.Key == Key.Enter) TryAccept();
            else if (e.Key == Key.Escape) DialogResult = false;
        };
        stack.Children.Add(_zoomInput);

        var btnPanel = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            Margin = new Thickness(0, 12, 0, 0)
        };
        btnPanel.Children.Add(DialogHelper.CreateButton("Cancel", () => DialogResult = false));
        btnPanel.Children.Add(DialogHelper.CreateButton("OK", TryAccept));
        stack.Children.Add(btnPanel);

        border.Child = stack;
        Content = border;

        Loaded += (_, _) => { _zoomInput.Focus(); _zoomInput.SelectAll(); };
    }

    private void TryAccept()
    {
        if (double.TryParse(_zoomInput.Text, out var pct) && pct >= 10 && pct <= 1000)
        {
            ZoomPercent = pct;
            DialogResult = true;
        }
        else DialogHelper.MarkError(_zoomInput);
    }
}

/// <summary>
/// Configure simple keyboard shortcuts.
/// </summary>
public class ShortcutConfigDialog : Window
{
    private readonly KuroReader.Core.Config.AppSettings _settings;
    private Button? _listeningButton;
    private string? _listeningAction;

    public ShortcutConfigDialog(KuroReader.Core.Config.AppSettings settings)
    {
        _settings = settings;
        DialogHelper.StyleDialog(this, 350, 480);

        var border = DialogHelper.CreateDialogBorder();
        var mainStack = new StackPanel();

        var title = DialogHelper.CreateLabel("Keyboard Shortcuts");
        title.FontWeight = FontWeights.Bold;
        title.FontSize = 14;
        mainStack.Children.Add(title);

        var scroll = new ScrollViewer 
        { 
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            Height = 360,
            Margin = new Thickness(0, 0, 0, 10)
        };
        
        var listStack = new StackPanel();
        
        // Group bindings logically
        var groups = new[]
        {
            ("Navigation", new[] { "NextPage", "PrevPage", "PageDown", "PageUp", "Home", "End", "ScrollUp", "ScrollDown" }),
            ("View & Zoom", new[] { "ViewMode_Single", "ViewMode_Double", "ViewMode_Webtoon", "ZoomIn", "ZoomOut", "Fullscreen", "Maximize" }),
            ("Fit Modes", new[] { "FitMode_Width", "FitMode_Height", "FitMode_Screen" }),
            ("Others", new[] { "Direction_RTL", "Direction_LTR", "TogglePageNumber", "OpenFile", "NextArchive", "PrevArchive" })
        };

        foreach (var (groupName, keys) in groups)
        {
            var header = new TextBlock
            {
                Text = groupName.ToUpper(),
                Foreground = new SolidColorBrush(Color.FromRgb(150, 150, 150)),
                FontSize = 10,
                FontWeight = FontWeights.SemiBold,
                Margin = new Thickness(0, 8, 0, 4)
            };
            listStack.Children.Add(header);

            foreach (var actionKey in keys)
            {
                if (!_settings.KeyBindings.TryGetValue(actionKey, out string? val)) continue;

                var row = new Grid { Margin = new Thickness(0, 1, 0, 1) };
                row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(100) });

                var displayName = actionKey.Replace("_", " ");

                var lbl = new TextBlock
                {
                    Text = displayName,
                    Foreground = Brushes.White,
                    VerticalAlignment = VerticalAlignment.Center,
                    FontSize = 12,
                    FontFamily = new FontFamily("Segoe UI")
                };
                Grid.SetColumn(lbl, 0);
                row.Children.Add(lbl);

                var btn = new Button
                {
                    Content = val,
                    Padding = new Thickness(0),
                    Height = 22,
                    FontSize = 11,
                    FontFamily = new FontFamily("Segoe UI"),
                    Background = new SolidColorBrush(Color.FromRgb(42, 42, 42)),
                    Foreground = Brushes.White,
                    BorderBrush = new SolidColorBrush(Color.FromRgb(80, 80, 80)),
                    BorderThickness = new Thickness(1),
                    Cursor = Cursors.Hand
                };
                btn.Click += (s, e) => StartListening(btn, actionKey);
                
                Grid.SetColumn(btn, 1);
                row.Children.Add(btn);

                listStack.Children.Add(row);
            }
        }

        scroll.Content = listStack;
        mainStack.Children.Add(scroll);

        var btnPanel = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right
        };
        btnPanel.Children.Add(DialogHelper.CreateButton("Done", () => DialogResult = true));
        mainStack.Children.Add(btnPanel);

        border.Child = mainStack;
        Content = border;

        KeyDown += OnWindowKeyDown;
    }

    private void StartListening(Button btn, string action)
    {
        if (_listeningButton != null)
        {
            _listeningButton.Content = _settings.KeyBindings[_listeningAction!];
        }

        _listeningButton = btn;
        _listeningAction = action;
        btn.Content = "Press key...";
    }

    private void OnWindowKeyDown(object sender, KeyEventArgs e)
    {
        if (_listeningButton != null && _listeningAction != null)
        {
            e.Handled = true;
            
            // Ignore modifiers themselves
            if (e.Key == Key.LeftShift || e.Key == Key.RightShift ||
                e.Key == Key.LeftCtrl || e.Key == Key.RightCtrl ||
                e.Key == Key.LeftAlt || e.Key == Key.RightAlt ||
                e.Key == Key.LWin || e.Key == Key.RWin)
            {
                return;
            }

            string keyName = e.Key.ToString();
            _settings.KeyBindings[_listeningAction] = keyName;
            _listeningButton.Content = keyName;
            
            _listeningButton = null;
            _listeningAction = null;
        }
        else if (e.Key == Key.Escape)
        {
            DialogResult = true;
        }
    }
}
