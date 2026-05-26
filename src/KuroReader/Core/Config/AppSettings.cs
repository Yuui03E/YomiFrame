using System.Text.Json.Serialization;

namespace KuroReader.Core.Config;

/// <summary>
/// Defines how pages are fitted to the viewport.
/// </summary>
[JsonConverter(typeof(JsonStringEnumConverter<FitMode>))]
public enum FitMode
{
    /// <summary>Fit the entire page within the viewport (no cropping).</summary>
    FitScreen,
    /// <summary>Match page height to viewport height; scroll horizontally if needed.</summary>
    FitHeight,
    /// <summary>Match page width to viewport width; scroll vertically if needed.</summary>
    FitWidth,
    /// <summary>Fit portrait pages to height, landscape pages to width.</summary>
    FitPortrait,
    /// <summary>Fit landscape pages to height, portrait pages to width.</summary>
    FitLandscape,
    /// <summary>Scale to be at least viewport height; may exceed width.</summary>
    OverHeight,
    /// <summary>Scale to be at least viewport width; may exceed height.</summary>
    OverWidth
}

/// <summary>
/// Defines how pages are laid out.
/// </summary>
[JsonConverter(typeof(JsonStringEnumConverter<ViewMode>))]
public enum ViewMode
{
    Single,
    Double,
    Webtoon
}

/// <summary>
/// Manga page reading direction.
/// </summary>
[JsonConverter(typeof(JsonStringEnumConverter<ReadingDirection>))]
public enum ReadingDirection
{
    /// <summary>Japanese-style right-to-left reading order.</summary>
    RightToLeft,
    /// <summary>Western-style left-to-right reading order.</summary>
    LeftToRight
}

/// <summary>
/// Application settings POCO. All properties have sensible defaults.
/// Serialized to/from JSON by <see cref="SettingsManager"/>.
/// </summary>
public sealed class AppSettings
{
    // ── Window state ──────────────────────────────────────────────────

    /// <summary>Window X position in device-independent pixels.</summary>
    public int WindowX { get; set; } = 100;

    /// <summary>Window Y position in device-independent pixels.</summary>
    public int WindowY { get; set; } = 100;

    /// <summary>Window width in device-independent pixels.</summary>
    public int WindowWidth { get; set; } = 1280;

    /// <summary>Window height in device-independent pixels.</summary>
    public int WindowHeight { get; set; } = 720;

    /// <summary>Whether the window is maximized.</summary>
    public bool IsMaximized { get; set; }

    // ── Viewing ───────────────────────────────────────────────────────

    /// <summary>How pages are fitted to the viewport for Single view.</summary>
    public FitMode FitModeSingle { get; set; } = FitMode.FitHeight;

    /// <summary>How pages are fitted to the viewport for Double view.</summary>
    public FitMode FitModeDouble { get; set; } = FitMode.FitHeight;

    /// <summary>How pages are fitted to the viewport for Webtoon view.</summary>
    public FitMode FitModeWebtoon { get; set; } = FitMode.FitWidth;

    /// <summary>Legacy FitMode, keeping for backward compatibility if needed, but not used actively for the modes.</summary>
    public FitMode FitMode { get; set; } = FitMode.FitHeight;

    /// <summary>Current zoom level. 1.0 = 100%.</summary>
    public double ZoomLevel { get; set; } = 1.0;

    /// <summary>Reading layout mode (Single, Double, Webtoon).</summary>
    public ViewMode ViewMode { get; set; } = ViewMode.Single;

    /// <summary>Page reading direction. Default is right-to-left (manga standard).</summary>
    public ReadingDirection ReadingDirection { get; set; } = ReadingDirection.RightToLeft;

    // ── Double page ───────────────────────────────────────────────────

    /// <summary>Whether double-page (spread) mode is enabled.</summary>
    public bool DoublePageEnabled { get; set; }

    /// <summary>Treat the first page as a cover (display alone).</summary>
    public bool DoublePage_Cover { get; set; } = true;

    /// <summary>Draw a shadow between the two pages in spread mode.</summary>
    public bool DoublePage_Shadow { get; set; }

    /// <summary>Advance by one page instead of two in spread mode.</summary>
    public bool DoublePage_ForwardOne { get; set; }

    /// <summary>Split wide pages into two halves in spread mode.</summary>
    public bool DoublePage_Split { get; set; }

    // ── Rotation ──────────────────────────────────────────────────────

    /// <summary>Page rotation in degrees. Valid values: 0, 90, 180, 270.</summary>
    public int RotationDegrees { get; set; }

    // ── Appearance ────────────────────────────────────────────────────

    /// <summary>Automatically derive UI accent colors from page content.</summary>
    public bool AutoColors { get; set; }

    /// <summary>Show scrollbars on the page viewport.</summary>
    public bool ShowScrollBars { get; set; }

    /// <summary>Whether to show the page number indicator when changing pages.</summary>
    public bool ShowPageNumber { get; set; } = true;

    // ── Rendering ─────────────────────────────────────────────────────

    /// <summary>Use GPU-accelerated SkiaSharp rendering when available.</summary>
    public bool UseGpuRendering { get; set; }

    // ── Session state ─────────────────────────────────────────────────

    /// <summary>Path to the last opened file or folder.</summary>
    public string? LastOpenedFile { get; set; }

    /// <summary>Zero-based page index of the last viewed page.</summary>
    public int LastPageIndex { get; set; }

    // ── Performance ───────────────────────────────────────────────────

    /// <summary>Maximum memory budget for the decoded page cache (L2) in megabytes.</summary>
    public int CacheMemoryBudgetMB { get; set; } = 500;

    // ── Input ─────────────────────────────────────────────────────────

    /// <summary>Customizable simple keyboard shortcuts mapping Actions to Key names.</summary>
    public System.Collections.Generic.Dictionary<string, string> KeyBindings { get; set; } = new(System.StringComparer.OrdinalIgnoreCase)
    {
        { "NextPage", "Right" },
        { "PrevPage", "Left" },
        { "PageDown", "PageDown" },
        { "PageUp", "PageUp" },
        { "Home", "Home" },
        { "End", "End" },
        { "ScrollUp", "Up" },
        { "ScrollDown", "Down" },
        { "Fullscreen", "F11" },
        { "Maximize", "F12" },
        { "TogglePageNumber", "P" },
        { "ViewMode_Single", "D1" },
        { "ViewMode_Double", "D2" },
        { "ViewMode_Webtoon", "D3" },
        { "FitMode_Width", "W" },
        { "FitMode_Height", "H" },
        { "FitMode_Screen", "F" },
        { "Direction_RTL", "R" },
        { "Direction_LTR", "L" },
        { "ZoomIn", "Add" },
        { "ZoomOut", "Subtract" },
        { "OpenFile", "O" },
        { "NextArchive", "OemCloseBrackets" },
        { "PrevArchive", "OemOpenBrackets" }
    };
}
