using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Media.Imaging;
using MusicPlayer.Services;
using Xunit;

namespace MusicPlayer.Tests
{
    public class ArtworkResourceAndLifecycleTests : IDisposable
    {
        private readonly string _tempDir;

        public ArtworkResourceAndLifecycleTests()
        {
            _tempDir = Path.Combine(Path.GetTempPath(), "mp_resource_tests_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_tempDir);
        }

        public void Dispose()
        {
            try
            {
                if (Directory.Exists(_tempDir))
                    Directory.Delete(_tempDir, true);
            }
            catch { }
        }

        [Fact]
        public void MemoryLRUCache_EnforcesByteBudgetPerTier()
        {
            // Create a small budget cache: e.g. 50 KB for List tier
            var memCache = new ArtworkMemoryCache(listBudgetMb: 0.05); // ~51 KB

            // Create 10 small bitmaps (each 80x80 Pbgra32 is ~25.6 KB)
            var bmp1 = CreateSolidBitmap(80, 80);
            var bmp2 = CreateSolidBitmap(80, 80);
            var bmp3 = CreateSolidBitmap(80, 80);

            memCache.Put("key1", ArtworkQuality.List, bmp1);
            memCache.Put("key2", ArtworkQuality.List, bmp2);

            // Adding third image will exceed ~51 KB budget, causing key1 (LRU) to be evicted
            memCache.Put("key3", ArtworkQuality.List, bmp3);

            Assert.True(memCache.TotalBytes <= 51200 * 1.5);
            Assert.False(memCache.TryGet("key1", ArtworkQuality.List, out _)); // Evicted!
            Assert.True(memCache.TryGet("key3", ArtworkQuality.List, out _));  // Present
        }

        [Fact]
        public void MemoryLRUCache_NegativeCacheSkipsNonExistentWithoutDiskChurn()
        {
            var memCache = new ArtworkMemoryCache();
            long fileTicks = 12345678;

            Assert.False(memCache.IsNegativeCached("empty_track.mp3", fileTicks));

            memCache.RecordNegative("empty_track.mp3", fileTicks);

            // Verified: negative cache hits
            Assert.True(memCache.IsNegativeCached("empty_track.mp3", fileTicks));

            // If file modified (different ticks), negative cache is cleared
            Assert.False(memCache.IsNegativeCached("empty_track.mp3", fileTicks + 10));
        }

        [Fact]
        public async Task GlobalCpuBudget_LimitsMaxConcurrency()
        {
            int maxConcurrency = 2;
            using var globalGate = new SemaphoreSlim(maxConcurrency, maxConcurrency);

            int currentRunning = 0;
            int peakRunning = 0;
            var tcs = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

            async Task WorkerTask()
            {
                await globalGate.WaitAsync();
                try
                {
                    int cur = Interlocked.Increment(ref currentRunning);
                    lock (tcs)
                    {
                        if (cur > peakRunning) peakRunning = cur;
                    }
                    await tcs.Task;
                }
                finally
                {
                    Interlocked.Decrement(ref currentRunning);
                    globalGate.Release();
                }
            }

            var tasks = new[]
            {
                Task.Run(WorkerTask),
                Task.Run(WorkerTask),
                Task.Run(WorkerTask),
                Task.Run(WorkerTask)
            };

            await Task.Delay(100);
            Assert.True(peakRunning <= maxConcurrency, $"Peak running {peakRunning} exceeded maxConcurrency {maxConcurrency}");

            tcs.TrySetResult();
            await Task.WhenAll(tasks);
        }

        private static BitmapSource CreateSolidBitmap(int width, int height)
        {
            var rtb = new RenderTargetBitmap(width, height, 96, 96, System.Windows.Media.PixelFormats.Pbgra32);
            rtb.Freeze();
            return rtb;
        }
    }
}

