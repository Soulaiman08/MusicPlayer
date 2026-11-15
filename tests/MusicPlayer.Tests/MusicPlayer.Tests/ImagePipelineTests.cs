using System.Collections.Concurrent;
using System.Reflection;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Microsoft.Extensions.Logging.Abstractions;
using MusicPlayer.Database;
using MusicPlayer.ImageLoading;
using MusicPlayer.Library;
using MusicPlayer.Models;

namespace MusicPlayer.Tests;

/// <summary>
/// Regression tests for the M2 image-pipeline work.
///
/// The scenarios mirror what the UI actually does: several consumers ask for
/// the same artwork at the same time, different consumers ask for different
/// renditions of the same artwork, a consumer walks away while another one
/// still waits, a decode fails and is retried, and the caches are put under
/// pressure.
/// </summary>
public sealed class ImagePipelineTests : IDisposable
{
    private readonly string _tempDir;
    private readonly DatabaseService _db;
    private readonly ImageDiskCache _diskCache;
    private readonly ImageMemoryCache _memoryCache;
    private readonly ImageLoadScheduler _scheduler;
    private readonly AlbumArtProvider _provider;

    public ImagePipelineTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "mp_imagepipe_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempDir);

        _db = new DatabaseService(Path.Combine(_tempDir, "test.db"), NullLogger<DatabaseService>.Instance);
        _diskCache = new ImageDiskCache(Path.Combine(_tempDir, "ArtCache"), NullLogger.Instance);
        _memoryCache = new ImageMemoryCache(maxSizeBytes: 4 * 1024 * 1024);
        _scheduler = new ImageLoadScheduler(NullLogger.Instance);
        _provider = new AlbumArtProvider(_diskCache, _memoryCache, _scheduler, _db, NullLogger.Instance);
    }

    public void Dispose()
    {
        try { _scheduler.Dispose(); } catch { }
        try { _db.Dispose(); } catch { }
        try { Directory.Delete(_tempDir, true); } catch { }
    }

    // ── helpers ────────────────────────────────────────────────────────────────

    /// <summary>Writes a real PNG cover so the decoder has something to chew on.</summary>
    private string WriteCover(string name = "cover.png", int size = 64)
    {
        string path = Path.Combine(_tempDir, name);
        var bitmap = new WriteableBitmap(size, size, 96, 96, PixelFormats.Bgra32, null);
        var pixels = new byte[size * size * 4];
        for (int i = 0; i < pixels.Length; i += 4)
        {
            pixels[i] = (byte)(i % 251);
            pixels[i + 1] = (byte)((i / 7) % 253);
            pixels[i + 2] = (byte)((i / 13) % 247);
            pixels[i + 3] = 255;
        }
        bitmap.WritePixels(new Int32Rect(0, 0, size, size), pixels, size * 4, 0);
        bitmap.Freeze();

        using var stream = new FileStream(path, FileMode.Create, FileAccess.Write);
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bitmap));
        encoder.Save(stream);
        return path;
    }

    private MusicTrack MakeTrack(string coverPath, int id = 1)
    {
        string audioPath = Path.Combine(_tempDir, $"track{id}.mp3");
        File.WriteAllBytes(audioPath, new byte[] { 1, 2, 3 });
        return new MusicTrack
        {
            Id = id,
            FilePath = audioPath,
            AlbumArtPath = coverPath,
            Title = $"Track {id}",
            Artist = "Artist",
            Album = "Album"
        };
    }

    // ── 1. several consumers, same source + size + transformation ─────────────

    [Fact]
    public async Task ConcurrentConsumersForTheSameSourceAndSizeDecodeOnce()
    {
        string cover = WriteCover();
        var track = MakeTrack(cover);

        using var scope = ImageDecodeMetrics.BeginScope();

        var results = await Task.WhenAll(Enumerable.Range(0, 32).Select(_ =>
            Task.Run(() => _provider.GetOrLoadAlbumArtAsync(
                track, cover, ImageQuality.Thumbnail, ImagePriority.Visible, CancellationToken.None))));

        Assert.All(results, r => Assert.NotNull(r));
        Assert.All(results, r => Assert.Same(results[0], r));

        // Exactly one rendition decode for the whole group.
        Assert.Equal(1, scope.Decodes);
        Assert.Equal(1, scope.Extractions);
        Assert.Equal(1, scope.DecodesBySourceRendition.Count);
        Assert.All(scope.DecodesBySourceRendition.Values, v => Assert.Equal(1, v));
    }

    // ── 2. concurrent requests with different sizes ───────────────────────────

    [Fact]
    public async Task ConcurrentRequestsWithDifferentSizesDecodeEachSizeOnce()
    {
        string cover = WriteCover();
        var track = MakeTrack(cover);

        using var scope = ImageDecodeMetrics.BeginScope();

        var thumbnail = _provider.GetOrLoadAlbumArtAsync(track, cover, ImageQuality.Thumbnail, ImagePriority.Visible, CancellationToken.None);
        var medium = _provider.GetOrLoadAlbumArtAsync(track, cover, ImageQuality.Medium, ImagePriority.Visible, CancellationToken.None);

        var results = await Task.WhenAll(thumbnail, medium);

        Assert.NotNull(results[0]);
        Assert.NotNull(results[1]);

        // Two renditions, one decode each - and never a second decode of one of them.
        Assert.Equal(2, scope.Decodes);
        Assert.Equal(2, scope.DecodesByRendition.Count);
        Assert.All(scope.DecodesByRendition.Values, v => Assert.Equal(1, v));
        Assert.All(scope.DecodesBySourceRendition.Values, v => Assert.Equal(1, v));
    }

    // ── 3. identical requests arrive before the cache store completes ─────────

    [Fact]
    public async Task IdenticalRequestsArrivingBeforeTheStoreCompletesShareOneDecode()
    {
        string cover = WriteCover();
        var track = MakeTrack(cover);

        using var scope = ImageDecodeMetrics.BeginScope();

        var gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var callers = Enumerable.Range(0, 8).Select(_ => Task.Run(async () =>
        {
            await gate.Task;
            return await _provider.GetOrLoadAlbumArtAsync(
                track, cover, ImageQuality.Thumbnail, ImagePriority.Visible, CancellationToken.None);
        })).ToArray();

        gate.SetResult();
        var results = await Task.WhenAll(callers);

        Assert.All(results, r => Assert.NotNull(r));
        Assert.Equal(1, scope.Decodes);
        Assert.All(scope.DecodesBySourceRendition.Values, v => Assert.Equal(1, v));
    }

    // ── 4. one consumer cancels, the other one keeps its result ──────────────

    [Fact]
    public async Task CancellingOneConsumerDoesNotCancelTheSharedRequest()
    {
        string cover = WriteCover();
        var track = MakeTrack(cover);

        using var scope = ImageDecodeMetrics.BeginScope();
        using var cancellation = new CancellationTokenSource();

        var cancelled = _provider.GetOrLoadAlbumArtAsync(
            track, cover, ImageQuality.Thumbnail, ImagePriority.Visible, cancellation.Token);
        var survivor = _provider.GetOrLoadAlbumArtAsync(
            track, cover, ImageQuality.Thumbnail, ImagePriority.Visible, CancellationToken.None);

        cancellation.Cancel();

        Assert.Null(await cancelled.WaitAsync(TimeSpan.FromSeconds(10)));
        var result = await survivor.WaitAsync(TimeSpan.FromSeconds(10));
        Assert.NotNull(result);

        // The cancelled consumer must not have caused a second decode.
        Assert.Equal(1, scope.Decodes);
    }

    // ── 5./6. a failing decode and the retry after it ─────────────────────────

    [Fact]
    public async Task FailedDecodeReturnsNullAndTheNextRequestSucceeds()
    {
        string cover = Path.Combine(_tempDir, "broken.png");
        File.WriteAllBytes(cover, new byte[] { 0x89, 0x50, 0x4E, 0x47, 1, 2, 3, 4, 5, 6, 7, 8 });
        var track = MakeTrack(cover);

        using var scope = ImageDecodeMetrics.BeginScope();

        var failed = await _provider.GetOrLoadAlbumArtAsync(
            track, cover, ImageQuality.Thumbnail, ImagePriority.Visible, CancellationToken.None);
        Assert.Null(failed);

        // The failure must not poison the cache: the next attempt has to retry.
        Assert.False(_memoryCache.TryGet(
            SourceIdentity.NormalizePath(cover), ImageKind.AlbumArt, ImageQuality.Thumbnail, out _));

        WriteCover("broken.png");
        var succeeded = await _provider.GetOrLoadAlbumArtAsync(
            track, cover, ImageQuality.Thumbnail, ImagePriority.Visible, CancellationToken.None);

        Assert.NotNull(succeeded);
        Assert.True(scope.Decodes >= 1);
    }

    // ── 7. cache pressure and eviction ───────────────────────────────────────

    [Fact]
    public void MemoryCacheEvictsUnderPressureAndStaysInsideItsBudget()
    {
        // ImageMemoryCache enforces a 1 MB floor on the budget, so a 256 KB
        // request still leaves 1 MB of headroom. Use images that are large
        // enough to make eviction observable at that scale.
        var cache = new ImageMemoryCache(maxSizeBytes: 1024 * 1024);
        int size = 256;
        var pixels = new byte[size * size * 4];

        for (int i = 0; i < 40; i++)
        {
            var bitmap = new WriteableBitmap(size, size, 96, 96, PixelFormats.Bgra32, null);
            bitmap.WritePixels(new Int32Rect(0, 0, size, size), pixels, size * 4, 0);
            bitmap.Freeze();
            cache.Set($"img{i}", ImageKind.AlbumArt, ImageQuality.Thumbnail, bitmap);
        }

        Assert.True(cache.Count < 40, "the cache must evict instead of growing without bound");
        // 1 MB budget, ~256 KB per image: the cache may hold a few images but
        // never all 40 of them.
        Assert.True(cache.CurrentSizeBytes <= 1024 * 1024 + 256 * 1024,
            $"cache grew past its budget: {cache.CurrentSizeBytes} bytes");

        // The most recent entry is still there.
        Assert.True(cache.TryGet("img39", ImageKind.AlbumArt, ImageQuality.Thumbnail, out _));
    }

    // ── 8. updating an existing key keeps the budget correct ─────────────────

    [Fact]
    public void UpdatingAnExistingMemoryCacheKeyDoesNotChargeTheBudgetTwice()
    {
        var cache = new ImageMemoryCache(maxSizeBytes: 8 * 1024 * 1024);
        int size = 64;
        var pixels = new byte[size * size * 4];

        var first = new WriteableBitmap(size, size, 96, 96, PixelFormats.Bgra32, null);
        first.WritePixels(new Int32Rect(0, 0, size, size), pixels, size * 4, 0);
        first.Freeze();
        cache.Set("same-key", ImageKind.AlbumArt, ImageQuality.Thumbnail, first);
        long afterFirst = cache.CurrentSizeBytes;

        var second = new WriteableBitmap(size, size, 96, 96, PixelFormats.Bgra32, null);
        second.WritePixels(new Int32Rect(0, 0, size, size), pixels, size * 4, 0);
        second.Freeze();
        cache.Set("same-key", ImageKind.AlbumArt, ImageQuality.Thumbnail, second);
        long afterSecond = cache.CurrentSizeBytes;

        Assert.Equal(1, cache.Count);
        Assert.Equal(afterFirst, afterSecond);
    }

    [Fact]
    public void AlbumArtLoaderStoreBoundedKeepsTheBudgetCorrectWhenAKeyIsUpdated()
    {
        var cache = new ConcurrentDictionary<string, ImageSource?>();
        var sizes = new ConcurrentDictionary<string, long>();
        long currentBytes = 0;

        var method = typeof(AlbumArtLoader).GetMethod(
            "StoreBounded", BindingFlags.NonPublic | BindingFlags.Static);
        Assert.NotNull(method);

        int size = 64;
        var pixels = new byte[size * size * 4];
        var bitmap = new WriteableBitmap(size, size, 96, 96, PixelFormats.Bgra32, null);
        bitmap.WritePixels(new Int32Rect(0, 0, size, size), pixels, size * 4, 0);
        bitmap.Freeze();

        object?[] args = { cache, sizes, currentBytes, 8L * 1024 * 1024, "same-key", bitmap };
        method.Invoke(null, args);
        long afterFirst = (long)args[2]!;

        method.Invoke(null, args);
        long afterSecond = (long)args[2]!;

        Assert.Equal(afterFirst, afterSecond);
        Assert.Single(cache);
    }

    // ── 9. no unbounded wait, no leaked flights ──────────────────────────────

    private static System.Collections.ICollection GetFlights(object coordinator)
    {
        var flightsField = typeof(SingleFlightCoordinator<ImageSource>)
            .GetField("_flights", BindingFlags.NonPublic | BindingFlags.Instance);
        Assert.NotNull(flightsField);
        return (System.Collections.ICollection)flightsField.GetValue(coordinator)!;
    }

    private static object GetAlbumArtSingleFlight(AlbumArtProvider provider)
    {
        var providerFlights = typeof(AlbumArtProvider)
            .GetField("_singleFlight", BindingFlags.NonPublic | BindingFlags.Instance);
        Assert.NotNull(providerFlights);
        return providerFlights.GetValue(provider)!;
    }

    private static async Task WaitForEmptyAsync(System.Collections.ICollection flights, int timeoutMs = 5000)
    {
        int waited = 0;
        while (flights.Count > 0 && waited < timeoutMs)
        {
            await Task.Delay(25);
            waited += 25;
        }
    }

    [Fact]
    public async Task CompletedRequestsLeaveNoFlightBehind()
    {
        string cover = WriteCover();
        var track = MakeTrack(cover);

        await _provider.GetOrLoadAlbumArtAsync(track, cover, ImageQuality.Thumbnail, ImagePriority.Visible, CancellationToken.None);

        var flights = GetFlights(GetAlbumArtSingleFlight(_provider));
        await WaitForEmptyAsync(flights);
        Assert.Empty(flights);
    }

    [Fact]
    public async Task ACancelledRequestDoesNotLeaveAStuckFlightBehind()
    {
        string cover = WriteCover();
        var track = MakeTrack(cover);

        using var cancellation = new CancellationTokenSource();
        var request = _provider.GetOrLoadAlbumArtAsync(
            track, cover, ImageQuality.Thumbnail, ImagePriority.Visible, cancellation.Token);
        cancellation.Cancel();
        await request.WaitAsync(TimeSpan.FromSeconds(10));

        var flights = GetFlights(GetAlbumArtSingleFlight(_provider));
        await WaitForEmptyAsync(flights);
        Assert.Empty(flights);
    }

    // ── 10. the derived-asset store must not decode or re-read the source ────

    [Fact]
    public void StoringADerivedRenditionDoesNotReadHashOrDecodeTheOriginal()
    {
        string cover = WriteCover();
        var track = MakeTrack(cover);
        var service = new AlbumArtAssetService();

        var thumbnail = new DrawingImage(new DrawingGroup());

        using (var scope = ImageDecodeMetrics.BeginScope())
        {
            Assert.True(service.StoreDerivedForTrack(track, AlbumArtQuality.Thumbnail, thumbnail));

            Assert.Equal(0, scope.Decodes);
            Assert.Equal(0, scope.MetadataProbes);
            Assert.Equal(0, scope.Extractions);
            Assert.Equal(0, scope.AssetResolves);
            Assert.Equal(0, scope.AssetSourceReads);
        }

        // The key-only lookup finds the very same entry.
        Assert.True(service.TryGetDerivedForTrack(track, AlbumArtQuality.Thumbnail, out var viaTrack));
        Assert.Same(thumbnail, viaTrack);

        // ... and it is the same key the full resolution would produce.
        var asset = service.Resolve(track);
        Assert.NotNull(asset);
        Assert.True(service.TryGetDerived(asset, AlbumArtQuality.Thumbnail, out var viaAsset));
        Assert.Same(thumbnail, viaAsset);
    }

    [Fact]
    public void TheDerivedCacheIsBoundedByBytesSoItCannotPinUnlimitedBitmaps()
    {
        var cache = new AlbumArtDerivedAssetCache();

        int size = 256;
        var pixels = new byte[size * size * 4];
        for (int i = 0; i < 600; i++)
        {
            var bitmap = new WriteableBitmap(size, size, 96, 96, PixelFormats.Bgra32, null);
            bitmap.WritePixels(new Int32Rect(0, 0, size, size), pixels, size * 4, 0);
            bitmap.Freeze();
            cache.Store($"source{i}", AlbumArtQuality.Thumbnail, bitmap);
        }

        // 600 x 256x256x4 bytes would be ~150 MB without a byte budget.
        int stored = 0;
        for (int i = 0; i < 600; i++)
        {
            if (cache.TryGet($"source{i}", AlbumArtQuality.Thumbnail, out _)) stored++;
        }

        Assert.True(stored < 600, "the derived cache must evict instead of pinning every bitmap");
        Assert.True(stored * (size * size * 4) <= 64L * 1024 * 1024 + 4L * 1024 * 1024,
            $"the derived cache pins {stored * (size * size * 4) / (1024 * 1024)} MB");
    }

    // ── 11. the single flight must execute its factory exactly once ──────────

    [Fact]
    public async Task SingleFlightExecutesTheFactoryExactlyOnceUnderContention()
    {
        const int rounds = 60;
        const int callers = 48;

        for (int round = 0; round < rounds; round++)
        {
            var singleFlight = new SingleFlightCoordinator<string>();
            int executions = 0;
            var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var start = new ManualResetEventSlim(false);

            async Task<string?> Work(CancellationToken ct)
            {
                Interlocked.Increment(ref executions);
                await release.Task.WaitAsync(ct);
                return "done";
            }

            var tasks = new Task<string?>[callers];
            for (int i = 0; i < callers; i++)
            {
                tasks[i] = Task.Run(async () =>
                {
                    start.Wait();
                    return await singleFlight.RunOrJoinAsync("shared-key", Work);
                });
            }

            start.Set();
            // Give every caller the chance to reach the flight before it finishes.
            await Task.Delay(15);
            release.SetResult();

            var results = await Task.WhenAll(tasks).WaitAsync(TimeSpan.FromSeconds(30));
            Assert.All(results, r => Assert.Equal("done", r));
            Assert.Equal(1, Volatile.Read(ref executions));
        }
    }

    [Fact]
    public async Task SingleFlightWaiterCancellationKeepsTheOtherWaitersAlive()
    {
        var singleFlight = new SingleFlightCoordinator<string>();
        int executions = 0;
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var cancellation = new CancellationTokenSource();

        async Task<string?> Work(CancellationToken ct)
        {
            Interlocked.Increment(ref executions);
            await release.Task.WaitAsync(ct);
            return "shared";
        }

        var cancelled = singleFlight.RunOrJoinAsync("key", Work, cancellation.Token);
        var alive = singleFlight.RunOrJoinAsync("key", Work);

        cancellation.Cancel();
        Assert.Null(await cancelled.WaitAsync(TimeSpan.FromSeconds(10)));

        release.SetResult();
        Assert.Equal("shared", await alive.WaitAsync(TimeSpan.FromSeconds(10)));
        Assert.Equal(1, Volatile.Read(ref executions));
    }
}
