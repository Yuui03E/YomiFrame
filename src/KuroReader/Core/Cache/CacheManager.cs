using System.Collections.Concurrent;
using KuroReader.Core.Archives;
using SkiaSharp;

namespace KuroReader.Core.Cache;

/// <summary>
/// Three-tier cache orchestrator for the manga reader pipeline.
/// <list type="bullet">
///   <item><b>L3 (Archive Buffer)</b>: Raw compressed archive bytes held in memory.
///         Archives ≤200 MB are loaded entirely; larger ones stream on demand.</item>
///   <item><b>L2 (Page Cache)</b>: LRU cache of decoded <see cref="SKBitmap"/> objects,
///         managed by <see cref="PageCache"/>.</item>
///   <item><b>L1 (Display Cache)</b>: Screen-ready bitmaps at current zoom/fit for the
///         current page and next page only.</item>
/// </list>
/// Background prefetch decodes ±5 pages around the current reading position.
/// Thread-safe with <see cref="SemaphoreSlim"/> and <see cref="ConcurrentDictionary{TKey,TValue}"/>.
/// </summary>
public sealed class CacheManager : IDisposable
{
    /// <summary>
    /// Maximum archive size (in bytes) that will be buffered entirely in memory (L3).
    /// Archives larger than this are streamed from disk on each page extraction.
    /// </summary>
    private const long MaxArchiveBufferSize = 200L * 1024 * 1024; // 200 MB

    /// <summary>
    /// Number of pages to prefetch in each direction around the current position.
    /// </summary>
    private const int PrefetchRadius = 5;

    private readonly PageCache _pageCache;
    private readonly ConcurrentDictionary<int, SKBitmap> _displayCache = new();
    private readonly SemaphoreSlim _archiveLock = new(1, 1);
    private readonly SemaphoreSlim _decodeLock = new(1, 1);

    private IArchiveReader? _reader;
    private IReadOnlyList<string>? _pageList;
    private byte[]? _archiveBuffer; // L3
    private CancellationTokenSource? _prefetchCts;
    private int _currentPage;
    private int _previousPage;
    private bool _disposed;

    /// <summary>
    /// Gets the underlying page cache (L2) for direct inspection.
    /// </summary>
    public PageCache L2Cache => _pageCache;

    /// <summary>
    /// Gets the total number of pages in the loaded archive.
    /// </summary>
    public int PageCount => _pageList?.Count ?? 0;

    /// <summary>
    /// Gets the current page index.
    /// </summary>
    public int CurrentPage => _currentPage;

    /// <summary>
    /// Gets whether an archive buffer (L3) is loaded.
    /// </summary>
    public bool HasArchiveBuffer => _archiveBuffer is not null;

    /// <summary>
    /// Creates a new cache manager with the specified memory budget for decoded bitmaps.
    /// </summary>
    /// <param name="cacheMemoryBudgetMB">Maximum MB for the L2 decoded page cache.</param>
    public CacheManager(int cacheMemoryBudgetMB = 500)
    {
        _pageCache = new PageCache(cacheMemoryBudgetMB);
    }

    /// <summary>
    /// Loads an archive into the cache system. Populates L3 buffer if the archive
    /// is small enough, enumerates pages, and begins prefetching around page 0.
    /// </summary>
    /// <param name="reader">The archive reader to use. Ownership is transferred to CacheManager.</param>
    /// <param name="startPage">Page to start on (e.g., from a bookmark).</param>
    /// <param name="progress">Optional progress reporter for loading toast.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    public async Task LoadArchiveAsync(
        IArchiveReader reader,
        int startPage = 0,
        IProgress<(int current, int total, string message)>? progress = null,
        CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        // Cancel any running prefetch from a previous archive.
        CancelPrefetch();

        // Dispose previous state.
        await _archiveLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            _reader?.Dispose();
            _pageCache.Clear();
            ClearDisplayCache();
            _archiveBuffer = null;

            _reader = reader;

            // Step 1: Enumerate pages.
            progress?.Report((0, 0, "Scanning archive..."));
            _pageList = await _reader.GetPageListAsync().ConfigureAwait(false);

            if (_pageList.Count == 0)
            {
                progress?.Report((0, 0, "No images found in archive."));
                return;
            }

            // Step 2: L3 buffer — read entire archive if small enough.
            if (_reader.ArchiveSize > 0 && _reader.ArchiveSize <= MaxArchiveBufferSize)
            {
                progress?.Report((0, _pageList.Count, "Buffering archive..."));
                _archiveBuffer = await _reader.ExtractAllBytesAsync().ConfigureAwait(false);
            }

            cancellationToken.ThrowIfCancellationRequested();

            // Step 3: Decode the start page immediately.
            _currentPage = Math.Clamp(startPage, 0, _pageList.Count - 1);
            _previousPage = _currentPage;

            progress?.Report((_currentPage + 1, _pageList.Count, "Decoding page..."));
            await DecodeAndCachePageAsync(_currentPage, cancellationToken).ConfigureAwait(false);

            progress?.Report((_currentPage + 1, _pageList.Count, "Ready."));
        }
        finally
        {
            _archiveLock.Release();
        }

        // Step 4: Begin prefetching neighbors.
        StartPrefetch(_currentPage);
    }

    /// <summary>
    /// Navigates to a specific page. Returns the decoded bitmap (from cache or freshly decoded).
    /// Triggers background prefetch of surrounding pages.
    /// </summary>
    /// <param name="pageIndex">Zero-based page index.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The decoded bitmap for the requested page, or null if unavailable.</returns>
    public async Task<SKBitmap?> GetPageAsync(int pageIndex, CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        if (_pageList is null || pageIndex < 0 || pageIndex >= _pageList.Count)
            return null;

        _previousPage = _currentPage;
        _currentPage = pageIndex;

        // Check L1 display cache first.
        if (_displayCache.TryGetValue(pageIndex, out var displayBitmap))
            return displayBitmap;

        // Check L2 page cache.
        if (_pageCache.TryGet(pageIndex, out var cachedBitmap) && cachedBitmap is not null)
        {
            return cachedBitmap;
        }

        // Cache miss — decode now.
        var bitmap = await DecodeAndCachePageAsync(pageIndex, cancellationToken).ConfigureAwait(false);

        // Restart prefetch around new position.
        CancelPrefetch();
        StartPrefetch(pageIndex);

        return bitmap;
    }

    /// <summary>
    /// Pre-renders a page at the given display size and stores it in the L1 display cache.
    /// Only the current and next page are kept in L1.
    /// </summary>
    /// <param name="pageIndex">Page to render.</param>
    /// <param name="targetWidth">Target display width in pixels.</param>
    /// <param name="targetHeight">Target display height in pixels.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The display-ready bitmap.</returns>
    public async Task<SKBitmap?> GetDisplayPageAsync(
        int pageIndex,
        int targetWidth,
        int targetHeight,
        CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        // Check L1 first.
        if (_displayCache.TryGetValue(pageIndex, out var existing))
            return existing;

        // Get the full-res bitmap from L2.
        var source = await GetPageAsync(pageIndex, cancellationToken).ConfigureAwait(false);
        if (source is null)
            return null;

        // Scale to target size.
        var scaled = await Task.Run(() =>
        {
            var info = new SKImageInfo(targetWidth, targetHeight, SKColorType.Bgra8888, SKAlphaType.Premul);
            var result = new SKBitmap(info);
            if (!source.ScalePixels(result, SKFilterQuality.High))
            {
                result.Dispose();
                return null;
            }
            return result;
        }, cancellationToken).ConfigureAwait(false);

        if (scaled is null)
            return null;

        // Store in L1, evicting pages that aren't current or next.
        _displayCache[pageIndex] = scaled;
        PruneDisplayCache(pageIndex);

        return scaled;
    }

    /// <summary>
    /// Evicts decoded page cache entries far from the current position.
    /// Call when the system is under memory pressure.
    /// </summary>
    /// <param name="keepRadius">Pages within ±keepRadius are kept.</param>
    public void HandleMemoryPressure(int keepRadius = 5)
    {
        _pageCache.EvictDistant(_currentPage, keepRadius);
        ClearDisplayCache();
    }

    /// <summary>
    /// Decodes raw image bytes into an <see cref="SKBitmap"/> and stores in L2 cache.
    /// </summary>
    private async Task<SKBitmap?> DecodeAndCachePageAsync(int pageIndex, CancellationToken ct)
    {
        if (_pageList is null || _reader is null)
            return null;

        // Already cached?
        if (_pageCache.TryGet(pageIndex, out var cached) && cached is not null)
            return cached;

        await _decodeLock.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            // Double-check after acquiring lock.
            if (_pageCache.TryGet(pageIndex, out cached) && cached is not null)
                return cached;

            string entryName = _pageList[pageIndex];
            byte[] rawBytes = await _reader.ExtractPageAsync(entryName).ConfigureAwait(false);

            ct.ThrowIfCancellationRequested();

            // Decode on thread pool to avoid blocking.
            var bitmap = await Task.Run(() =>
            {
                using var data = SKData.CreateCopy(rawBytes);
                using var codec = SKCodec.Create(data);
                if (codec is null)
                    return null;

                var info = new SKImageInfo(
                    codec.Info.Width,
                    codec.Info.Height,
                    SKColorType.Bgra8888,
                    SKAlphaType.Premul);

                var bmp = new SKBitmap(info);
                var result = codec.GetPixels(info, bmp.GetPixels());
                if (result != SKCodecResult.Success && result != SKCodecResult.IncompleteInput)
                {
                    bmp.Dispose();
                    return null;
                }

                return bmp;
            }, ct).ConfigureAwait(false);

            if (bitmap is not null)
            {
                _pageCache.Add(pageIndex, bitmap, _currentPage, _previousPage);
            }

            return bitmap;
        }
        finally
        {
            _decodeLock.Release();
        }
    }

    /// <summary>
    /// Starts background prefetch of ±<see cref="PrefetchRadius"/> pages around the given position.
    /// </summary>
    private void StartPrefetch(
        int centerPage,
        IProgress<(int current, int total, string message)>? progress = null)
    {
        var cts = new CancellationTokenSource();
        _prefetchCts = cts;

        _ = Task.Run(async () =>
        {
            try
            {
                int total = _pageList?.Count ?? 0;

                // Prefetch forward first (more likely to be needed), then backward.
                for (int offset = 1; offset <= PrefetchRadius; offset++)
                {
                    int[] indices = [centerPage + offset, centerPage - offset];

                    foreach (int idx in indices)
                    {
                        if (cts.Token.IsCancellationRequested)
                            return;

                        if (idx < 0 || idx >= total)
                            continue;

                        if (_pageCache.Contains(idx))
                            continue;

                        await DecodeAndCachePageAsync(idx, cts.Token).ConfigureAwait(false);

                        progress?.Report((idx + 1, total, $"Prefetching page {idx + 1}..."));
                    }
                }
            }
            catch (OperationCanceledException)
            {
                // Normal cancellation when user navigates away.
            }
            catch (Exception)
            {
                // Prefetch failures are non-fatal — page will be decoded on demand.
            }
        }, cts.Token);
    }

    /// <summary>
    /// Cancels any running prefetch task.
    /// </summary>
    private void CancelPrefetch()
    {
        var cts = _prefetchCts;
        if (cts is not null)
        {
            cts.Cancel();
            cts.Dispose();
            _prefetchCts = null;
        }
    }

    /// <summary>
    /// Keeps only current and current+1 in the L1 display cache.
    /// </summary>
    private void PruneDisplayCache(int currentPage)
    {
        var toRemove = _displayCache.Keys
            .Where(k => k != currentPage && k != currentPage + 1)
            .ToList();

        foreach (int key in toRemove)
        {
            if (_displayCache.TryRemove(key, out var bmp))
                bmp.Dispose();
        }
    }

    /// <summary>
    /// Clears the entire L1 display cache.
    /// </summary>
    private void ClearDisplayCache()
    {
        foreach (var kvp in _displayCache)
        {
            if (_displayCache.TryRemove(kvp.Key, out var bmp))
                bmp.Dispose();
        }
    }

    /// <inheritdoc />
    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        CancelPrefetch();
        ClearDisplayCache();
        _pageCache.Dispose();
        _reader?.Dispose();
        _archiveBuffer = null;
        _archiveLock.Dispose();
        _decodeLock.Dispose();
    }
}
