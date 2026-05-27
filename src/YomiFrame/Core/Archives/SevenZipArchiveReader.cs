using YomiFrame.Helpers;
using SharpCompress.Archives;
using SharpCompress.Archives.SevenZip;

namespace YomiFrame.Core.Archives;

/// <summary>
/// Reads image pages from 7-Zip (.7z) and CB7 (.cb7) archives using SharpCompress.
/// Filters for supported image extensions, flattens nested folders, and naturally sorts entries.
/// </summary>
public sealed class SevenZipArchiveReader : IArchiveReader
{
    private static readonly HashSet<string> SupportedExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".jpg", ".jpeg", ".png", ".webp", ".gif", ".bmp", ".tiff", ".tif"
    };

    private readonly string _filePath;
    private readonly long _archiveSize;
    private SevenZipArchive? _archive;
    private IReadOnlyList<string>? _pageList;
    private Dictionary<string, SevenZipArchiveEntry>? _entryMap;
    private bool _disposed;

    /// <inheritdoc />
    public string FilePath => _filePath;

    /// <inheritdoc />
    public long ArchiveSize => _archiveSize;

    /// <inheritdoc />
    public int PageCount => _pageList?.Count ?? 0;

    /// <summary>
    /// Opens a 7Z/CB7 archive for reading.
    /// </summary>
    /// <param name="filePath">Absolute path to the 7Z or CB7 file.</param>
    /// <exception cref="FileNotFoundException">Thrown if the file does not exist.</exception>
    public SevenZipArchiveReader(string filePath)
    {
        if (!File.Exists(filePath))
            throw new FileNotFoundException($"Archive not found: {filePath}", filePath);

        _filePath = filePath;
        _archiveSize = new FileInfo(filePath).Length;

        _archive = SevenZipArchive.Open(filePath);
    }

    /// <inheritdoc />
    public Task<IReadOnlyList<string>> GetPageListAsync()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        if (_pageList is not null)
            return Task.FromResult(_pageList);

        return Task.Run(() =>
        {
            var comparer = NaturalSortComparer.Instance;
            _entryMap = new Dictionary<string, SevenZipArchiveEntry>(StringComparer.Ordinal);

            var imageEntries = _archive!.Entries
                .Where(e => !e.IsDirectory &&
                            e.Size > 0 &&
                            e.Key is not null &&
                            SupportedExtensions.Contains(Path.GetExtension(e.Key)))
                .ToList();

            foreach (var entry in imageEntries)
            {
                _entryMap[entry.Key!] = entry;
            }

            var sorted = _entryMap.Keys
                .OrderBy(name => name, comparer)
                .ToList();

            _pageList = sorted.AsReadOnly();
            return _pageList;
        });
    }

    /// <inheritdoc />
    public async Task<byte[]> ExtractPageAsync(string entryName)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        if (_entryMap is null || !_entryMap.TryGetValue(entryName, out var entry))
            throw new FileNotFoundException($"Entry not found in archive: {entryName}");

        // 7z in SharpCompress requires sequential extraction via reader pattern
        // for best compatibility. For random access, we use the entry stream directly.
        return await Task.Run(() =>
        {
            using var stream = entry.OpenEntryStream();
            using var ms = new MemoryStream((int)entry.Size);
            stream.CopyTo(ms);
            return ms.ToArray();
        }).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task<byte[]> ExtractAllBytesAsync()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        return await File.ReadAllBytesAsync(_filePath).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        _archive?.Dispose();
        _archive = null;
        _entryMap = null;
    }
}
