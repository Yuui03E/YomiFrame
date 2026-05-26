using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;

namespace KuroReader.Core.Archives;

/// <summary>
/// Wraps multiple IArchiveReader instances to present them as a single continuous sequence of pages.
/// </summary>
public class MultiArchiveReader : IArchiveReader
{
    private readonly IReadOnlyList<IArchiveReader> _readers;
    private readonly string _primaryFilePath;
    private int _pageCount;
    private bool _initialized;

    // Maps global entry name to (ArchiveIndex, LocalEntryName)
    private readonly Dictionary<string, (int ReaderIndex, string LocalEntryName)> _entryMap = new();
    private readonly List<string> _globalPageList = new();

    public MultiArchiveReader(IEnumerable<IArchiveReader> readers, string primaryFilePath)
    {
        _readers = readers.ToList();
        if (_readers.Count == 0)
            throw new System.ArgumentException("At least one reader must be provided.", nameof(readers));
            
        _primaryFilePath = primaryFilePath;
    }

    public string FilePath => _primaryFilePath;

    public long ArchiveSize => _readers.Sum(r => r.ArchiveSize);

    public int PageCount => _pageCount;

    public async Task<IReadOnlyList<string>> GetPageListAsync()
    {
        if (_initialized) return _globalPageList;

        for (int i = 0; i < _readers.Count; i++)
        {
            var reader = _readers[i];
            var localPages = await reader.GetPageListAsync().ConfigureAwait(false);
            
            foreach (var localEntry in localPages)
            {
                // Create a unique global entry name to prevent collisions across archives
                string globalEntry = $"{i}|{localEntry}";
                _globalPageList.Add(globalEntry);
                _entryMap[globalEntry] = (i, localEntry);
            }
        }

        _pageCount = _globalPageList.Count;
        _initialized = true;
        
        return _globalPageList;
    }

    public async Task<byte[]> ExtractPageAsync(string entryName)
    {
        if (!_entryMap.TryGetValue(entryName, out var mapping))
        {
            throw new FileNotFoundException($"Entry {entryName} not found in MultiArchiveReader.");
        }

        var reader = _readers[mapping.ReaderIndex];
        return await reader.ExtractPageAsync(mapping.LocalEntryName).ConfigureAwait(false);
    }

    public async Task<byte[]> ExtractAllBytesAsync()
    {
        // For multi-archive, returning the combined raw bytes of all archives is impractical and not useful 
        // since memory streams expect a single valid zip/rar file structure.
        // We will just return the bytes of the first archive, or throw.
        // CacheManager checks ArchiveSize to decide whether to load to RAM. We can return an empty array 
        // to force stream-based extraction for multi-archives.
        return await Task.FromResult(System.Array.Empty<byte>());
    }

    public void Dispose()
    {
        foreach (var reader in _readers)
        {
            try
            {
                reader.Dispose();
            }
            catch
            {
                // Ignore dispose errors on individual readers
            }
        }
        System.GC.SuppressFinalize(this);
    }
}
