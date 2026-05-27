using YomiFrame.Helpers;
using SharpCompress.Archives;
using SharpCompress.Archives.Rar;

namespace YomiFrame.Core.Archives;

/// <summary>
/// Reads image pages from RAR (.rar) and CBR (.cbr) archives using SharpCompress.
/// Handles solid RAR archives by extracting sequentially, filters for supported image
/// extensions, and naturally sorts entries.
/// </summary>
public sealed class RarArchiveReader : IArchiveReader
{
    private static readonly HashSet<string> SupportedExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".jpg", ".jpeg", ".png", ".webp", ".gif", ".bmp", ".tiff", ".tif"
    };

    private readonly string _filePath;
    private readonly long _archiveSize;
    private RarArchive? _archive;
    private IReadOnlyList<string>? _pageList;
    private Dictionary<string, RarArchiveEntry>? _entryMap;
    private bool _isSolid;
    private bool _disposed;

    /// <inheritdoc />
    public string FilePath => _filePath;

    /// <inheritdoc />
    public long ArchiveSize => _archiveSize;

    /// <inheritdoc />
    public int PageCount => _pageList?.Count ?? 0;

    /// <summary>
    /// Opens a RAR/CBR archive for reading.
    /// </summary>
    /// <param name="filePath">Absolute path to the RAR or CBR file.</param>
    /// <exception cref="FileNotFoundException">Thrown if the file does not exist.</exception>
    public RarArchiveReader(string filePath)
    {
        if (!File.Exists(filePath))
            throw new FileNotFoundException($"Archive not found: {filePath}", filePath);

        _filePath = filePath;
        _archiveSize = new FileInfo(filePath).Length;

        _archive = RarArchive.Open(filePath);
        _isSolid = _archive.IsSolid;
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
            _entryMap = new Dictionary<string, RarArchiveEntry>(StringComparer.Ordinal);

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

        if (_isSolid)
        {
            // Solid archives require sequential extraction.
            // Extract all entries and return the requested one.
            return await ExtractFromSolidAsync(entryName).ConfigureAwait(false);
        }

        return await Task.Run(() =>
        {
            using var stream = entry.OpenEntryStream();
            var buffer = new byte[entry.Size];
            int totalRead = 0;
            while (totalRead < buffer.Length)
            {
                int bytesRead = stream.Read(buffer, totalRead, buffer.Length - totalRead);
                if (bytesRead == 0) break;
                totalRead += bytesRead;
            }

            if (totalRead < buffer.Length)
                return buffer.AsSpan(0, totalRead).ToArray();

            return buffer;
        }).ConfigureAwait(false);
    }

    /// <summary>
    /// Extracts a page from a solid RAR archive. Solid archives store entries
    /// as a continuous stream, so we must read through entries sequentially.
    /// </summary>
    private Task<byte[]> ExtractFromSolidAsync(string entryName)
    {
        return Task.Run(() =>
        {
            // For solid archives, re-open and iterate using the reader pattern
            // which processes entries in stored order.
            using var archive = RarArchive.Open(_filePath);

            foreach (var entry in archive.Entries)
            {
                if (entry.IsDirectory || entry.Key is null)
                    continue;

                if (!string.Equals(entry.Key, entryName, StringComparison.Ordinal))
                    continue;

                using var stream = entry.OpenEntryStream();
                using var ms = new MemoryStream((int)entry.Size);
                stream.CopyTo(ms);
                return ms.ToArray();
            }

            throw new FileNotFoundException($"Entry not found in solid archive: {entryName}");
        });
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
