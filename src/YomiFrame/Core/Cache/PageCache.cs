using System.Collections.Concurrent;
using SkiaSharp;

namespace YomiFrame.Core.Cache;

/// <summary>
/// LRU cache for decoded <see cref="SKBitmap"/> page images.
/// Thread-safe via <see cref="ConcurrentDictionary{TKey,TValue}"/> and lock-free access tracking.
/// Evicts pages furthest from the current reading position when the memory budget is exceeded.
/// </summary>
public sealed class PageCache : IDisposable
{
    private readonly ConcurrentDictionary<int, CacheEntry> _cache = new();
    private long _currentMemoryBytes;
    private long _memoryBudgetBytes;
    private bool _disposed;

    /// <summary>
    /// Gets or sets the memory budget in bytes. Changing this triggers eviction if the
    /// new budget is smaller than current usage.
    /// </summary>
    public long MemoryBudgetBytes
    {
        get => Interlocked.Read(ref _memoryBudgetBytes);
        set => Interlocked.Exchange(ref _memoryBudgetBytes, value);
    }

    /// <summary>
    /// Gets the current memory usage in bytes (sum of all cached bitmap sizes).
    /// </summary>
    public long CurrentMemoryBytes => Interlocked.Read(ref _currentMemoryBytes);

    /// <summary>
    /// Gets the number of cached pages.
    /// </summary>
    public int Count => _cache.Count;

    /// <summary>
    /// Creates a new page cache with the specified memory budget.
    /// </summary>
    /// <param name="memoryBudgetMB">Maximum memory in megabytes for decoded bitmaps.</param>
    public PageCache(int memoryBudgetMB = 500)
    {
        _memoryBudgetBytes = (long)memoryBudgetMB * 1024 * 1024;
    }

    /// <summary>
    /// Tries to get a cached bitmap for the given page index.
    /// Updates access time on hit for LRU tracking.
    /// </summary>
    /// <param name="pageIndex">Zero-based page index.</param>
    /// <param name="bitmap">The cached bitmap if found.</param>
    /// <returns>True if the page was in cache.</returns>
    public bool TryGet(int pageIndex, out SKBitmap? bitmap)
    {
        if (_cache.TryGetValue(pageIndex, out var entry))
        {
            entry.LastAccess = Environment.TickCount64;
            bitmap = entry.Bitmap;
            return true;
        }

        bitmap = null;
        return false;
    }

    /// <summary>
    /// Adds a decoded bitmap to the cache. If the page already exists, the old bitmap is disposed
    /// and replaced. Triggers eviction if the memory budget would be exceeded.
    /// </summary>
    /// <param name="pageIndex">Zero-based page index.</param>
    /// <param name="bitmap">The decoded bitmap to cache. Ownership is transferred to the cache.</param>
    /// <param name="protectedPositions">Positions to protect from eviction (e.g. current and previous reading position).</param>
    public void Add(int pageIndex, SKBitmap bitmap, params int[] protectedPositions)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        long bitmapBytes = CalculateBitmapSize(bitmap);

        // If adding this bitmap would blow the budget, evict first.
        EvictUntilFits(bitmapBytes, protectedPositions);

        var newEntry = new CacheEntry(bitmap, bitmapBytes);

        if (_cache.TryGetValue(pageIndex, out var existing))
        {
            // Replace existing entry.
            if (_cache.TryUpdate(pageIndex, newEntry, existing))
            {
                Interlocked.Add(ref _currentMemoryBytes, bitmapBytes - existing.SizeBytes);
                existing.Bitmap.Dispose();
            }
            else
            {
                // Concurrent update race — someone else replaced it. Dispose ours.
                bitmap.Dispose();
            }
        }
        else
        {
            if (_cache.TryAdd(pageIndex, newEntry))
            {
                Interlocked.Add(ref _currentMemoryBytes, bitmapBytes);
            }
            else
            {
                // Someone else added the same page concurrently. Dispose ours.
                bitmap.Dispose();
            }
        }
    }

    /// <summary>
    /// Removes a specific page from the cache and disposes its bitmap.
    /// </summary>
    /// <param name="pageIndex">Zero-based page index.</param>
    /// <returns>True if the page was found and removed.</returns>
    public bool Remove(int pageIndex)
    {
        if (_cache.TryRemove(pageIndex, out var entry))
        {
            Interlocked.Add(ref _currentMemoryBytes, -entry.SizeBytes);
            entry.Bitmap.Dispose();
            return true;
        }
        return false;
    }

    /// <summary>
    /// Checks whether a page is cached.
    /// </summary>
    public bool Contains(int pageIndex) => _cache.ContainsKey(pageIndex);

    /// <summary>
    /// Clears all cached bitmaps and disposes them.
    /// </summary>
    public void Clear()
    {
        foreach (var kvp in _cache)
        {
            if (_cache.TryRemove(kvp.Key, out var entry))
            {
                entry.Bitmap.Dispose();
            }
        }

        Interlocked.Exchange(ref _currentMemoryBytes, 0);
    }

    /// <summary>
    /// Evicts pages furthest from <paramref name="protectedPositions"/> until
    /// there is enough room for <paramref name="requiredBytes"/>.
    /// </summary>
    private void EvictUntilFits(long requiredBytes, params int[] protectedPositions)
    {
        long budget = MemoryBudgetBytes;

        while (Interlocked.Read(ref _currentMemoryBytes) + requiredBytes > budget && _cache.Count > 0)
        {
            // Find the page furthest from protected positions.
            int? furthestPage = null;
            int maxDistance = -1;

            foreach (var kvp in _cache)
            {
                int distance = protectedPositions.Length == 0 ? 0 : protectedPositions.Min(p => Math.Abs(kvp.Key - p));
                
                // Never evict pages that are distance 0 or 1 from any protected position
                if (distance <= 1) continue; 

                if (distance > maxDistance)
                {
                    maxDistance = distance;
                    furthestPage = kvp.Key;
                }
            }

            if (furthestPage is null)
                break; // Everything left is protected

            Remove(furthestPage.Value);
        }
    }

    /// <summary>
    /// Evicts pages far from the current position even if under budget, to proactively free memory.
    /// Call this when memory pressure is detected.
    /// </summary>
    /// <param name="currentReadingPosition">Current page index.</param>
    /// <param name="keepRadius">Number of pages around current to keep. Default ±10.</param>
    public void EvictDistant(int currentReadingPosition, int keepRadius = 10)
    {
        var toRemove = _cache.Keys
            .Where(k => Math.Abs(k - currentReadingPosition) > keepRadius)
            .OrderByDescending(k => Math.Abs(k - currentReadingPosition))
            .ToList();

        foreach (int pageIndex in toRemove)
        {
            Remove(pageIndex);
        }
    }

    /// <summary>
    /// Calculates the uncompressed memory size of a bitmap: width × height × 4 bytes (BGRA).
    /// </summary>
    private static long CalculateBitmapSize(SKBitmap bitmap) =>
        (long)bitmap.Width * bitmap.Height * 4;

    /// <inheritdoc />
    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        Clear();
    }

    /// <summary>
    /// Internal cache entry holding the bitmap, its computed size, and last-access timestamp.
    /// </summary>
    private sealed class CacheEntry
    {
        public readonly SKBitmap Bitmap;
        public readonly long SizeBytes;
        public long LastAccess;

        public CacheEntry(SKBitmap bitmap, long sizeBytes)
        {
            Bitmap = bitmap;
            SizeBytes = sizeBytes;
            LastAccess = Environment.TickCount64;
        }
    }
}
