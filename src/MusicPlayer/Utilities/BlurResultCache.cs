using System;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Windows.Media.Imaging;

namespace MusicPlayer.Utilities;

/// <summary>
/// LRU, memory-budgeted cache for blurred wallpaper results.
/// Separate from WallpaperDecodeCache (original decodes).
/// </summary>
public static class BlurResultCache
{
    private sealed class Entry
    {
        public BitmapSource Image = null!;
        public long Ticks;
        public long CostBytes;
    }

    private static readonly ConcurrentDictionary<string, Entry> _cache =
        new(StringComparer.OrdinalIgnoreCase);
    private static readonly object _gate = new();
    private static readonly ConcurrentDictionary<string, object> _locks =
        new(StringComparer.OrdinalIgnoreCase);

    public const long BudgetBytes = 128L * 1024 * 1024;

    private static long _usedBytes;

    public static bool TryGet(string key, out BitmapSource? image)
    {
        image = null;
        if (string.IsNullOrEmpty(key)) return false;
        lock (_gate)
        {
            if (_cache.TryGetValue(key, out var entry) && entry.Image != null)
            {
                entry.Ticks = Stopwatch.GetTimestamp();
                image = entry.Image;
                return true;
            }
        }
        return false;
    }

    public static void Store(string key, BitmapSource image)
    {
        if (string.IsNullOrEmpty(key) || image == null) return;
        long cost = (long)image.PixelWidth * image.PixelHeight * 4;
        if (cost <= 0 || cost > BudgetBytes / 2) return;

        lock (_gate)
        {
            if (_cache.TryGetValue(key, out var existing))
            {
                _usedBytes -= existing.CostBytes;
                _cache.TryRemove(key, out _);
            }

            while (_usedBytes + cost > BudgetBytes && _cache.Count > 0)
            {
                string? oldestKey = null; long oldestTicks = long.MaxValue;
                foreach (var kv in _cache)
                    if (kv.Value.Ticks < oldestTicks) { oldestTicks = kv.Value.Ticks; oldestKey = kv.Key; }
                if (oldestKey == null) break;
                if (_cache.TryRemove(oldestKey, out var removed))
                    _usedBytes -= removed.CostBytes;
            }

            _cache[key] = new Entry { Image = image, Ticks = Stopwatch.GetTimestamp(), CostBytes = cost };
            _usedBytes += cost;
        }
    }

    /// <summary>Per-key lock object for single-flight blur computation.</summary>
    public static object GetLock(string key) => _locks.GetOrAdd(key, _ => new object());

    public static void ReleaseLock(string key) => _locks.TryRemove(key, out _);

    public static (int Entries, long UsedBytes) Stats
    {
        get { lock (_gate) return (_cache.Count, _usedBytes); }
    }
}
