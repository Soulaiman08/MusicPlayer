using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace MusicPlayer.Services
{
    /// <summary>
    /// Thread-safe LRU memory cache with strict byte budgets per quality tier.
    /// Prevents memory bloat while keeping visible images instantly accessible.
    /// </summary>
    public sealed class ArtworkMemoryCache
    {
        private sealed class CacheNode
        {
            public string Key = null!;
            public ImageSource Image = null!;
            public long ByteSize;
            public CacheNode? Prev;
            public CacheNode? Next;
        }

        private sealed class TierPartition
        {
            private readonly long _maxBytes;
            private long _currentBytes;
            private readonly Dictionary<string, CacheNode> _map = new(StringComparer.OrdinalIgnoreCase);
            private CacheNode? _head;
            private CacheNode? _tail;
            private readonly object _lock = new();

            public TierPartition(long maxBytes)
            {
                _maxBytes = maxBytes;
            }

            public bool TryGet(string key, out ImageSource? image)
            {
                lock (_lock)
                {
                    if (_map.TryGetValue(key, out var node))
                    {
                        MoveToHead(node);
                        image = node.Image;
                        return true;
                    }
                }
                image = null;
                return false;
            }

            public void Put(string key, ImageSource image, long byteSize)
            {
                lock (_lock)
                {
                    if (_map.TryGetValue(key, out var existing))
                    {
                        _currentBytes -= existing.ByteSize;
                        Interlocked.Add(ref ArtworkMetrics.MemorySizeBytes, -existing.ByteSize);
                        existing.Image = image;
                        existing.ByteSize = byteSize;
                        _currentBytes += byteSize;
                        Interlocked.Add(ref ArtworkMetrics.MemorySizeBytes, byteSize);
                        MoveToHead(existing);
                        EnsureBudget();
                        return;
                    }

                    var node = new CacheNode
                    {
                        Key = key,
                        Image = image,
                        ByteSize = byteSize
                    };

                    _map[key] = node;
                    _currentBytes += byteSize;
                    Interlocked.Add(ref ArtworkMetrics.MemorySizeBytes, byteSize);
                    AddToHead(node);
                    EnsureBudget();
                }
            }

            public bool Contains(string key)
            {
                lock (_lock)
                {
                    return _map.ContainsKey(key);
                }
            }

            public void Clear()
            {
                lock (_lock)
                {
                    Interlocked.Add(ref ArtworkMetrics.MemorySizeBytes, -_currentBytes);
                    _currentBytes = 0;
                    _map.Clear();
                    _head = null;
                    _tail = null;
                }
            }

            private void EnsureBudget()
            {
                while (_currentBytes > _maxBytes && _tail != null)
                {
                    var victim = _tail;
                    RemoveNode(victim);
                    _map.Remove(victim.Key);
                    _currentBytes -= victim.ByteSize;
                    Interlocked.Add(ref ArtworkMetrics.MemorySizeBytes, -victim.ByteSize);
                    Interlocked.Increment(ref ArtworkMetrics.MemoryEvictions);
                }
            }

            private void MoveToHead(CacheNode node)
            {
                if (ReferenceEquals(_head, node)) return;
                RemoveNode(node);
                AddToHead(node);
            }

            private void AddToHead(CacheNode node)
            {
                node.Next = _head;
                node.Prev = null;
                if (_head != null) _head.Prev = node;
                _head = node;
                if (_tail == null) _tail = node;
            }

            private void RemoveNode(CacheNode node)
            {
                if (node.Prev != null) node.Prev.Next = node.Next;
                else _head = node.Next;

                if (node.Next != null) node.Next.Prev = node.Prev;
                else _tail = node.Prev;

                node.Prev = null;
                node.Next = null;
            }
        }

        // Byte budgets per quality tier:
        // List:  16 MB (~650 items @ 80x80x4)
        // Mini:  10 MB (~100 items @ 160x160x4)
        // Cards: 40 MB (~180 items @ 240x240x4)
        // Full:  30 MB (~3-5 original full-resolution covers)
        private readonly TierPartition _listTier = new(16 * 1024 * 1024);
        private readonly TierPartition _miniTier = new(10 * 1024 * 1024);
        private readonly TierPartition _cardsTier = new(40 * 1024 * 1024);
        private readonly TierPartition _fullTier = new(30 * 1024 * 1024);

        // Negative cache: paths/keys proven to contain no artwork.
        // Stores source file modification ticks to automatically invalidate if the file is replaced.
        private readonly ConcurrentDictionary<string, long> _negativeCache = new(StringComparer.OrdinalIgnoreCase);

        private TierPartition GetPartition(ArtworkQuality quality) => quality switch
        {
            ArtworkQuality.List => _listTier,
            ArtworkQuality.Mini => _miniTier,
            ArtworkQuality.Cards => _cardsTier,
            ArtworkQuality.Full => _fullTier,
            _ => _cardsTier
        };

        public bool TryGet(string key, ArtworkQuality quality, out ImageSource? image)
        {
            if (string.IsNullOrWhiteSpace(key))
            {
                image = null;
                return false;
            }

            var partition = GetPartition(quality);
            if (partition.TryGet(key, out image))
            {
                Interlocked.Increment(ref ArtworkMetrics.MemoryHits);
                return true;
            }

            Interlocked.Increment(ref ArtworkMetrics.MemoryMisses);
            image = null;
            return false;
        }

        public void Put(string key, ArtworkQuality quality, ImageSource image)
        {
            if (string.IsNullOrWhiteSpace(key) || image == null) return;
            long byteSize = EstimateByteSize(image, quality);
            var partition = GetPartition(quality);
            partition.Put(key, image, byteSize);
            _negativeCache.TryRemove(key, out _);
        }

        public bool IsNegativeCached(string key, long currentFileTicks = 0)
        {
            if (string.IsNullOrWhiteSpace(key)) return true;
            if (_negativeCache.TryGetValue(key, out long cachedTicks))
            {
                if (currentFileTicks == 0 || currentFileTicks == cachedTicks)
                {
                    Interlocked.Increment(ref ArtworkMetrics.NegativeHits);
                    return true;
                }
                _negativeCache.TryRemove(key, out _);
            }
            return false;
        }

        public void RecordNegative(string key, long sourceFileTicks = 0)
        {
            if (string.IsNullOrWhiteSpace(key)) return;
            _negativeCache[key] = sourceFileTicks;
        }

        public void ClearNegative(string key)
        {
            if (!string.IsNullOrWhiteSpace(key))
                _negativeCache.TryRemove(key, out _);
        }

        public void Clear()
        {
            _listTier.Clear();
            _miniTier.Clear();
            _cardsTier.Clear();
            _fullTier.Clear();
            _negativeCache.Clear();
        }

        private static long EstimateByteSize(ImageSource image, ArtworkQuality quality)
        {
            if (image is BitmapSource bs)
            {
                int w = bs.PixelWidth;
                int h = bs.PixelHeight;
                int bpp = bs.Format.BitsPerPixel > 0 ? bs.Format.BitsPerPixel : 32;
                if (w > 0 && h > 0)
                {
                    return (long)w * h * (bpp / 8);
                }
            }
            int fallbackDim = quality == ArtworkQuality.Full ? 500 : (int)quality;
            return (long)fallbackDim * fallbackDim * 4;
        }
    }
}

