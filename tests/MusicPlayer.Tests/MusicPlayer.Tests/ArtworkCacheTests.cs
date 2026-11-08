using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Media.Imaging;
using MusicPlayer.Models;
using MusicPlayer.Services;
using Xunit;

namespace MusicPlayer.Tests
{
    public class ArtworkCacheTests : IDisposable
    {
        private readonly string _tempDir;
        private readonly string _tempCacheDir;

        public ArtworkCacheTests()
        {
            _tempDir = Path.Combine(Path.GetTempPath(), "mp_cache_test_" + Guid.NewGuid().ToString("N"));
            _tempCacheDir = Path.Combine(_tempDir, "Cache");
            Directory.CreateDirectory(_tempDir);
            Directory.CreateDirectory(_tempCacheDir);
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
        public void NegativeCache_InvalidatesWhenSourceFileModified()
        {
            var diskCache = new ArtworkDiskCache(_tempCacheDir);
            var testFile = Path.Combine(_tempDir, "dummy_audio.mp3");
            File.WriteAllText(testFile, "test audio data v1");

            long t1 = File.GetLastWriteTimeUtc(testFile).Ticks;
            diskCache.RecordNegativeDisk(testFile, t1);

            // Negative cache should hit
            Assert.False(diskCache.TryGetDerivativeFile(testFile, ArtworkQuality.List, out _, out _));
            Assert.True(File.Exists(diskCache.GetNoArtPath(testFile)));

            // Simulate file modification (e.g. tag editor added artwork or modified file)
            File.WriteAllText(testFile, "test audio data v2 modified");
            File.SetLastWriteTimeUtc(testFile, DateTime.UtcNow.AddMinutes(1));

            // When file changed, TryGetDerivativeFile must detect change and remove old noart entry
            diskCache.TryGetDerivativeFile(testFile, ArtworkQuality.List, out _, out _);
            Assert.False(File.Exists(diskCache.GetNoArtPath(testFile)));
        }

        [Fact]
        public void SidecarDetection_InvalidatesWhenCoverArtAdded()
        {
            var diskCache = new ArtworkDiskCache(_tempCacheDir);
            var albumDir = Path.Combine(_tempDir, "AlbumFolder");
            Directory.CreateDirectory(albumDir);

            var trackFile = Path.Combine(albumDir, "song1.mp3");
            File.WriteAllText(trackFile, "song content");
            long trackTicks = File.GetLastWriteTimeUtc(trackFile).Ticks;

            // Initially recorded as no-art
            diskCache.RecordNegativeDisk(trackFile, trackTicks);
            Assert.True(File.Exists(diskCache.GetNoArtPath(trackFile)));

            // User drops a cover.jpg into the album folder
            var coverJpg = Path.Combine(albumDir, "cover.jpg");
            File.WriteAllBytes(coverJpg, Create1x1Png());

            // Check sidecar invalidation
            bool invalidated = diskCache.CheckAndInvalidateSidecar(trackFile);
            Assert.True(invalidated);
            Assert.False(File.Exists(diskCache.GetNoArtPath(trackFile)));
        }

        [Fact]
        public void CacheMetadata_ValidatesVersionAndSourceTicks()
        {
            var diskCache = new ArtworkDiskCache(_tempCacheDir);
            var testFile = Path.Combine(_tempDir, "track_meta.mp3");
            File.WriteAllText(testFile, "content");
            long ticks = File.GetLastWriteTimeUtc(testFile).Ticks;

            var metaPath = diskCache.GetMetaPath(testFile);
            diskCache.SaveMeta(testFile, ticks);
            Assert.True(File.Exists(metaPath));

            // Valid ticks check
            Assert.True(diskCache.ValidateTicks(metaPath, ticks));

            // Mismatched ticks check (e.g. file updated)
            Assert.False(diskCache.ValidateTicks(metaPath, ticks + 1000));
        }

        [Fact]
        public void CorruptedDerivative_IsDetectedAndRemoved()
        {
            var diskCache = new ArtworkDiskCache(_tempCacheDir);
            var testFile = Path.Combine(_tempDir, "track_corrupt.mp3");
            File.WriteAllText(testFile, "content");
            long ticks = File.GetLastWriteTimeUtc(testFile).Ticks;

            // Create a 0-byte corrupted derivative file
            string derivPath = diskCache.GetDerivativePath(testFile, ArtworkQuality.List, false);
            File.WriteAllBytes(derivPath, Array.Empty<byte>());
            diskCache.SaveMeta(testFile, ticks);

            // TryGetDerivativeFile must detect 0-byte corrupt file, delete it, and return false
            bool found = diskCache.TryGetDerivativeFile(testFile, ArtworkQuality.List, out _, out _);
            Assert.False(found);
            Assert.False(File.Exists(derivPath));
        }

        [Fact]
        public async Task SingleExtraction_ForMultipleQualityTiers()
        {
            var memCache = new ArtworkMemoryCache();
            var diskCache = new ArtworkDiskCache(_tempCacheDir);
            using var pipeline = new ArtworkPipeline(memCache, diskCache, maxWorkers: 1);

            var dummyFile = Path.Combine(_tempDir, "single_extract.jpg");
            File.WriteAllBytes(dummyFile, Create1x1Png());

            long initialTagLib = ArtworkMetrics.TagLibParses;

            // Request List (80px), Mini (160px), Cards (240px) concurrently
            var t1 = pipeline.GetAsync(dummyFile, ArtworkQuality.List);
            var t2 = pipeline.GetAsync(dummyFile, ArtworkQuality.Mini);
            var t3 = pipeline.GetAsync(dummyFile, ArtworkQuality.Cards);

            await Task.WhenAll(t1, t2, t3);

            // TagLib extraction count must be at most 1 (or 0 since direct image)
            long deltaTagLib = ArtworkMetrics.TagLibParses - initialTagLib;
            Assert.True(deltaTagLib <= 1);

            // Derivatives must be saved on disk for requested tiers
            Assert.True(File.Exists(diskCache.GetDerivativePath(dummyFile, ArtworkQuality.List, false)) ||
                        File.Exists(diskCache.GetDerivativePath(dummyFile, ArtworkQuality.List, true)));
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

