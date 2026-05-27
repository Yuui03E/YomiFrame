namespace YomiFrame.Core.Archives;

/// <summary>
/// Defines a unified interface for reading image pages from various archive formats
/// (ZIP/CBZ, RAR/CBR, 7Z/CB7) or loose folder structures.
/// </summary>
public interface IArchiveReader : IDisposable
{
    /// <summary>
    /// Gets the full file path to the archive or folder.
    /// </summary>
    string FilePath { get; }

    /// <summary>
    /// Gets the total size of the archive file in bytes.
    /// For folder readers, returns the sum of all image file sizes.
    /// </summary>
    long ArchiveSize { get; }

    /// <summary>
    /// Gets the number of image pages discovered in the archive.
    /// Available after <see cref="GetPageListAsync"/> has been called.
    /// </summary>
    int PageCount { get; }

    /// <summary>
    /// Enumerates and returns a naturally-sorted list of image entry names within the archive.
    /// Only includes files with supported image extensions.
    /// Nested folders are flattened — only the entry paths are returned.
    /// </summary>
    /// <returns>A sorted, read-only list of image entry names (archive-relative paths).</returns>
    Task<IReadOnlyList<string>> GetPageListAsync();

    /// <summary>
    /// Extracts the raw bytes of a single page by its entry name.
    /// </summary>
    /// <param name="entryName">The archive-relative entry name as returned by <see cref="GetPageListAsync"/>.</param>
    /// <returns>The raw image bytes.</returns>
    Task<byte[]> ExtractPageAsync(string entryName);

    /// <summary>
    /// Reads the entire archive file into a contiguous memory buffer.
    /// Used by the L3 archive buffer cache to avoid repeated disk I/O.
    /// </summary>
    /// <returns>The raw bytes of the entire archive file.</returns>
    Task<byte[]> ExtractAllBytesAsync();
}
