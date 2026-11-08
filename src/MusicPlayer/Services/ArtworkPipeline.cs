using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using MusicPlayer.Models;
using MusicPlayer.Utilities;
using MusicPlayer.Views;

namespace MusicPlayer.Services
{
    /// <summary>
    /// Centralized, high-performance artwork pipeline.
    /// Unifies image decoding, priority queueing, In-Flight request deduplication,
    /// multi-tier disk derivatives, and LRU memory cache across the entire application.
    /// Guarantees zero blocking disk I/O and zero synchronous decodes on the UI thread.
    /// </summary>
    public sealed class ArtworkPipeline : IDisposable
    {
        private static ArtworkPipeline? _instance;
        public static ArtworkPipeline Instance => _instance ??= new ArtworkPipeline();

        public ArtworkMemoryCache MemoryCache { get; }
        public ArtworkDiskCache DiskCache { get; }

        private readonly ConcurrentDictionary<(string Key, ArtworkQuality Quality), ArtworkRequest> _inFlight = new();
        private readonly LinkedList<ArtworkRequest>[] _queues;
        private readonly object _queueLock = new();
        private readonly SemaphoreSlim _signal = new(0);

        private readonly ConcurrentQueue<string> _batchedReadyKeys = new();
        private int _batchFlushScheduled;

        public event Action<IReadOnlySet<string>>? ArtworkBatchReady;

        private ImageSource? _defaultArtwork;
        private readonly CancellationTokenSource _cts = new();
        private int _activeWorkers;
        private bool _disposed;

        public ArtworkPipeline(
            ArtworkMemoryCache? memoryCache = null,
            ArtworkDiskCache? diskCache = null)
        {
            MemoryCache = memoryCache ?? new ArtworkMemoryCache();
            DiskCache = diskCache ?? new ArtworkDiskCache();

            int priorityLevels = Enum.GetValues<ArtworkPriority>().Length;
            _queues = new LinkedList<ArtworkRequest>[priorityLevels];
            for (int i = 0; i < priorityLevels; i++)
            {
                _queues[i] = new LinkedList<ArtworkRequest>();
            }

            _instance = this;
        }

        public ImageSource GetDefaultArtwork()
        {
            if (_defaultArtwork != null) return _defaultArtwork;
            try
            {
                var uri = new Uri("pack://application:,,,/Assets/default_album_art.png", UriKind.Absolute);
                var bi = new BitmapImage();
                bi.BeginInit();
                bi.UriSource = uri;
                bi.DecodePixelWidth = 80;
                bi.CacheOption = BitmapCacheOption.OnLoad;
                bi.EndInit();
                bi.Freeze();
                _defaultArtwork = bi;
                return _defaultArtwork;
            }
            catch
            {
                var dv = new DrawingVisual();
                using (var dc = dv.RenderOpen())
                {
                    dc.DrawRectangle(Brushes.DarkGray, null, new Rect(0, 0, 80, 80));
                }
                var rtb = new RenderTargetBitmap(80, 80, 96, 96, PixelFormats.Pbgra32);
                rtb.Render(dv);
                rtb.Freeze();
                _defaultArtwork = rtb;
                return _defaultArtwork;
            }
        }

        /// <summary>
        /// Fast non-blocking query: returns cached image if already in memory, otherwise null.
        /// </summary>
        public ImageSource? GetCached(string? key, ArtworkQuality quality)
        {
            if (string.IsNullOrWhiteSpace(key)) return null;
            if (MemoryCache.TryGet(key, quality, out var img))
                return img;
            return null;
        }

        /// <summary>
        /// Fast UI accessor: returns cached image immediately if available.
        /// If not in memory, returns frozen default placeholder immediately and enqueues async decode.
        /// Guaranteed ZERO disk I/O and ZERO TagLib execution on UI thread.
        /// </summary>
        public ImageSource? GetOrRequest(
            string? key,
            ArtworkQuality quality,
            ArtworkPriority priority = ArtworkPriority.ImmediateVisible,
            MusicTrack? track = null)
        {
            if (string.IsNullOrWhiteSpace(key))
                return GetDefaultArtwork();

            if (MemoryCache.TryGet(key, quality, out var cached) && cached != null)
                return cached;

            if (MemoryCache.IsNegativeCached(key))
                return GetDefaultArtwork();

            // Enqueue request asynchronously
            Enqueue(key, quality, priority, track);
            return GetDefaultArtwork();
        }

        /// <summary>
        /// Asynchronously retrieves the requested artwork, sharing results with in-flight decodes.
        /// </summary>
        public Task<ImageSource?> GetAsync(
            string key,
            ArtworkQuality quality,
            ArtworkPriority priority,
            CancellationToken cancellationToken)
            => GetAsync(key, quality, priority, null, cancellationToken);

        public Task<ImageSource?> GetAsync(
            string key,
            ArtworkQuality quality,
            ArtworkPriority priority = ArtworkPriority.ImmediateVisible,
            MusicTrack? track = null,
            CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrWhiteSpace(key))
                return Task.FromResult<ImageSource?>(GetDefaultArtwork());

            if (MemoryCache.TryGet(key, quality, out var cached) && cached != null)
                return Task.FromResult<ImageSource?>(cached);

            if (MemoryCache.IsNegativeCached(key))
                return Task.FromResult<ImageSource?>(GetDefaultArtwork());

            var request = Enqueue(key, quality, priority, track, cancellationToken);
            return request.TaskCompletionSource.Task;
        }

        public ArtworkRequest Enqueue(
            string key,
            ArtworkQuality quality,
            ArtworkPriority priority,
            CancellationToken cancellationToken)
            => Enqueue(key, quality, priority, null, cancellationToken);

        public ArtworkRequest Enqueue(
            string key,
            ArtworkQuality quality,
            ArtworkPriority priority,
            MusicTrack? track = null,
            CancellationToken cancellationToken = default)
        {
            Interlocked.Increment(ref ArtworkMetrics.RequestsTotal);

            var inFlightKey = (key, quality);
            if (_inFlight.TryGetValue(inFlightKey, out var existing))
            {
                Interlocked.Increment(ref ArtworkMetrics.RequestsDeduplicated);
                existing.BoostPriority(priority);
                return existing;
            }

            var request = new ArtworkRequest(key, quality, priority, track, cancellationToken);
            if (!_inFlight.TryAdd(inFlightKey, request))
            {
                if (_inFlight.TryGetValue(inFlightKey, out var concurrent))
                {
                    Interlocked.Increment(ref ArtworkMetrics.RequestsDeduplicated);
                    concurrent.BoostPriority(priority);
                    return concurrent;
                }
            }

            lock (_queueLock)
            {
                int bucket = (int)priority;
                _queues[bucket].AddLast(request);
                Interlocked.Increment(ref ArtworkMetrics.QueuedRequests);
                EnsureWorkerStartedLocked();
            }

            _signal.Release();
            return request;
        }

        private void EnsureWorkerStartedLocked()
        {
            if (_disposed || _cts.IsCancellationRequested) return;

            // Worker concurrency policy:
            // 1 worker during active user interaction or startup.
            // Up to 2 workers when idle on multi-core systems (>= 4 cores).
            bool isInteracting = SmoothScrollHelper.IsInteractingNow(400);
            int maxWorkers = (isInteracting || Environment.ProcessorCount < 4) ? 1 : 2;

            if (_activeWorkers < maxWorkers)
            {
                _activeWorkers++;
                ArtworkMetrics.ActiveWorkers = _activeWorkers;
                _ = Task.Run(WorkerLoopAsync);
            }
        }

        private async Task WorkerLoopAsync()
        {
            while (!_disposed && !_cts.IsCancellationRequested)
            {
                ArtworkRequest? request = null;

                try
                {
                    await _signal.WaitAsync(TimeSpan.FromSeconds(1), _cts.Token);
                }
                catch (OperationCanceledException)
                {
                    break;
                }

                lock (_queueLock)
                {
                    bool isInteracting = SmoothScrollHelper.IsInteractingNow(300);
                    // When user is interacting, only process visible elements
                    int maxBucket = isInteracting ? (int)ArtworkPriority.StartupViewport : (int)ArtworkPriority.Maintenance;

                    for (int b = 0; b <= maxBucket; b++)
                    {
                        if (_queues[b].Count > 0)
                        {
                            request = _queues[b].First!.Value;
                            _queues[b].RemoveFirst();
                            Interlocked.Decrement(ref ArtworkMetrics.QueuedRequests);
                            break;
                        }
                    }

                    if (request == null)
                    {
                        _activeWorkers--;
                        ArtworkMetrics.ActiveWorkers = _activeWorkers;
                        return;
                    }
                }

                if (request.CancellationToken.IsCancellationRequested || _cts.IsCancellationRequested)
                {
                    request.TaskCompletionSource.TrySetCanceled();
                    _inFlight.TryRemove((request.Key, request.Quality), out _);
                    continue;
                }

                try
                {
                    var result = await ProcessRequestAsync(request);
                    request.TaskCompletionSource.TrySetResult(result);
                    Interlocked.Increment(ref ArtworkMetrics.RequestsCompleted);

                    if (result != null)
                    {
                        NotifyArtworkReady(request.Key);
                    }
                }
                catch (Exception ex)
                {
                    request.TaskCompletionSource.TrySetException(ex);
                    Interlocked.Increment(ref ArtworkMetrics.RequestsFailed);
                }
                finally
                {
                    _inFlight.TryRemove((request.Key, request.Quality), out _);
                }
            }

            lock (_queueLock)
            {
                _activeWorkers--;
                ArtworkMetrics.ActiveWorkers = _activeWorkers;
            }
        }

        private async Task<ImageSource?> ProcessRequestAsync(ArtworkRequest request)
        {
            string path = request.Key;
            var quality = request.Quality;
            long t0 = Stopwatch.GetTimestamp();

            // 1. Memory Cache check
            if (MemoryCache.TryGet(path, quality, out var memImg) && memImg != null)
                return memImg;

            // 2. Negative Cache check
            long currentTicks = 0;
            if (File.Exists(path))
            {
                currentTicks = File.GetLastWriteTimeUtc(path).Ticks;
            }
            if (MemoryCache.IsNegativeCached(path, currentTicks))
                return null;

            // 3. Disk Cache check
            if (DiskCache.TryGetDerivativeFile(path, quality, out var derivativeFile, out bool isExact) && !string.IsNullOrEmpty(derivativeFile))
            {
                int targetWidth = quality == ArtworkQuality.Full ? 0 : (int)quality;
                var diskImg = LoadBitmapFromFile(derivativeFile, targetWidth);
                if (diskImg != null)
                {
                    MemoryCache.Put(path, quality, diskImg);
                    if (!isExact && quality != ArtworkQuality.Full)
                    {
                        // Generate exact derivative from original disk cache image
                        DiskCache.SaveDerivative(path, quality, diskImg, currentTicks);
                    }

                    double ms = (Stopwatch.GetTimestamp() - t0) * 1000.0 / Stopwatch.Frequency;
                    ArtworkMetrics.RecordDecode(quality, ms, Application.Current?.Dispatcher?.CheckAccess() == true);
                    return diskImg;
                }
            }

            // 4. Source Extraction (Audio Tag or Sidecar File)
            var extracted = await Task.Run(() => ExtractArtworkFromSource(path, request.Track, quality, currentTicks));
            if (extracted != null)
            {
                MemoryCache.Put(path, quality, extracted);
                double ms = (Stopwatch.GetTimestamp() - t0) * 1000.0 / Stopwatch.Frequency;
                ArtworkMetrics.RecordDecode(quality, ms, Application.Current?.Dispatcher?.CheckAccess() == true);
                return extracted;
            }

            // 5. No art found: record in Negative Cache
            MemoryCache.RecordNegative(path, currentTicks);
            DiskCache.RecordNegativeDisk(path, currentTicks);
            return null;
        }

        private ImageSource? ExtractArtworkFromSource(string path, MusicTrack? track, ArtworkQuality quality, long sourceTicks)
        {
            int targetWidth = quality == ArtworkQuality.Full ? 0 : (int)quality;

            // Check if source path is directly an image file (e.g. artist image or album art path)
            string ext = Path.GetExtension(path).ToLowerInvariant();
            if (ext is ".jpg" or ".jpeg" or ".png" or ".webp" or ".bmp")
            {
                if (File.Exists(path))
                {
                    var directImg = LoadBitmapFromFile(path, targetWidth);
                    if (directImg != null)
                    {
                        if (quality != ArtworkQuality.Full)
                            DiskCache.SaveDerivative(path, quality, directImg, sourceTicks);
                        return directImg;
                    }
                }
            }

            // Audio File Tag extraction via TagLib
            if (File.Exists(path))
            {
                Interlocked.Increment(ref ArtworkMetrics.TagLibParses);
                try
                {
                    using var tagFile = TagLib.File.Create(path);
                    foreach (var picture in tagFile.Tag.Pictures)
                    {
                        if (picture.Data.Data == null || picture.Data.Data.Length == 0) continue;

                        var origDiskPath = DiskCache.SaveExtractedOriginal(path, picture.Data.Data, sourceTicks);
                        using var ms = new MemoryStream(picture.Data.Data);
                        var img = LoadBitmapFromStream(ms, targetWidth);
                        if (img != null)
                        {
                            if (quality != ArtworkQuality.Full)
                                DiskCache.SaveDerivative(path, quality, img, sourceTicks);
                            return img;
                        }
                    }
                }
                catch { }

                // Check sidecar files in same directory (cover.jpg, folder.jpg)
                try
                {
                    var dir = Path.GetDirectoryName(path);
                    if (!string.IsNullOrEmpty(dir) && Directory.Exists(dir))
                    {
                        foreach (var name in new[] { "cover.jpg", "folder.jpg", "front.jpg", "album.jpg", "cover.png", "folder.png" })
                        {
                            var sidecar = Path.Combine(dir, name);
                            if (File.Exists(sidecar))
                            {
                                var img = LoadBitmapFromFile(sidecar, targetWidth);
                                if (img != null)
                                {
                                    if (quality != ArtworkQuality.Full)
                                        DiskCache.SaveDerivative(path, quality, img, sourceTicks);
                                    return img;
                                }
                            }
                        }
                    }
                }
                catch { }
            }

            return null;
        }

        private static BitmapImage? LoadBitmapFromFile(string filePath, int decodeWidth)
        {
            try
            {
                var bi = new BitmapImage();
                bi.BeginInit();
                bi.UriSource = new Uri(filePath, UriKind.Absolute);
                if (decodeWidth > 0)
                    bi.DecodePixelWidth = decodeWidth;
                bi.CacheOption = BitmapCacheOption.OnLoad;
                bi.CreateOptions = BitmapCreateOptions.IgnoreColorProfile;
                bi.EndInit();
                bi.Freeze();
                return bi;
            }
            catch
            {
                return null;
            }
        }

        private static BitmapImage? LoadBitmapFromStream(Stream stream, int decodeWidth)
        {
            try
            {
                var bi = new BitmapImage();
                bi.BeginInit();
                bi.StreamSource = stream;
                if (decodeWidth > 0)
                    bi.DecodePixelWidth = decodeWidth;
                bi.CacheOption = BitmapCacheOption.OnLoad;
                bi.CreateOptions = BitmapCreateOptions.IgnoreColorProfile;
                bi.EndInit();
                bi.Freeze();
                return bi;
            }
            catch
            {
                return null;
            }
        }

        private void NotifyArtworkReady(string key)
        {
            _batchedReadyKeys.Enqueue(key);
            if (Interlocked.CompareExchange(ref _batchFlushScheduled, 1, 0) == 0)
            {
                var dispatcher = Application.Current?.Dispatcher;
                if (dispatcher != null && !dispatcher.HasShutdownStarted)
                {
                    dispatcher.BeginInvoke(DispatcherPriority.Background, new Action(() =>
                    {
                        Interlocked.Exchange(ref _batchFlushScheduled, 0);
                        var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                        while (_batchedReadyKeys.TryDequeue(out var k))
                        {
                            set.Add(k);
                        }
                        if (set.Count > 0)
                        {
                            ArtworkBatchReady?.Invoke(set);
                        }
                    }));
                }
                else
                {
                    Interlocked.Exchange(ref _batchFlushScheduled, 0);
                    var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                    while (_batchedReadyKeys.TryDequeue(out var k))
                    {
                        set.Add(k);
                    }
                    if (set.Count > 0)
                    {
                        ArtworkBatchReady?.Invoke(set);
                    }
                }
            }
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            _cts.Cancel();
            _signal.Dispose();
            _cts.Dispose();
            MemoryCache.Clear();
        }
    }
}

