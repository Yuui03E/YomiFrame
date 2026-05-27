using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace YomiFrame.Core.Config;

/// <summary>
/// Per-file bookmarks that persist the last viewed page index for each file.
/// Files are identified by a SHA-256 hash of their absolute path (not content — too slow for large files).
/// Persists to <c>%APPDATA%\YomiFrame\bookmarks.json</c>.
/// </summary>
public sealed class Bookmarks
{
    private static readonly string SettingsDirectory =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "YomiFrame");

    private static readonly string BookmarksFilePath =
        Path.Combine(SettingsDirectory, "bookmarks.json");

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    private readonly SemaphoreSlim _lock = new(1, 1);
    private Dictionary<string, int> _bookmarks = new(StringComparer.Ordinal);

    /// <summary>
    /// Loads bookmarks from disk. Missing or corrupt file → empty dictionary.
    /// </summary>
    public async Task LoadAsync()
    {
        if (!File.Exists(BookmarksFilePath))
        {
            _bookmarks = new Dictionary<string, int>(StringComparer.Ordinal);
            return;
        }

        try
        {
            await using var stream = new FileStream(
                BookmarksFilePath,
                FileMode.Open,
                FileAccess.Read,
                FileShare.Read,
                bufferSize: 4096,
                FileOptions.Asynchronous | FileOptions.SequentialScan);

            var loaded = await JsonSerializer.DeserializeAsync<Dictionary<string, int>>(stream, JsonOptions)
                .ConfigureAwait(false);

            _bookmarks = loaded ?? new Dictionary<string, int>(StringComparer.Ordinal);
        }
        catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException)
        {
            _bookmarks = new Dictionary<string, int>(StringComparer.Ordinal);
        }
    }

    /// <summary>
    /// Sets (or updates) the bookmarked page index for the given file path.
    /// </summary>
    /// <param name="filePath">Absolute path to the archive or folder.</param>
    /// <param name="pageIndex">Zero-based page index.</param>
    public async Task SetAsync(string filePath, int pageIndex)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(filePath);

        string key = HashFilePath(filePath);

        await _lock.WaitAsync().ConfigureAwait(false);
        try
        {
            _bookmarks[key] = pageIndex;
            await SaveAsync().ConfigureAwait(false);
        }
        finally
        {
            _lock.Release();
        }
    }

    /// <summary>
    /// Gets the bookmarked page index for the given file path, or null if no bookmark exists.
    /// </summary>
    /// <param name="filePath">Absolute path to the archive or folder.</param>
    /// <returns>The saved page index, or null.</returns>
    public int? Get(string filePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(filePath);
        string key = HashFilePath(filePath);
        return _bookmarks.TryGetValue(key, out int index) ? index : null;
    }

    /// <summary>
    /// Removes the bookmark for the given file path, if it exists.
    /// </summary>
    /// <param name="filePath">Absolute path.</param>
    public async Task RemoveAsync(string filePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(filePath);
        string key = HashFilePath(filePath);

        await _lock.WaitAsync().ConfigureAwait(false);
        try
        {
            if (_bookmarks.Remove(key))
                await SaveAsync().ConfigureAwait(false);
        }
        finally
        {
            _lock.Release();
        }
    }

    /// <summary>
    /// Clears all bookmarks.
    /// </summary>
    public async Task ClearAsync()
    {
        await _lock.WaitAsync().ConfigureAwait(false);
        try
        {
            _bookmarks.Clear();
            await SaveAsync().ConfigureAwait(false);
        }
        finally
        {
            _lock.Release();
        }
    }

    /// <summary>
    /// Hashes a file path to a stable, compact key. Uses SHA-256 of the
    /// upper-cased, normalized path for case-insensitive Windows paths.
    /// </summary>
    private static string HashFilePath(string filePath)
    {
        string normalized = Path.GetFullPath(filePath).ToUpperInvariant();
        byte[] hashBytes = SHA256.HashData(Encoding.UTF8.GetBytes(normalized));
        return Convert.ToHexString(hashBytes);
    }

    private async Task SaveAsync()
    {
        Directory.CreateDirectory(SettingsDirectory);

        string tempPath = BookmarksFilePath + ".tmp";

        await using (var stream = new FileStream(
            tempPath,
            FileMode.Create,
            FileAccess.Write,
            FileShare.None,
            bufferSize: 4096,
            FileOptions.Asynchronous | FileOptions.WriteThrough))
        {
            await JsonSerializer.SerializeAsync(stream, _bookmarks, JsonOptions).ConfigureAwait(false);
        }

        File.Move(tempPath, BookmarksFilePath, overwrite: true);
    }
}
