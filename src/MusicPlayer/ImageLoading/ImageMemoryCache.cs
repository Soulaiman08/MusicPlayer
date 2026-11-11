using System;
using System.Collections.Generic;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace MusicPlayer.ImageLoading;

public sealed class ImageMemoryCache
{
    private sealed class CacheEntry
    {
        public required string CacheKey { get; init; }
        public required ImageKind Kind { get; init; }
        public required ImageQuality Quality { get; init; }
        public required ImageSource Image { get; init; }
        public required long EstimatedSizeBytes { get; init; }
    }

    private readonly object _gate = new();
    private readonly long _maxSizeBytes;
    private long _currentSizeBytes;
    private readonly Dictionary<string, LinkedListNode<CacheEntry>> _index = new(StringComparer.OrdinalIgnoreCase);
    private readonly LinkedList<CacheEntry> _lruList = new();

    public ImageMemoryCache(long maxSizeBytes = 64 * 1024 * 1024) // 64 MB default
    {
        _maxSizeBytes = Math.Max(1024 * 1024, maxSizeBytes);
    }

    public long CurrentSizeBytes
    {
        get { lock (_gate) return _currentSizeBytes; }
    }

    public int Count
    {
        get { lock (_gate) return _index.Count; }
    }

    private static string MakeKey(string key, ImageKind kind, ImageQuality quality) =>
        $"{kind}:{(int)quality}:{key}";

    public bool TryGet(string key, ImageKind kind, ImageQuality quality, out ImageSource? image)
    {
        image = null;
        if (string.IsNullOrWhiteSpace(key)) return false;

        string cacheKey = MakeKey(key, kind, quality);
        lock (_gate)
        {
            if (_index.TryGetValue(cacheKey, out var node))
            {
                _lruList.Remove(node);
                _lruList.AddFirst(node);
                image = node.Value.Image;
                return true;
            }
        }
        return false;
    }

    public void Set(string key, ImageKind kind, ImageQuality quality, ImageSource image)
    {
        if (string.IsNullOrWhiteSpace(key) || image == null) return;

        long sizeBytes = EstimateSizeBytes(image);
        string cacheKey = MakeKey(key, kind, quality);

        lock (_gate)
        {
            if (_index.TryGetValue(cacheKey, out var existingNode))
            {
                _lruList.Remove(existingNode);
                _currentSizeBytes -= existingNode.Value.EstimatedSizeBytes;
                _index.Remove(cacheKey);
            }

            var entry = new CacheEntry
            {
                CacheKey = cacheKey,
                Kind = kind,
                Quality = quality,
                Image = image,
                EstimatedSizeBytes = sizeBytes
            };

            var newNode = _lruList.AddFirst(entry);
            _index[cacheKey] = newNode;
            _currentSizeBytes += sizeBytes;

            // Evict if over budget
            while (_currentSizeBytes > _maxSizeBytes && _lruList.Last != null)
            {
                var victim = _lruList.Last;
                _lruList.RemoveLast();
                _index.Remove(victim.Value.CacheKey);
                _currentSizeBytes -= victim.Value.EstimatedSizeBytes;
            }
        }
    }

    public bool Remove(string key, ImageKind kind, ImageQuality quality)
    {
        if (string.IsNullOrWhiteSpace(key)) return false;
        string cacheKey = MakeKey(key, kind, quality);
        lock (_gate)
        {
            if (_index.TryGetValue(cacheKey, out var node))
            {
                _lruList.Remove(node);
                _currentSizeBytes -= node.Value.EstimatedSizeBytes;
                _index.Remove(cacheKey);
                return true;
            }
        }
        return false;
    }

    public void RemoveAllForSource(string key)
    {
        if (string.IsNullOrWhiteSpace(key)) return;
        lock (_gate)
        {
            var keysToRemove = new List<string>();
            foreach (var k in _index.Keys)
            {
                if (k.EndsWith(":" + key, StringComparison.OrdinalIgnoreCase))
                {
                    keysToRemove.Add(k);
                }
            }

            foreach (var k in keysToRemove)
            {
                if (_index.TryGetValue(k, out var node))
                {
                    _lruList.Remove(node);
                    _currentSizeBytes -= node.Value.EstimatedSizeBytes;
                    _index.Remove(k);
                }
            }
        }
    }

    public void Clear()
    {
        lock (_gate)
        {
            _index.Clear();
            _lruList.Clear();
            _currentSizeBytes = 0;
        }
    }

    private static long EstimateSizeBytes(ImageSource image)
    {
        if (image is BitmapSource bitmap)
        {
            int w = Math.Max(1, bitmap.PixelWidth);
            int h = Math.Max(1, bitmap.PixelHeight);
            int bpp = bitmap.Format.BitsPerPixel > 0 ? bitmap.Format.BitsPerPixel : 32;
            return ((long)w * h * bpp) / 8 + 128;
        }

        return 4096; // Approximate baseline for vector/drawing images
    }
}

