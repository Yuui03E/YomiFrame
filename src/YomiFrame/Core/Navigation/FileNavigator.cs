using YomiFrame.Core.Archives;
using YomiFrame.Helpers;

namespace YomiFrame.Core.Navigation;

/// <summary>
/// Navigates between supported files (archives and images) within a directory.
/// Given a current file path, finds the next/previous supported file using natural sort order.
/// </summary>
public sealed class FileNavigator
{
    private static readonly HashSet<string> SupportedExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".zip", ".cbz", ".rar", ".cbr", ".7z", ".cb7",
        ".jpg", ".jpeg", ".png", ".webp", ".gif", ".bmp", ".tiff", ".tif"
    };

    private readonly string _directory;
    private List<string>? _files;
    private int _currentIndex = -1;

    /// <summary>
    /// Gets the directory being navigated.
    /// </summary>
    public string Directory => _directory;

    /// <summary>
    /// Gets the current file path, or null if no file is selected.
    /// </summary>
    public string? CurrentFile => _currentIndex >= 0 && _files is not null && _currentIndex < _files.Count
        ? _files[_currentIndex]
        : null;

    /// <summary>
    /// Gets the total number of supported files in the directory.
    /// </summary>
    public int FileCount => _files?.Count ?? 0;

    /// <summary>
    /// Gets the current file's zero-based index within the sorted file list.
    /// </summary>
    public int CurrentIndex => _currentIndex;

    /// <summary>
    /// Gets or sets whether navigation wraps around at the beginning/end.
    /// </summary>
    public bool WrapAround { get; set; }

    /// <summary>
    /// Creates a file navigator for the directory containing the specified file.
    /// </summary>
    /// <param name="currentFilePath">Path to the currently open file.</param>
    /// <exception cref="ArgumentException">Thrown if the file path is null or empty.</exception>
    /// <exception cref="DirectoryNotFoundException">Thrown if the parent directory doesn't exist.</exception>
    public FileNavigator(string currentFilePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(currentFilePath);

        string? dir = Path.GetDirectoryName(Path.GetFullPath(currentFilePath));
        if (dir is null || !System.IO.Directory.Exists(dir))
            throw new DirectoryNotFoundException(
                $"Cannot determine or find the directory for: {currentFilePath}");

        _directory = dir;
        Refresh(currentFilePath);
    }

    /// <summary>
    /// Creates a file navigator for a specific directory, optionally selecting an initial file.
    /// </summary>
    /// <param name="directory">The directory to navigate.</param>
    /// <param name="initialFile">Optional initial file to select.</param>
    public FileNavigator(string directory, string? initialFile)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(directory);

        if (!System.IO.Directory.Exists(directory))
            throw new DirectoryNotFoundException($"Directory not found: {directory}");

        _directory = directory;
        Refresh(initialFile);
    }

    /// <summary>
    /// Re-scans the directory for supported files and re-establishes the current position.
    /// </summary>
    /// <param name="currentFilePath">The file to select after refresh, or null to keep position.</param>
    public void Refresh(string? currentFilePath = null)
    {
        var comparer = NaturalSortComparer.Instance;

        _files = System.IO.Directory.EnumerateFiles(_directory)
            .Where(f => SupportedExtensions.Contains(Path.GetExtension(f)))
            .OrderBy(f => Path.GetFileName(f), comparer)
            .ToList();

        _currentIndex = -1;

        if (currentFilePath is not null)
        {
            string fullPath = Path.GetFullPath(currentFilePath);
            _currentIndex = _files.FindIndex(f =>
                string.Equals(Path.GetFullPath(f), fullPath, StringComparison.OrdinalIgnoreCase));
        }

        // If not found and we have files, default to the first one.
        if (_currentIndex < 0 && _files.Count > 0)
            _currentIndex = 0;
    }

    /// <summary>
    /// Returns the path of the next file in natural sort order, or null if at the end
    /// (and <see cref="WrapAround"/> is false).
    /// </summary>
    /// <returns>Path to the next file, or null.</returns>
    public string? GetNextFile()
    {
        if (_files is null || _files.Count == 0)
            return null;

        int nextIndex = _currentIndex + 1;

        if (nextIndex >= _files.Count)
        {
            if (!WrapAround)
                return null;
            nextIndex = 0;
        }

        _currentIndex = nextIndex;
        return _files[_currentIndex];
    }

    /// <summary>
    /// Returns the path of the previous file in natural sort order, or null if at the beginning
    /// (and <see cref="WrapAround"/> is false).
    /// </summary>
    /// <returns>Path to the previous file, or null.</returns>
    public string? GetPreviousFile()
    {
        if (_files is null || _files.Count == 0)
            return null;

        int prevIndex = _currentIndex - 1;

        if (prevIndex < 0)
        {
            if (!WrapAround)
                return null;
            prevIndex = _files.Count - 1;
        }

        _currentIndex = prevIndex;
        return _files[_currentIndex];
    }

    /// <summary>
    /// Peeks at the next file without changing the current position.
    /// </summary>
    public string? PeekNextFile()
    {
        if (_files is null || _files.Count == 0)
            return null;

        int nextIndex = _currentIndex + 1;
        if (nextIndex >= _files.Count)
            return WrapAround ? _files[0] : null;

        return _files[nextIndex];
    }

    /// <summary>
    /// Peeks at the previous file without changing the current position.
    /// </summary>
    public string? PeekPreviousFile()
    {
        if (_files is null || _files.Count == 0)
            return null;

        int prevIndex = _currentIndex - 1;
        if (prevIndex < 0)
            return WrapAround ? _files[^1] : null;

        return _files[prevIndex];
    }

    /// <summary>
    /// Returns the sorted list of all supported files in the directory.
    /// </summary>
    public IReadOnlyList<string> GetAllFiles()
    {
        return _files?.AsReadOnly() ?? (IReadOnlyList<string>)Array.Empty<string>();
    }
}
