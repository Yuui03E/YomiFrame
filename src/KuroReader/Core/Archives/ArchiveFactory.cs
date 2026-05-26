namespace KuroReader.Core.Archives;

/// <summary>
/// Factory for creating the appropriate <see cref="IArchiveReader"/> based on file extension.
/// Supports ZIP, CBZ, RAR, CBR, 7Z, CB7 archives and individual image files (via folder reader).
/// </summary>
public static class ArchiveFactory
{
    private static readonly HashSet<string> ArchiveExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".zip", ".cbz", ".rar", ".cbr", ".7z", ".cb7"
    };

    private static readonly HashSet<string> ImageExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".jpg", ".jpeg", ".png", ".webp", ".gif", ".bmp", ".tiff", ".tif"
    };

    /// <summary>
    /// Opens an archive or folder and returns the appropriate reader.
    /// </summary>
    /// <param name="filePath">
    /// Path to an archive file (.zip, .cbz, .rar, .cbr, .7z, .cb7),
    /// a directory, or an individual image file (which uses a folder reader on the parent directory).
    /// </param>
    /// <returns>An <see cref="IArchiveReader"/> ready for use.</returns>
    /// <exception cref="ArgumentException">Thrown when the path is null or empty.</exception>
    /// <exception cref="NotSupportedException">Thrown when the file type is not supported.</exception>
    /// <exception cref="FileNotFoundException">Thrown when the file does not exist.</exception>
    public static IArchiveReader Open(string filePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(filePath);

        // If it's a directory, use FolderReader directly.
        if (Directory.Exists(filePath))
            return new FolderReader(filePath);

        if (!File.Exists(filePath))
            throw new FileNotFoundException($"File not found: {filePath}", filePath);

        string ext = Path.GetExtension(filePath);

        // Archive files → use the corresponding reader.
        if (IsZipFamily(ext))
            return new ZipArchiveReader(filePath);

        if (IsRarFamily(ext))
            return new RarArchiveReader(filePath);

        if (Is7ZipFamily(ext))
            return new SevenZipArchiveReader(filePath);

        // Individual image file → read from the parent directory.
        if (ImageExtensions.Contains(ext))
        {
            string? directory = Path.GetDirectoryName(filePath);
            if (directory is null)
                throw new NotSupportedException($"Cannot determine parent directory for: {filePath}");

            return new FolderReader(directory);
        }

        throw new NotSupportedException(
            $"Unsupported file type '{ext}'. Supported: {string.Join(", ", ArchiveExtensions)} and image files.");
    }

    /// <summary>
    /// Opens multiple archives or folders and combines them into a single MultiArchiveReader.
    /// </summary>
    public static IArchiveReader OpenMultiple(IEnumerable<string> filePaths)
    {
        var paths = filePaths.ToList();
        if (paths.Count == 0)
            throw new ArgumentException("No file paths provided.", nameof(filePaths));

        if (paths.Count == 1)
            return Open(paths[0]);

        // Natural sort the files so volumes are in order
        paths.Sort(KuroReader.Helpers.NaturalSortComparer.Instance);

        var readers = new List<IArchiveReader>();
        foreach (var path in paths)
        {
            try
            {
                readers.Add(Open(path));
            }
            catch
            {
                // Ignore unsupported/missing files when loading multiple
            }
        }

        if (readers.Count == 0)
            throw new NotSupportedException("None of the provided files are supported archives.");

        if (readers.Count == 1)
            return readers[0];

        return new MultiArchiveReader(readers, paths[0]);
    }

    /// <summary>
    /// Returns whether the given file path is a supported archive or image type.
    /// </summary>
    public static bool IsSupported(string filePath)
    {
        string ext = Path.GetExtension(filePath);
        return ArchiveExtensions.Contains(ext) || ImageExtensions.Contains(ext);
    }

    /// <summary>
    /// Returns whether the given file path is a supported archive (not an image).
    /// </summary>
    public static bool IsArchive(string filePath)
    {
        string ext = Path.GetExtension(filePath);
        return ArchiveExtensions.Contains(ext);
    }

    /// <summary>
    /// Returns whether the given file path is a supported image file.
    /// </summary>
    public static bool IsImage(string filePath)
    {
        string ext = Path.GetExtension(filePath);
        return ImageExtensions.Contains(ext);
    }

    private static bool IsZipFamily(string ext) =>
        ext.Equals(".zip", StringComparison.OrdinalIgnoreCase) ||
        ext.Equals(".cbz", StringComparison.OrdinalIgnoreCase);

    private static bool IsRarFamily(string ext) =>
        ext.Equals(".rar", StringComparison.OrdinalIgnoreCase) ||
        ext.Equals(".cbr", StringComparison.OrdinalIgnoreCase);

    private static bool Is7ZipFamily(string ext) =>
        ext.Equals(".7z", StringComparison.OrdinalIgnoreCase) ||
        ext.Equals(".cb7", StringComparison.OrdinalIgnoreCase);
}
