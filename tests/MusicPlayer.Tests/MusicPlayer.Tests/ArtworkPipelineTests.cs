using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using MusicPlayer.Models;
using MusicPlayer.Services;
using Xunit;

namespace MusicPlayer.Tests
{
    public class ArtworkPipelineTests : IDisposable
    {
        private readonly string _tempCacheDir;

        public ArtworkPipelineTests()
        {
            _tempCacheDir = Path.Combine(Path.GetTempPath(), "mp_pipeline_tests_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_tempCacheDir);
        }

        public void Dispose()
        {
            try
            {
                if (Directory.Exists(_tempCacheDir))
                    Directory.Delete(_tempCacheDir, true);
            }
            catch { }
        }

        [Fact]
        public void BoostPriority_PhysicallyMovesRequestToHigherQueue()
        {
            var memCache = new ArtworkMemoryCache();
            var diskCache = new ArtworkDiskCache(_tempCacheDir);
            using var pipeline = new ArtworkPipeline(memCache, diskCache, maxWorkers: 0); // 0 workers so items stay in queue

            // Enqueue request at Maintenance (lowest priority = 4)
            var req = pipeline.Enqueue("track_boost_test.mp3", ArtworkQuality.List, ArtworkPriority.Maintenance);
            Assert.Equal(ArtworkPriority.Maintenance, req.Priority);
            Assert.Equal(1, pipeline.GetQueueCount(ArtworkPriority.Maintenance));
            Assert.Equal(0, pipeline.GetQueueCount(ArtworkPriority.ImmediateVisible));

            // Boost priority to ImmediateVisible (0)
            pipeline.BoostRequest(req, ArtworkPriority.ImmediateVisible);

            // Verified: request must have physically moved from Maintenance queue to ImmediateVisible queue
            Assert.Equal(ArtworkPriority.ImmediateVisible, req.Priority);
            Assert.Equal(0, pipeline.GetQueueCount(ArtworkPriority.Maintenance));
            Assert.Equal(1, pipeline.GetQueueCount(ArtworkPriority.ImmediateVisible));
        }

        [Fact]
        public async Task DeduplicateRequests_SharesResultBetweenConsumers()
        {
            var memCache = new ArtworkMemoryCache();
            var diskCache = new ArtworkDiskCache(_tempCacheDir);
            using var pipeline = new ArtworkPipeline(memCache, diskCache, maxWorkers: 1);

            // Generate a real dummy source image file
            var dummyFile = Path.Combine(_tempCacheDir, "test_track.jpg");
            File.WriteAllBytes(dummyFile, Create1x1Png());

            var task1 = pipeline.GetAsync(dummyFile, ArtworkQuality.List, ArtworkPriority.ImmediateVisible);
            var task2 = pipeline.GetAsync(dummyFile, ArtworkQuality.List, ArtworkPriority.ImmediateVisible);

            var img1 = await task1;
            var img2 = await task2;

            Assert.NotNull(img1);
            Assert.NotNull(img2);
            Assert.Same(img1, img2);
            Assert.True(ArtworkMetrics.RequestsDeduplicated >= 1);
        }

        [Fact]
        public async Task ConsumerCancellation_IsolatedFromOtherConsumers()
        {
            var memCache = new ArtworkMemoryCache();
            var diskCache = new ArtworkDiskCache(_tempCacheDir);
            // 0 workers initially so the request sits in queue
            using var pipeline = new ArtworkPipeline(memCache, diskCache, maxWorkers: 0);

            var dummyFile = Path.Combine(_tempCacheDir, "shared_track.jpg");
            File.WriteAllBytes(dummyFile, Create1x1Png());

            using var ctsConsumer1 = new CancellationTokenSource();
            using var ctsConsumer2 = new CancellationTokenSource();

            var task1 = pipeline.GetAsync(dummyFile, ArtworkQuality.List, ArtworkPriority.ImmediateVisible, ctsConsumer1.Token);
            var task2 = pipeline.GetAsync(dummyFile, ArtworkQuality.List, ArtworkPriority.ImmediateVisible, ctsConsumer2.Token);

            // Consumer 1 cancels
            ctsConsumer1.Cancel();

            // Task 1 must be cancelled
            await Assert.ThrowsAnyAsync<OperationCanceledException>(async () => await task1);

            // Crucial: Task 2 must NOT be cancelled!
            Assert.False(task2.IsCanceled);
            Assert.False(task2.IsCompleted);

            // Now start a worker to process it
            pipeline.StartWorker();
            var img2 = await task2;
            Assert.NotNull(img2);
        }

        [Fact]
        public async Task QueueResumes_WhenInteractionStops()
        {
            var memCache = new ArtworkMemoryCache();
            var diskCache = new ArtworkDiskCache(_tempCacheDir);
            using var pipeline = new ArtworkPipeline(memCache, diskCache, maxWorkers: 1);

            var dummyFile = Path.Combine(_tempCacheDir, "bg_resume.jpg");
            File.WriteAllBytes(dummyFile, Create1x1Png());

            // Simulate user interaction active
            pipeline.SetInteractionState(true);

            // Request at BackgroundLibrary (should not process during interaction)
            var bgTask = pipeline.GetAsync(dummyFile, ArtworkQuality.List, ArtworkPriority.BackgroundLibrary);

            await Task.Delay(100);
            Assert.False(bgTask.IsCompleted);

            // Interaction stops: pipeline must resume and complete the background request
            pipeline.SetInteractionState(false);
            pipeline.ResumeQueueProcessing();

            var img = await bgTask.WaitAsync(TimeSpan.FromSeconds(3));
            Assert.NotNull(img);
        }

        [Fact]
        public async Task Shutdown_CancelsPendingRequestsAndDrainsWorkers()
        {
            var memCache = new ArtworkMemoryCache();
            var diskCache = new ArtworkDiskCache(_tempCacheDir);
            var pipeline = new ArtworkPipeline(memCache, diskCache, maxWorkers: 0);

            var task1 = pipeline.GetAsync("pending1.mp3", ArtworkQuality.List, ArtworkPriority.BackgroundLibrary);
            var task2 = pipeline.GetAsync("pending2.mp3", ArtworkQuality.List, ArtworkPriority.BackgroundLibrary);

            // Stop pipeline gracefully
            await pipeline.StopAsync();

            // All pending requests must complete in cancelled state
            await Assert.ThrowsAnyAsync<OperationCanceledException>(async () => await task1);
            await Assert.ThrowsAnyAsync<OperationCanceledException>(async () => await task2);

            Assert.True(pipeline.IsShutdown);
            pipeline.Dispose();
        }

        [Fact]
        public async Task Shutdown_IsIdempotent()
        {
            var memCache = new ArtworkMemoryCache();
            var diskCache = new ArtworkDiskCache(_tempCacheDir);
            var pipeline = new ArtworkPipeline(memCache, diskCache, maxWorkers: 1);

            await pipeline.StopAsync();
            await pipeline.StopAsync(); // Should not throw
            pipeline.Dispose();
            pipeline.Dispose(); // Should not throw
        }

        private static byte[] Create1x1Png()
        {
            return new byte[]
            {
                0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A,
                0x00, 0x00, 0x00, 0x0D, 0x49, 0x48, 0x44, 0x52,
                0x00, 0x00, 0x00, 0x01, 0x00, 0x00, 0x00, 0x01,
                0x08, 0x06, 0x00, 0x00, 0x00, 0x1F, 0x15, 0xC4,
                0x89, 0x00, 0x00, 0x00, 0x0A, 0x49, 0x44, 0x41,
                0x54, 0x78, 0x9C, 0x63, 0x00, 0x01, 0x00, 0x00,
                0x05, 0x00, 0x01, 0x0D, 0x0A, 0x2D, 0xB4, 0x00,
                0x00, 0x00, 0x00, 0x49, 0x45, 0x4E, 0x44, 0xAE,
                0x42, 0x60, 0x82
            };
        }
    }
}

