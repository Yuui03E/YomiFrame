using System.Text.Json;
using System.Text.Json.Serialization;

namespace KuroReader.Core.Config;

/// <summary>
/// A single entry in the recent files list.
/// </summary>
public sealed class RecentFileEntry
{
    /// <summary>Full path to the file or folder.</summary>
    public string FilePath { get; set; } = string.Empty;

    /// <summary>Zero-based index of the last viewed page.</summary>
    public int LastPageIndex { get; set; }

    /// <summary>UTC timestamp of when the file was last opened.</summary>
    public DateTime LastOpened { get; set; } = DateTime.UtcNow;
}

/// <summary>
/// Tracks the most recently opened files (up to 30) with per-file reading position.
/// Persists to <c>%APPDATA%\KuroReader\recent.json</c>.
/// </summary>
public sealed class RecentFiles
{
    private static readonly string SettingsDirectory =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "KuroReader");

    private static readonly string RecentFilePath =
        Path.Combine(SettingsDirectory, "recent.json");

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    private const int MaxEntries = 30;

    private readonly SemaphoreSlim _lock = new(1, 1);
    private List<RecentFileEntry> _entries = new();

    /// <summary>
    /// Gets a read-only snapshot of the current recent files list, ordered most-recent-first.
    /// </summary>
    public IReadOnlyList<RecentFileEntry> Entries => _entries.AsReadOnly();

    /// <summary>
    /// Loads the recent files list from disk. If the file is missing or corrupt, starts empty.
    /// </summary>
    public async Task LoadAsync()
    {
        if (!File.Exists(RecentFilePath))
        {
            _entries = new List<RecentFileEntry>();
            return;
        }

        try
        {
            await using var stream = new FileStream(
                RecentFilePath,
                FileMode.Open,
                FileAccess.Read,
                FileShare.Read,
                bufferSize: 4096,
                FileOptions.Asynchronous | FileOptions.SequentialScan);

            var loaded = await JsonSerializer.DeserializeAsync<List<RecentFileEntry>>(stream, JsonOptions)
                .ConfigureAwait(false);

            _entries = loaded ?? new List<RecentFileEntry>();
        }
        catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException)
        {
            _entries = new List<RecentFileEntry>();
        }
    }

    /// <summary>
    /// Adds or updates a file in the recent list. If the file already exists, its page index
    /// and timestamp are updated. The list is trimmed to <see cref="MaxEntries"/> and saved.
    /// </summary>
    /// <param name="filePath">Full path to the file.</param>
    /// <param name="lastPageIndex">Zero-based page index.</param>
    public async Task AddAsync(string filePath, int lastPageIndex)
    {
        await _lock.WaitAsync().ConfigureAwait(false);
        try
        {
            // Remove existing entry for this path (case-insensitive on Windows).
            _entries.RemoveAll(e =>
                string.Equals(e.FilePath, filePath, StringComparison.OrdinalIgnoreCase));

            // Insert at the beginning (most recent first).
            _entries.Insert(0, new RecentFileEntry
            {
                FilePath = filePath,
                LastPageIndex = lastPageIndex,
                LastOpened = DateTime.UtcNow
            });

            // Trim to max.
            if (_entries.Count > MaxEntries)
                _entries.RemoveRange(MaxEntries, _entries.Count - MaxEntries);

            await SaveAsync().ConfigureAwait(false);
        }
        finally
        {
            _lock.Release();
        }
    }

    /// <summary>
    /// Removes a file from the recent list.
    /// </summary>
    /// <param name="filePath">Full path to remove.</param>
    public async Task RemoveAsync(string filePath)
    {
        await _lock.WaitAsync().ConfigureAwait(false);
        try
        {
            int removed = _entries.RemoveAll(e =>
                string.Equals(e.FilePath, filePath, StringComparison.OrdinalIgnoreCase));

            if (removed > 0)
                await SaveAsync().ConfigureAwait(false);
        }
        finally
        {
            _lock.Release();
        }
    }

    /// <summary>
    /// Gets the recent entry for a file path, or null if not found.
    /// </summary>
    public RecentFileEntry? Get(string filePath)
    {
        return _entries.FirstOrDefault(e =>
            string.Equals(e.FilePath, filePath, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// Clears all recent file entries and saves.
    /// </summary>
    public async Task ClearAsync()
    {
        await _lock.WaitAsync().ConfigureAwait(false);
        try
        {
            _entries.Clear();
            await SaveAsync().ConfigureAwait(false);
        }
        finally
        {
            _lock.Release();
        }
    }

    private async Task SaveAsync()
    {
        Directory.CreateDirectory(SettingsDirectory);

        string tempPath = RecentFilePath + ".tmp";

        await using (var stream = new FileStream(
            tempPath,
            FileMode.Create,
            FileAccess.Write,
            FileShare.None,
            bufferSize: 4096,
            FileOptions.Asynchronous | FileOptions.WriteThrough))
        {
            await JsonSerializer.SerializeAsync(stream, _entries, JsonOptions).ConfigureAwait(false);
        }

        File.Move(tempPath, RecentFilePath, overwrite: true);
    }
}
