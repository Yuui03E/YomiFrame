using System.IO;
using System.IO.Compression;
using YomiFrame.Helpers;

namespace YomiFrame.Core.Archives;

/// <summary>
/// Reads image pages from ZIP (.zip) and CBZ (.cbz) archives using <see cref="System.IO.Compression.ZipArchive"/>.
/// Filters for supported image extensions, flattens nested folder structures, and naturally sorts entries.
/// </summary>
public sealed class ZipArchiveReader : IArchiveReader
{
    private static readonly HashSet<string> SupportedExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".jpg", ".jpeg", ".png", ".webp", ".gif", ".bmp", ".tiff", ".tif"
    };

    private readonly string _filePath;
    private readonly long _archiveSize;
    private IReadOnlyList<string>? _pageList;
    private ZipArchive? _archive;
    private FileStream? _fileStream;
    private bool _disposed;

    /// <inheritdoc />
    public string FilePath => _filePath;

    /// <inheritdoc />
    public long ArchiveSize => _archiveSize;

    /// <inheritdoc />
    public int PageCount => _pageList?.Count ?? 0;

    /// <summary>
    /// Opens a ZIP/CBZ archive for reading.
    /// </summary>
    /// <param name="filePath">Absolute path to the ZIP or CBZ file.</param>
    /// <exception cref="FileNotFoundException">Thrown if the file does not exist.</exception>
    public ZipArchiveReader(string filePath)
    {
        if (!File.Exists(filePath))
            throw new FileNotFoundException($"Archive not found: {filePath}", filePath);

        _filePath = filePath;
        _archiveSize = new FileInfo(filePath).Length;

        _fileStream = new FileStream(
            filePath,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read,
            bufferSize: 81920,
            FileOptions.Asynchronous | FileOptions.SequentialScan);

        _archive = new ZipArchive(_fileStream, ZipArchiveMode.Read, leaveOpen: false);
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

            var entries = _archive!.Entries
                .Where(e => !string.IsNullOrEmpty(e.Name) && // skip directory entries
                            e.Length > 0 &&
                            SupportedExtensions.Contains(Path.GetExtension(e.Name)))
                .Select(e => e.FullName)
                .OrderBy(name => name, comparer)
                .ToList();

            _pageList = entries.AsReadOnly();
            return _pageList;
        });
    }

    /// <inheritdoc />
    public async Task<byte[]> ExtractPageAsync(string entryName)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        var entry = _archive!.GetEntry(entryName)
            ?? throw new FileNotFoundException($"Entry not found in archive: {entryName}");

        // Pre-allocate exact buffer to avoid array resizing.
        var buffer = new byte[entry.Length];
        using var stream = entry.Open();

        int totalRead = 0;
        while (totalRead < buffer.Length)
        {
            int bytesRead = await stream.ReadAsync(
                buffer.AsMemory(totalRead, buffer.Length - totalRead))
                .ConfigureAwait(false);

            if (bytesRead == 0)
                break; // Unexpected EOF — return what we have.

            totalRead += bytesRead;
        }

        // If we got fewer bytes than expected (corrupt entry), trim.
        if (totalRead < buffer.Length)
            return buffer.AsSpan(0, totalRead).ToArray();

        return buffer;
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

        // FileStream is disposed by ZipArchive when leaveOpen=false,
        // but we null it out for safety.
        _fileStream = null;
    }
}
