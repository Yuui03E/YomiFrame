using KuroReader.Helpers;

namespace KuroReader.Core.Archives;

/// <summary>
/// Reads image pages from a local folder (and its subfolders) as if it were an archive.
/// Recursively discovers image files, flattens the hierarchy, and sorts naturally.
/// </summary>
public sealed class FolderReader : IArchiveReader
{
    private static readonly HashSet<string> SupportedExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".jpg", ".jpeg", ".png", ".webp", ".gif", ".bmp", ".tiff", ".tif"
    };

    private readonly string _folderPath;
    private IReadOnlyList<string>? _pageList;
    private long _archiveSize;
    private bool _disposed;

    /// <inheritdoc />
    public string FilePath => _folderPath;

    /// <inheritdoc />
    public long ArchiveSize => _archiveSize;

    /// <inheritdoc />
    public int PageCount => _pageList?.Count ?? 0;

    /// <summary>
    /// Creates a new <see cref="FolderReader"/> for the specified directory.
    /// </summary>
    /// <param name="folderPath">Absolute path to the folder containing image files.</param>
    /// <exception cref="DirectoryNotFoundException">Thrown if <paramref name="folderPath"/> does not exist.</exception>
    public FolderReader(string folderPath)
    {
        if (!Directory.Exists(folderPath))
            throw new DirectoryNotFoundException($"Folder not found: {folderPath}");

        _folderPath = folderPath;
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
            long totalSize = 0;

            var files = Directory.EnumerateFiles(_folderPath, "*", SearchOption.AllDirectories)
                .Where(f => SupportedExtensions.Contains(Path.GetExtension(f)))
                .OrderBy(f => f, comparer)
                .ToList();

            foreach (var file in files)
            {
                try
                {
                    totalSize += new FileInfo(file).Length;
                }
                catch (IOException)
                {
                    // File may have been deleted between enumeration and stat; skip.
                }
            }

            _archiveSize = totalSize;
            _pageList = files.AsReadOnly();
            return _pageList;
        });
    }

    /// <inheritdoc />
    public async Task<byte[]> ExtractPageAsync(string entryName)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        if (!File.Exists(entryName))
            throw new FileNotFoundException($"Image file not found: {entryName}", entryName);

        return await File.ReadAllBytesAsync(entryName).ConfigureAwait(false);
    }

    /// <summary>
    /// Not meaningful for folders — returns an empty array.
    /// Folder readers do not have a single archive blob to buffer.
    /// </summary>
    public Task<byte[]> ExtractAllBytesAsync()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        return Task.FromResult(Array.Empty<byte>());
    }

    /// <inheritdoc />
    public void Dispose()
    {
        _disposed = true;
    }
}
