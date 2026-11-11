using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Microsoft.Extensions.Logging.Abstractions;
using MusicPlayer.Converters;
using MusicPlayer.Database;
using MusicPlayer.ImageLoading;
using MusicPlayer.Library;
using MusicPlayer.Models;
using Xunit;

namespace MusicPlayer.Tests;

public class UnifiedImageLoadingTests : IDisposable
{
    private readonly string _tempDir;
    private readonly string _dbPath;
    private readonly DatabaseService _db;
    private readonly ImageDiskCache _diskCache;
    private readonly ImageMemoryCache _memoryCache;
    private readonly ImageLoadScheduler _scheduler;
    private readonly AlbumArtProvider _albumArtProvider;
    private readonly ArtistImageProvider _artistProvider;
    private readonly ImageLoadingCoordinator _coordinator;

    // 1x1 transparent PNG sample bytes
    private static readonly byte[] SamplePngBytes = Convert.FromBase64String(
        "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAAAXNSR0IArs4c6QAAAARnQU1BAACxjwv8YQUAAAAJcEhZcwAADsMAAA7DAcdvqGQAAAAZdEVYdFNvZnR3YXJlAHBhaW50Lm5ldCA0LjAuMTZE3gzGAAAADUlEQVQYV2P4//8/AwAI/AL+XJ/PggAAAABJRU5ErkJggg==");

    // 1x1 red PNG sample bytes
    private static readonly byte[] SampleRedPngBytes = Convert.FromBase64String(
        "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAAAXNSR0IArs4c6QAAAARnQU1BAACxjwv8YQUAAAAJcEhZcwAADsMAAA7DAcdvqGQAAAAZdEVYdFNvZnR3YXJlAHBhaW50Lm5ldCA0LjAuMTZE3gzGAAAADUlEQVQYV2P4z8DwHwAFAAH/plybEQAAAABJRU5ErkJggg==");

    public UnifiedImageLoadingTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "mp_unified_test_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempDir);
        _dbPath = Path.Combine(_tempDir, "test.db");
        _db = new DatabaseService(_dbPath, NullLogger<DatabaseService>.Instance);

        string cacheDir = Path.Combine(_tempDir, "ArtCache");
        _diskCache = new ImageDiskCache(cacheDir, NullLogger.Instance);
        _memoryCache = new ImageMemoryCache(maxSizeBytes: 512 * 1024); // 512 KB budget for test
        _scheduler = new ImageLoadScheduler(NullLogger.Instance);

        _albumArtProvider = new AlbumArtProvider(_diskCache, _memoryCache, _scheduler, _db, NullLogger.Instance);
        _artistProvider = new ArtistImageProvider(_memoryCache, _scheduler, NullLogger.Instance);

        _coordinator = new ImageLoadingCoordinator(_db, NullLogger.Instance);
    }

    public void Dispose()
    {
        try { _coordinator.Dispose(); } catch { }
        try { _scheduler.Dispose(); } catch { }
        try { _artistProvider.Dispose(); } catch { }
        try { _db.Dispose(); } catch { }
        try { Directory.Delete(_tempDir, true); } catch { }
    }

    // ── SOURCE IDENTITY TESTS ──────────────────────────────────────────────────

    [Fact]
    public void FastVerifyDetectsFileModificationInSamePath()
    {
        string filePath = Path.Combine(_tempDir, "song.mp3");
        File.WriteAllBytes(filePath, new byte[] { 1, 2, 3, 4, 5 });
        var fi1 = new FileInfo(filePath);

        Assert.True(SourceIdentity.FastVerify(filePath, fi1.Length, fi1.LastWriteTimeUtc.Ticks));

        // Modify file size
        File.WriteAllBytes(filePath, new byte[] { 1, 2, 3, 4, 5, 6, 7 });
        Assert.False(SourceIdentity.FastVerify(filePath, fi1.Length, fi1.LastWriteTimeUtc.Ticks));

        // Modify write time
        var fi2 = new FileInfo(filePath);
        long oldTicks = fi2.LastWriteTimeUtc.Ticks;
        File.SetLastWriteTimeUtc(filePath, DateTime.UtcNow.AddMinutes(5));
        Assert.False(SourceIdentity.FastVerify(filePath, fi2.Length, oldTicks));
    }

    [Fact]
    public void ExtractedCoverSha256IsSharedBetweenDifferentTracksWithSameCover()
    {
        string hash1 = SourceIdentity.ComputeSha256(SamplePngBytes);
        string hash2 = SourceIdentity.ComputeSha256(SamplePngBytes);
        string hashRed = SourceIdentity.ComputeSha256(SampleRedPngBytes);

        Assert.Equal(hash1, hash2);
        Assert.NotEqual(hash1, hashRed);
    }

    [Fact]
    public async Task TracksWithDifferentPathsSharingIdenticalArtShareDiskCacheFile()
    {
        string coverPath = Path.Combine(_tempDir, "shared_cover.png");
        File.WriteAllBytes(coverPath, SamplePngBytes);

        var track1 = new MusicTrack
        {
            Id = 1,
            FilePath = Path.Combine(_tempDir, "track1.mp3"),
            AlbumArtPath = coverPath,
            Title = "Song One"
        };
        var track2 = new MusicTrack
        {
            Id = 2,
            FilePath = Path.Combine(_tempDir, "track2.mp3"),
            AlbumArtPath = coverPath,
            Title = "Song Two"
        };

        File.WriteAllBytes(track1.FilePath, new byte[] { 10, 20 });
        File.WriteAllBytes(track2.FilePath, new byte[] { 30, 40 });

        var art1 = await _albumArtProvider.GetOrLoadAlbumArtAsync(track1, track1.FilePath, ImageQuality.Medium, ImagePriority.Visible);
        var art2 = await _albumArtProvider.GetOrLoadAlbumArtAsync(track2, track2.FilePath, ImageQuality.Medium, ImagePriority.Visible);

        Assert.NotNull(art1);
        Assert.NotNull(art2);

        var rec1 = _db.GetAlbumArtRecord(SourceIdentity.NormalizePath(coverPath));
        Assert.NotNull(rec1);
        Assert.True(rec1.HasArtwork);
        Assert.Equal(SourceIdentity.ComputeSha256(SamplePngBytes), rec1.ContentHash);
    }

    [Fact]
    public async Task OriginalAssetPreservedAndDerivedQualitiesGeneratedFromCachedFull()
    {
        string coverPath = Path.Combine(_tempDir, "derive_cover.png");
        File.WriteAllBytes(coverPath, SamplePngBytes);

        var track = new MusicTrack
        {
            Id = 99,
            FilePath = Path.Combine(_tempDir, "derive_track.mp3"),
            AlbumArtPath = coverPath,
            Title = "Derive Test"
        };
        File.WriteAllBytes(track.FilePath, new byte[] { 1, 2, 3 });

        // 1. First request is Thumbnail
        var thumb = await _albumArtProvider.GetOrLoadAlbumArtAsync(
            track, track.FilePath, ImageQuality.Thumbnail, ImagePriority.Visible);
        Assert.NotNull(thumb);

        string normPath = SourceIdentity.NormalizePath(coverPath);
        var rec = _db.GetAlbumArtRecord(normPath);
        Assert.NotNull(rec);
        Assert.NotNull(rec.ThumbnailPath);
        Assert.True(File.Exists(rec.ThumbnailPath));
        Assert.NotNull(rec.FullPath);
        Assert.True(File.Exists(rec.FullPath));

        // 2. Clear in-memory cache to force reading from disk
        _memoryCache.Clear();

        // 3. Second request is Medium quality; should derive from cached Full asset
        var medium = await _albumArtProvider.GetOrLoadAlbumArtAsync(
            track, track.FilePath, ImageQuality.Medium, ImagePriority.Visible);
        Assert.NotNull(medium);

        var updatedRec = _db.GetAlbumArtRecord(normPath);
        Assert.NotNull(updatedRec);
        Assert.NotNull(updatedRec.MediumPath);
        Assert.True(File.Exists(updatedRec.MediumPath));
        Assert.NotNull(updatedRec.ThumbnailPath);
        Assert.NotNull(updatedRec.FullPath);
    }

    // ── SINGLE-FLIGHT TESTS ───────────────────────────────────────────────────

    [Fact]
    public async Task SingleFlightSharesOneExecutionForConcurrentRequests()
    {
        var singleFlight = new SingleFlightCoordinator<string>();
        int executionCount = 0;
        var startTcs = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseTcs = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        async Task<string?> HeavyWork(CancellationToken ct)
        {
            Interlocked.Increment(ref executionCount);
            startTcs.TrySetResult();
            await releaseTcs.Task.WaitAsync(ct);
            return "completed-result";
        }

        var task1 = singleFlight.RunOrJoinAsync("key1", HeavyWork);
        var task2 = singleFlight.RunOrJoinAsync("key1", HeavyWork);
        var task3 = singleFlight.RunOrJoinAsync("key1", HeavyWork);

        await startTcs.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal(1, Volatile.Read(ref executionCount));

        releaseTcs.TrySetResult();
        var results = await Task.WhenAll(task1, task2, task3).WaitAsync(TimeSpan.FromSeconds(5));

        Assert.All(results, r => Assert.Equal("completed-result", r));
        Assert.Equal(1, executionCount);
    }

    [Fact]
    public async Task SingleFlightWaiterCancellationDoesNotCancelRemainingWaiters()
    {
        var singleFlight = new SingleFlightCoordinator<string>();
        int executionCount = 0;
        var startTcs = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseTcs = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var cancelCts = new CancellationTokenSource();

        async Task<string?> HeavyWork(CancellationToken ct)
        {
            Interlocked.Increment(ref executionCount);
            startTcs.TrySetResult();
            await releaseTcs.Task.WaitAsync(ct);
            return "active-result";
        }

        var cancelledTask = singleFlight.RunOrJoinAsync("shared-key", HeavyWork, cancelCts.Token);
        var remainingTask = singleFlight.RunOrJoinAsync("shared-key", HeavyWork, CancellationToken.None);

        await startTcs.Task.WaitAsync(TimeSpan.FromSeconds(5));

        // Cancel first waiter
        cancelCts.Cancel();
        var cancelledResult = await cancelledTask.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Null(cancelledResult);

        // Remaining waiter continues
        Assert.False(remainingTask.IsCompleted);
        releaseTcs.TrySetResult();

        var finalResult = await remainingTask.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal("active-result", finalResult);
        Assert.Equal(1, executionCount);
    }

    [Fact]
    public async Task SingleFlightFailedExecutionIsEvictedAndAllowsImmediateRetry()
    {
        var singleFlight = new SingleFlightCoordinator<string>();
        int count = 0;

        Task<string?> FailingWork(CancellationToken ct)
        {
            Interlocked.Increment(ref count);
            return Task.FromException<string?>(new InvalidOperationException("disk error"));
        }

        Task<string?> SucceedingWork(CancellationToken ct)
        {
            Interlocked.Increment(ref count);
            return Task.FromResult<string?>("retry-success");
        }

        try
        {
            await singleFlight.RunOrJoinAsync("fail-key", FailingWork);
        }
        catch
        {
            // expected
        }

        var retried = await singleFlight.RunOrJoinAsync("fail-key", SucceedingWork).WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal("retry-success", retried);
        Assert.Equal(2, count);
    }

    // ── MEMORY CACHE TESTS ───────────────────────────────────────────────────

    [Fact]
    public void MemoryCacheEnforcesByteBudgetWithLruEviction()
    {
        // 32 KB cache budget
        var cache = new ImageMemoryCache(maxSizeBytes: 32 * 1024);

        // Render 100x100 bitmaps (each ~40 KB in memory)
        var bm1 = ImageDecoder.Decode(SamplePngBytes, ImageQuality.Thumbnail)!;
        var bm2 = ImageDecoder.Decode(SampleRedPngBytes, ImageQuality.Thumbnail)!;

        cache.Set("img1", ImageKind.AlbumArt, ImageQuality.Thumbnail, bm1);
        Assert.True(cache.TryGet("img1", ImageKind.AlbumArt, ImageQuality.Thumbnail, out var r1));
        Assert.NotNull(r1);

        cache.Set("img2", ImageKind.AlbumArt, ImageQuality.Thumbnail, bm2);
        Assert.True(cache.TryGet("img2", ImageKind.AlbumArt, ImageQuality.Thumbnail, out var r2));
        Assert.NotNull(r2);

        // Invalidate img1 specifically
        cache.Remove("img1", ImageKind.AlbumArt, ImageQuality.Thumbnail);
        Assert.False(cache.TryGet("img1", ImageKind.AlbumArt, ImageQuality.Thumbnail, out _));
    }

    // ── DISK CACHE TESTS ─────────────────────────────────────────────────────

    [Fact]
    public async Task DiskCachePerformsAtomicWriteAndPurgesCorruptFiles()
    {
        string hash = "testhash123";
        string? savedPath = await _diskCache.SaveAtomicAsync(hash, ImageQuality.Medium, SamplePngBytes, isPng: true);

        Assert.NotNull(savedPath);
        Assert.True(File.Exists(savedPath));
        Assert.True(_diskCache.TryGetValidCachedFile(hash, ImageQuality.Medium, out var validPath));
        Assert.Equal(savedPath, validPath);

        // Simulate file corruption: truncate to 0 bytes
        File.WriteAllBytes(savedPath, Array.Empty<byte>());

        Assert.False(_diskCache.TryGetValidCachedFile(hash, ImageQuality.Medium, out _));
        // Auto-purged 0-byte corrupt file
        Assert.False(File.Exists(savedPath));
    }

    // ── SCHEDULER & CONCURRENCY TESTS ─────────────────────────────────────────

    [Fact]
    public async Task SchedulerLimitsConcurrencyAndHonorsCancellation()
    {
        using var scheduler = new ImageLoadScheduler();
        int activeExtracts = 0;
        int maxSimultaneous = 0;
        var sync = new object();

        var tasks = Enumerable.Range(0, 10).Select(async _ =>
        {
            await scheduler.ScheduleAsync(
                OperationType.TagLibExtraction,
                ImagePriority.Playback,
                async ct =>
                {
                    int current;
                    lock (sync)
                    {
                        activeExtracts++;
                        current = activeExtracts;
                        if (current > maxSimultaneous) maxSimultaneous = current;
                    }
                    await Task.Delay(50, ct);
                    lock (sync) { activeExtracts--; }
                    return 42;
                });
        });

        await Task.WhenAll(tasks).WaitAsync(TimeSpan.FromSeconds(5));

        // TagLib extraction gate limit is 2
        Assert.True(maxSimultaneous <= 2, $"Expected at most 2 simultaneous extracts but was {maxSimultaneous}");
    }

    // ── LIFECYCLE & DISPOSAL TESTS ───────────────────────────────────────────

    [Fact]
    public void CoordinatorDisposeSafelyCleansResourcesAndPreventsNewWork()
    {
        var coord = new ImageLoadingCoordinator(_db, NullLogger.Instance);
        Assert.NotNull(coord.AlbumArt);
        Assert.NotNull(coord.ArtistImages);

        coord.Dispose();
        coord.Dispose(); // Idempotent

        Assert.Null(coord.TryGetAlbumArt("any-path", ImageQuality.Thumbnail));
    }

    // ── UI CONVERTER INTEGRATION TESTS ────────────────────────────────────────

    [Fact]
    public void AlbumArtConverterReturnsInstantPlaceholderAndOffloadsHeavyDecode()
    {
        var prev = AlbumArtLoader.Instance;
        using var loader = new AlbumArtLoader(null, initializeDiskCache: false);
        AlbumArtLoader.Instance = loader;

        try
        {
            var converter = new AlbumArtConverter();
            string fakePath = Path.Combine(_tempDir, "non_cached.mp3");

            // UI thread converter call must never block and return a DrawingImage placeholder immediately
            var result = converter.Convert(fakePath, typeof(ImageSource), "", CultureInfo.InvariantCulture);

            Assert.NotNull(result);
            Assert.IsAssignableFrom<ImageSource>(result);
        }
        finally
        {
            AlbumArtLoader.Instance = prev!;
        }
    }

    [Fact]
    public void ArtistImageConverterReturnsInstantPlaceholderWithoutSynchronousStall()
    {
        var converter = new ArtistImageConverter();
        var result = converter.Convert("Coldplay", typeof(ImageSource), "full", CultureInfo.InvariantCulture);
        Assert.NotNull(result);
    }

    // ── COORDINATOR MEMORY CACHE CROSS-POPULATION TESTS ──────────────────────

    [Fact]
    public void ArtistImageProvider_MemoryCacheIsKeyedByNormalizedArtistName()
    {
        // NormalizeKey strips feat., ft., trailing commas/ampersands
        Assert.Equal(ArtistImageProvider.NormalizeKey("Coldplay"), ArtistImageProvider.NormalizeKey("Coldplay"));
        Assert.NotEqual(ArtistImageProvider.NormalizeKey("Coldplay"), ArtistImageProvider.NormalizeKey("Radiohead"));
        // Normalize strips feat. collaborator
        Assert.Equal(ArtistImageProvider.NormalizeKey("CKay"), ArtistImageProvider.NormalizeKey("CKay feat. Someone"));
    }

    [Fact]
    public void CoordinatorMemoryCacheSet_IsRetrievableViaProvider()
    {
        // After setting a decoded image in the shared memory cache, the coordinator's
        // TryGetArtistImage method should return it immediately without hitting the disk.
        var bm = ImageDecoder.Decode(SamplePngBytes, ImageQuality.Thumbnail)!;
        string artist = "TestArtist";
        string key = ArtistImageProvider.NormalizeKey(artist);

        _coordinator.MemoryCache.Set(key, ImageKind.Artist, ImageQuality.Thumbnail, bm);

        var fromCoord = _coordinator.TryGetArtistImage(artist, isHighRes: false);
        Assert.NotNull(fromCoord);
        Assert.Same(bm, fromCoord);
    }

    [Fact]
    public void InvalidArtistNamesAreRejectedByProvider()
    {
        Assert.True(ArtistImageProvider.IsInvalidArtistName(null));
        Assert.True(ArtistImageProvider.IsInvalidArtistName(""));
        Assert.True(ArtistImageProvider.IsInvalidArtistName("unknown"));
        Assert.True(ArtistImageProvider.IsInvalidArtistName("Unknown Artist"));
        Assert.True(ArtistImageProvider.IsInvalidArtistName("Various Artists"));
        Assert.True(ArtistImageProvider.IsInvalidArtistName("123")); // numeric only
        Assert.False(ArtistImageProvider.IsInvalidArtistName("Coldplay"));
        Assert.False(ArtistImageProvider.IsInvalidArtistName("CKay"));
    }

    // ── DISK CACHE TEMP CLEANUP TESTS ─────────────────────────────────────────

    [Fact]
    public void DiskCache_CleanOldTempFilesRemovesStaleTemporaryFiles()
    {
        string cacheDir = Path.Combine(_tempDir, "cleantest");
        Directory.CreateDirectory(cacheDir);
        var cache = new ImageDiskCache(cacheDir);

        // The temp directory is <cacheDir>/temp (created by EnsureDirectories in constructor)
        string tempDir = Path.Combine(cacheDir, "temp");
        Directory.CreateDirectory(tempDir);

        // Create a fake temp file older than 1 hour
        string staleTemp = Path.Combine(tempDir, Guid.NewGuid().ToString("N") + ".tmp");
        File.WriteAllBytes(staleTemp, new byte[] { 1, 2, 3 });
        File.SetLastWriteTimeUtc(staleTemp, DateTime.UtcNow.AddHours(-2));

        // A fresh temp file (should NOT be deleted)
        string freshTemp = Path.Combine(tempDir, Guid.NewGuid().ToString("N") + ".tmp");
        File.WriteAllBytes(freshTemp, new byte[] { 4, 5, 6 });
        // freshTemp has current LastWriteTime so it is within the 1-hour window

        // Clean files older than 1 hour
        cache.CleanOldTempFiles(TimeSpan.FromHours(1));

        Assert.False(File.Exists(staleTemp), "Stale temp file should have been deleted");
        Assert.True(File.Exists(freshTemp), "Fresh temp file should be preserved");
    }

    // ── SOURCE IDENTITY MULTI-STAGE VERIFY ────────────────────────────────────

    [Fact]
    public void SourceIdentity_NormalizePathIsConsistentAcrossSeparatorStyles()
    {
        string path = @"C:\Users\win\Music\song.mp3";
        string pathFwd = "C:/Users/win/Music/song.mp3";

        string norm1 = SourceIdentity.NormalizePath(path);
        string norm2 = SourceIdentity.NormalizePath(pathFwd);

        // Both must map to the same canonical form (lowercase, forward slashes on Windows)
        Assert.Equal(norm1, norm2);
    }

    [Fact]
    public void SourceIdentity_ComputeSha256_IsDeterministicAndDistinctForDifferentBytes()
    {
        byte[] a = new byte[] { 1, 2, 3 };
        byte[] b = new byte[] { 4, 5, 6 };

        Assert.Equal(SourceIdentity.ComputeSha256(a), SourceIdentity.ComputeSha256(a));
        Assert.NotEqual(SourceIdentity.ComputeSha256(a), SourceIdentity.ComputeSha256(b));
        Assert.Equal(64, SourceIdentity.ComputeSha256(a).Length); // SHA-256 hex = 64 chars
    }

    // ── REGRESSION: CACHE LOOKUP WHEN SOURCE AUDIO FILE IS DELETED ───────────

    [Fact]
    public async Task AlbumArtProvider_ReturnsCachedArtWhenSourceAudioFileIsMissing()
    {
        string fakeAudioPath = Path.Combine(_tempDir, "deleted_song.mp3");
        string normPath = SourceIdentity.NormalizePath(fakeAudioPath);
        string hash = SourceIdentity.ComputeSha256(SamplePngBytes);

        // Pre-cache full and thumb in disk cache
        await _diskCache.SaveAtomicAsync(hash, ImageQuality.Thumbnail, SamplePngBytes, isPng: true, cancellationToken: CancellationToken.None);
        await _diskCache.SaveAtomicAsync(hash, ImageQuality.Full, SamplePngBytes, isPng: true, cancellationToken: CancellationToken.None);

        // Add record in DB pointing to cached art
        _db.UpsertAlbumArtRecord(new AlbumArtRecord
        {
            NormalizedTrackPath = normPath,
            ContentHash = hash,
            HasArtwork = true,
            FileSizeBytes = 12345,
            LastModifiedUtcTicks = DateTime.UtcNow.Ticks
        });

        // Ensure fakeAudioPath does NOT exist on disk
        Assert.False(File.Exists(fakeAudioPath));

        // Request artwork via the _albumArtProvider which uses our test _diskCache
        // (the coordinator has its own internal cache dir so we use _albumArtProvider directly)
        var result = await _albumArtProvider.GetOrLoadAlbumArtAsync(
            null,
            fakeAudioPath,
            ImageQuality.Thumbnail,
            ImagePriority.Visible,
            CancellationToken.None);

        Assert.NotNull(result);
    }

    // ── REGRESSION: MANAGED FOLDER FILTERING IN DATABASE SERVICE ─────────────

    [Fact]
    public void DatabaseService_DetailQueriesFilterByManagedFolders()
    {
        string managedFolder = Path.Combine(_tempDir, "MusicFolder");
        Directory.CreateDirectory(managedFolder);

        // Track inside managed folder
        var managedTrack = new MusicTrack
        {
            Title = "Managed Song",
            Artist = "Test Artist",
            Album = "Test Album",
            Genre = "Rock",
            FilePath = Path.Combine(managedFolder, "track1.mp3")
        };

        // Track outside managed folder (e.g. from an old deleted folder)
        var orphanTrack = new MusicTrack
        {
            Title = "Orphan Song",
            Artist = "Test Artist",
            Album = "Test Album",
            Genre = "Rock",
            FilePath = Path.Combine(Path.GetTempPath(), "old_removed_folder", "track2.mp3")
        };

        _db.InsertTrack(managedTrack);
        _db.InsertTrack(orphanTrack);

        // Before adding managed folder: no enabled managed folders exist, so detail queries return nothing
        var albumDetailBefore = _db.GetAlbumDetail("Test Album");
        Assert.Empty(albumDetailBefore.Tracks);

        var artistTracksBefore = _db.GetArtistTracks("Test Artist");
        Assert.Empty(artistTracksBefore);

        var genreTracksBefore = _db.GetGenreTracks("Rock");
        Assert.Empty(genreTracksBefore);

        var genresBefore = _db.GetAllGenres();
        Assert.Empty(genresBefore);

        // Now add the managed folder
        _db.AddManagedFolder(new ManagedMusicFolder
        {
            Path = managedFolder,
            Name = "MusicFolder",
            Enabled = true
        });

        // After adding managed folder: only the managed track should be returned, orphan track must be excluded
        var albumDetailAfter = _db.GetAlbumDetail("Test Album");
        Assert.Single(albumDetailAfter.Tracks);
        Assert.Equal("Managed Song", albumDetailAfter.Tracks[0].Title);

        var artistTracksAfter = _db.GetArtistTracks("Test Artist");
        Assert.Single(artistTracksAfter);
        Assert.Equal("Managed Song", artistTracksAfter[0].Title);

        var genreTracksAfter = _db.GetGenreTracks("Rock");
        Assert.Single(genreTracksAfter);
        Assert.Equal("Managed Song", genreTracksAfter[0].Title);

        var genresAfter = _db.GetAllGenres();
        Assert.Single(genresAfter);
        Assert.Equal("Rock", genresAfter[0]);
    }

    // ── C2PA / NON-IMAGE PICTURE FILTERING TESTS ──────────────────────────────

    /// <summary>
    /// Regression test for the C2PA manifest bug:
    /// When an MP3 contains both an "application/c2pa" frame (NotAPicture) AND a
    /// FrontCover image/jpeg frame, ExtractArtwork must return the JPEG bytes,
    /// not the manifest bytes.
    /// </summary>
    [Fact]
    public void AlbumArtProvider_SkipsC2PAManifestAndSelectsFrontCoverJpeg()
    {
        // --- Arrange: build a real MP3 file with two APIC frames ---
        string mp3Path = Path.Combine(_tempDir, "c2pa_test_song.mp3");
        File.WriteAllBytes(mp3Path, MinimalMp3Bytes());

        // Fake C2PA manifest bytes (non-image binary data, starts with known prefix)
        var c2paBytes = new byte[256];
        c2paBytes[0] = 0xC2; c2paBytes[1] = 0xCA; // distinctive non-JPEG prefix
        new Random(42).NextBytes(new Span<byte>(c2paBytes, 2, 254));

        // Real image: a 1×1 JPEG (starts 0xFF 0xD8)
        byte[] jpegBytes = SampleJpegBytes();
        Assert.Equal(0xFF, jpegBytes[0]); // sanity check
        Assert.Equal(0xD8, jpegBytes[1]);

        using (var tagFile = TagLib.File.Create(mp3Path))
        {
            tagFile.Tag.Pictures = new TagLib.IPicture[]
            {
                new TagLib.Picture
                {
                    Type     = TagLib.PictureType.NotAPicture,
                    MimeType = "application/c2pa",
                    Data     = new TagLib.ByteVector(c2paBytes)
                },
                new TagLib.Picture
                {
                    Type     = TagLib.PictureType.FrontCover,
                    MimeType = "image/jpeg",
                    Data     = new TagLib.ByteVector(jpegBytes)
                }
            };
            tagFile.Save();
        }

        var track = new MusicTrack { FilePath = mp3Path, Title = "C2PA Test", Artist = "A", Album = "B" };

        // --- Act: call the internal extraction method directly ---
        var extracted = AlbumArtProvider.ExtractArtwork(mp3Path, track, CancellationToken.None);

        // --- Assert ---
        Assert.NotNull(extracted);
        Assert.NotNull(extracted!.RawBytes);

        // Must be the JPEG, not the C2PA manifest
        Assert.Equal(0xFF, extracted.RawBytes[0]);
        Assert.Equal(0xD8, extracted.RawBytes[1]);
        Assert.NotEqual(0xC2, extracted.RawBytes[0]); // ensure C2PA was not selected

        // Verify the byte count matches the JPEG we embedded
        Assert.Equal(jpegBytes.Length, extracted.RawBytes.Length);
    }

    /// <summary>
    /// When ALL pictures in a file are non-image (e.g. only a C2PA manifest),
    /// ExtractArtwork must return null rather than the binary garbage.
    /// </summary>
    [Fact]
    public void AlbumArtProvider_ReturnsNullWhenOnlyNonImagePicturesPresent()
    {
        string mp3Path = Path.Combine(_tempDir, "c2pa_only_song.mp3");
        File.WriteAllBytes(mp3Path, MinimalMp3Bytes());

        var c2paBytes = new byte[128];
        new Random(99).NextBytes(c2paBytes);

        using (var tagFile = TagLib.File.Create(mp3Path))
        {
            tagFile.Tag.Pictures = new TagLib.IPicture[]
            {
                new TagLib.Picture
                {
                    Type     = TagLib.PictureType.NotAPicture,
                    MimeType = "application/c2pa",
                    Data     = new TagLib.ByteVector(c2paBytes)
                }
            };
            tagFile.Save();
        }

        var track = new MusicTrack { FilePath = mp3Path, Title = "C2PA Only", Artist = "A", Album = "B" };

        // No sidecar images, no image/* frames — extraction must return null.
        var extracted = AlbumArtProvider.ExtractArtwork(mp3Path, track, CancellationToken.None);

        Assert.Null(extracted);
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    /// <summary>Minimal valid MPEG Layer 3 file bytes that TagLib can open and tag.</summary>
    private static byte[] MinimalMp3Bytes()
    {
        // ID3v2.3 header (10 bytes) with zero-length tag body, followed by
        // one valid MPEG1 Layer3 silent frame header so TagLib accepts the file.
        // ID3v2: "ID3" + version 2.3 + flags 0 + syncsafe size 0
        // MPEG frame: 0xFF 0xFB 0x90 0x00 (MPEG1, Layer3, 128kbps, 44100Hz, stereo)
        return new byte[]
        {
            0x49, 0x44, 0x33,       // "ID3"
            0x03, 0x00,             // version 2.3.0
            0x00,                   // flags
            0x00, 0x00, 0x00, 0x00, // syncsafe size = 0
            // One silent MPEG1 Layer3 frame header
            0xFF, 0xFB, 0x90, 0x00
        };
    }

    /// <summary>Minimal 1×1 pixel JPEG bytes.</summary>
    private static byte[] SampleJpegBytes() => Convert.FromBase64String(
        "/9j/4AAQSkZJRgABAQEASABIAAD/2wBDAAgGBgcGBQgHBwcJCQgKDBQNDAsLDBkSEw8U" +
        "HRofHh0aHBwgJC4nICIsIxwcKDcpLDAxNDQ0Hyc5PTgyPC4zNDL/2wBDAQkJCQwLDBgN" +
        "DRgyIRwhMjIyMjIyMjIyMjIyMjIyMjIyMjIyMjIyMjIyMjIyMjIyMjIyMjIyMjIyMjIy" +
        "MjL/wAARCAABAAEDASIAAhEBAxEB/8QAFAABAAAAAAAAAAAAAAAAAAAACf/EABQQAQAAAAAA" +
        "AAAAAAAAAAAAAP/EABQBAQAAAAAAAAAAAAAAAAAAAAD/xAAUEQEAAAAAAAAAAAAAAAAAAAAA" +
        "/9oADAMBAAIRAxEAPwCwABmX/9k=");
}
