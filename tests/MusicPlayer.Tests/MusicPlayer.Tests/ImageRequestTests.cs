using System.Collections.Concurrent;
using System.Reflection;
using System.Windows.Media;
using System.Globalization;
using MusicPlayer.Converters;
using Microsoft.Extensions.Logging.Abstractions;
using MusicPlayer.Library;
using MusicPlayer.Models;
using MusicPlayer.Services;

namespace MusicPlayer.Tests;

public class ImageRequestTests
{
    [Fact]
    public async Task AlbumArtRequestsForSamePathShareOneDecodeAndResult()
    {
        using var loader = new AlbumArtLoader(null, initializeDiskCache: false);
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var expected = new DrawingImage(new DrawingGroup());
        int decodeCount = 0;

        async Task<ImageSource?> DecodeAsync()
        {
            Interlocked.Increment(ref decodeCount);
            started.TrySetResult();
            await release.Task;
            return expected;
        }

        var requests = await Task.WhenAll(Enumerable.Range(0, 32).Select(_ =>
            Task.Run(() => new { Shared = loader.EnqueueFullArtParse("same-track", DecodeAsync) })));

        await started.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal(1, Volatile.Read(ref decodeCount));
        Assert.All(requests, request => Assert.Same(requests[0].Shared, request.Shared));

        release.TrySetResult();
        var results = await Task.WhenAll(requests.Select(request => request.Shared))
            .WaitAsync(TimeSpan.FromSeconds(5));
        Assert.All(results, result => Assert.Same(expected, result));
        Assert.Equal(1, decodeCount);
    }

    [Fact]
    public async Task FailedAlbumArtDecodeIsRemovedAndCanBeRetried()
    {
        using var loader = new AlbumArtLoader(null, initializeDiskCache: false);
        int decodeCount = 0;

        ImageSource? failed = await loader.EnqueueFullArtParse(
            "retry-track",
            () =>
            {
                Interlocked.Increment(ref decodeCount);
                return Task.FromException<ImageSource?>(new InvalidOperationException("decode failed"));
            });

        var expected = new DrawingImage(new DrawingGroup());
        ImageSource? retried = await loader.EnqueueFullArtParse(
            "retry-track",
            () =>
            {
                Interlocked.Increment(ref decodeCount);
                return Task.FromResult<ImageSource?>(expected);
            }).WaitAsync(TimeSpan.FromSeconds(5));

        Assert.Null(failed);
        Assert.Same(expected, retried);
        Assert.Equal(2, decodeCount);
    }

    [Fact]
    public void AlbumArtSourceResolutionFallsBackToTrackFileWhenAlbumArtPathIsInvalid()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), "AlbumArtFallback_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);
        var trackFile = Path.Combine(tempDir, "track.mp3");
        File.WriteAllText(trackFile, "track");

        var track = new MusicTrack
        {
            FilePath = trackFile,
            AlbumArtPath = Path.Combine(tempDir, "missing-cover.png")
        };

        var method = typeof(AlbumArtLoader).GetMethod(
            "ResolveTrackArtPath",
            BindingFlags.NonPublic | BindingFlags.Static);

        Assert.NotNull(method);
        Assert.Equal(trackFile, method.Invoke(null, new object?[] { track }));
    }

    [Fact]
    public void AlbumArtAssetServiceStoresDerivedAssetsBySourceIdentity()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), "AlbumArtAssetService_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);
        var coverPath = Path.Combine(tempDir, "cover.png");
        var pngBytes = Convert.FromBase64String("iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAF" +
            "c2suAAAAAXNSR0IArs4c6QAAAARnQU1BAACxjwv8YQUAAAAJ0UkGAAAAA" +
            "AQMBAAB0lqQAAAAAElFTkSuQmCC");
        File.WriteAllBytes(coverPath, pngBytes);

        var track = new MusicTrack
        {
            FilePath = Path.Combine(tempDir, "track.mp3"),
            AlbumArtPath = coverPath,
            Title = "Asset service test"
        };

        var service = new AlbumArtAssetService();
        var asset = service.Resolve(track);

        Assert.NotNull(asset);
        Assert.Equal(AlbumArtSourceKind.External, asset.Identity.SourceKind);

        var thumbnail = new DrawingImage(new DrawingGroup());
        service.StoreDerived(asset, AlbumArtQuality.Thumbnail, thumbnail);

        Assert.True(service.TryGetDerived(asset, AlbumArtQuality.Thumbnail, out var resolved));
        Assert.Same(thumbnail, resolved);
    }

    [Fact]
    public void MediumAlbumArtDoesNotTreatThumbnailAsMediumQuality()
    {
        using var loader = new AlbumArtLoader(null, initializeDiskCache: false);
        var thumb = new DrawingImage(new DrawingGroup());
        var cacheField = typeof(AlbumArtLoader).GetField("_cache", BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.NotNull(cacheField);
        var cache = (ConcurrentDictionary<string, ImageSource?>)cacheField.GetValue(loader)!;
        cache["mixed-quality-track"] = thumb;

        var result = loader.LoadMediumAlbumArt("mixed-quality-track");

        Assert.NotSame(thumb, result);
    }

    [Fact]
    public async Task FullAlbumArtDoesNotStoreThumbnailAsFullQuality()
    {
        using var loader = new AlbumArtLoader(null, initializeDiskCache: false);
        var thumb = new DrawingImage(new DrawingGroup());

        var cacheField = typeof(AlbumArtLoader).GetField("_cache", BindingFlags.Instance | BindingFlags.NonPublic);
        var fullCacheField = typeof(AlbumArtLoader).GetField("_fullQualityCache", BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.NotNull(cacheField);
        Assert.NotNull(fullCacheField);

        var cache = (ConcurrentDictionary<string, ImageSource?>)cacheField.GetValue(loader)!;
        var fullCache = (ConcurrentDictionary<string, ImageSource?>)fullCacheField.GetValue(loader)!;
        cache["full-quality-track"] = thumb;

        var result = await loader.EnqueueFullArtParse("full-quality-track", _ => Task.FromResult<ImageSource?>(null));

        Assert.Null(result);
        Assert.False(fullCache.TryGetValue("full-quality-track", out var cached) && ReferenceEquals(cached, thumb));
    }

    [Fact]
    public async Task FullAlbumArtStoresTrackDerivedAssetAfterDecode()
    {
        using var loader = new AlbumArtLoader(null, initializeDiskCache: false);
        var tempDir = Path.Combine(Path.GetTempPath(), "AlbumArtDerivedAsset_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);
        var coverPath = Path.Combine(tempDir, "derived-full-cover.png");
        var pngBytes = Convert.FromBase64String("iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAF" +
            "c2suAAAAAXNSR0IArs4c6QAAAARnQU1BAACxjwv8YQUAAAAJ0UkGAAAAA" +
            "AQMBAAB0lqQAAAAAElFTkSuQmCC");
        File.WriteAllBytes(coverPath, pngBytes);

        var track = new MusicTrack
        {
            FilePath = Path.Combine(tempDir, "derived-full-track.mp3"),
            AlbumArtPath = coverPath,
            Title = "Derived full track"
        };
        var expected = new DrawingImage(new DrawingGroup());

        var assetServiceField = typeof(AlbumArtLoader).GetField("_assetService", BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.NotNull(assetServiceField);
        var assetService = (AlbumArtAssetService)assetServiceField.GetValue(loader)!;

        var result = await loader.EnqueueFullArtParse(coverPath, track, _ => Task.FromResult<ImageSource?>(expected));

        Assert.Same(expected, result);
        var asset = assetService.Resolve(track);
        Assert.NotNull(asset);
        Assert.True(assetService.TryGetDerived(asset, AlbumArtQuality.Full, out var stored));
        Assert.Same(expected, stored);
    }

    [Fact]
    public void AlbumArtAssetServiceCanInvalidateRemovedDerivedAssets()
    {
        var service = new AlbumArtAssetService();
        var source = new OriginalAlbumArtAsset(
            new AlbumArtSourceIdentity(
                "C:/source/cover.png",
                AlbumArtSourceKind.External,
                "C:/source/cover.png",
                42,
                1,
                "album-art-v1",
                "fingerprint"),
            new AlbumArtMetadata(
                512,
                512,
                "Png",
                42,
                AlbumArtSourceKind.External,
                "C:/source/cover.png",
                1,
                "fingerprint",
                true,
                true),
            new byte[] { 1, 2, 3, 4 });

        var thumbnail = new DrawingImage(new DrawingGroup());
        service.StoreDerived(source, AlbumArtQuality.Thumbnail, thumbnail);
        Assert.True(service.TryGetDerived(source, AlbumArtQuality.Thumbnail, out var cached));
        Assert.Same(thumbnail, cached);

        service.InvalidateDerived(source);
        Assert.False(service.TryGetDerived(source, AlbumArtQuality.Thumbnail, out _));
    }

    [Fact]
    public async Task MediumAlbumArtRequestsForSamePathShareOneDecode()
    {
        using var loader = new AlbumArtLoader(null, initializeDiskCache: false);
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var expected = new DrawingImage(new DrawingGroup());
        int decodeCount = 0;

        async Task<ImageSource?> DecodeAsync(CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref decodeCount);
            started.TrySetResult();
            await release.Task.WaitAsync(cancellationToken);
            return expected;
        }

        Task<ImageSource?>[] requests = Enumerable.Range(0, 32)
            .Select(_ => loader.EnqueueMediumArtParseForTest("same-medium-track", DecodeAsync))
            .ToArray();

        await started.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal(1, Volatile.Read(ref decodeCount));
        release.TrySetResult();

        ImageSource?[] results = await Task.WhenAll(requests).WaitAsync(TimeSpan.FromSeconds(5));
        Assert.All(results, result => Assert.Same(expected, result));
        Assert.Equal(1, decodeCount);
    }

    [Fact]
    public async Task MediumAlbumArtReturnsMemoryCachedImageImmediately()
    {
        using var loader = new AlbumArtLoader(null, initializeDiskCache: false);
        var expected = new DrawingImage(new DrawingGroup());
        await loader.EnqueueMediumArtParseForTest(
            "cached-medium-track",
            _ => Task.FromResult<ImageSource?>(expected));

        Assert.Same(expected, loader.LoadMediumAlbumArt("cached-medium-track"));
    }

    [Fact]
    public void MediumAlbumArtConverterReturnsProgressiveSourceDuringScrolling()
    {
        AlbumArtLoader? previous = AlbumArtLoader.Instance;
        using var loader = new AlbumArtLoader(null, initializeDiskCache: false);
        AlbumArtLoader.Instance = loader;
        AlbumArtLoader.SetScrollingActive(true);
        try
        {
            object? result = new AlbumArtConverter().Convert(
                Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".mp3"),
                typeof(ImageSource),
                "medium",
                CultureInfo.InvariantCulture);

            Assert.IsType<DrawingImage>(result);
        }
        finally
        {
            AlbumArtLoader.SetScrollingActive(false);
            AlbumArtLoader.Instance = previous!;
        }
    }

    [Fact]
    public async Task AlbumArtLoaderDisposeCancelsOwnedParsesAndRejectsNewRequests()
    {
        var loader = new AlbumArtLoader(null, initializeDiskCache: false);
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        int decodeCount = 0;

        Task<ImageSource?> request = loader.EnqueueMediumArtParseForTest(
            "dispose-medium-track",
            async cancellationToken =>
            {
                Interlocked.Increment(ref decodeCount);
                started.TrySetResult();
                await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
                return null;
            });

        await started.Task.WaitAsync(TimeSpan.FromSeconds(5));
        loader.Dispose();
        loader.Dispose();

        Assert.Null(await request.WaitAsync(TimeSpan.FromSeconds(5)));
        Assert.Null(await loader.EnqueueFullArtParse("after-dispose", () =>
        {
            Interlocked.Increment(ref decodeCount);
            return Task.FromResult<ImageSource?>(null);
        }));
        Assert.Equal(1, decodeCount);
    }

    [Fact]
    public async Task ArtistRequestsShareWorkAndOneCancelledWaiterDoesNotCancelOthers()
    {
        using var service = new ArtistImageService(NullLogger<ArtistImageService>.Instance);
        using var cancellation = new CancellationTokenSource();
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        int requestCount = 0;

        async Task<string?> FetchAsync()
        {
            Interlocked.Increment(ref requestCount);
            started.TrySetResult();
            await release.Task;
            return "cached-image-path";
        }

        Task<string?> cancelledWaiter = service.GetOrFetchArtistImageAsync(
            "artistkey", "Artist", FetchAsync, cancellation.Token);
        Task<string?> activeWaiter = service.GetOrFetchArtistImageAsync(
            "artistkey", "Artist", FetchAsync);

        await started.Task.WaitAsync(TimeSpan.FromSeconds(5));
        cancellation.Cancel();
        Assert.Null(await cancelledWaiter.WaitAsync(TimeSpan.FromSeconds(5)));
        Assert.False(activeWaiter.IsCompleted);
        Assert.Equal(1, requestCount);

        release.TrySetResult();
        Assert.Equal("cached-image-path", await activeWaiter.WaitAsync(TimeSpan.FromSeconds(5)));
        Assert.Equal(1, requestCount);
    }

    [Fact]
    public async Task FailedArtistRequestIsRemovedAndCanBeRetried()
    {
        using var service = new ArtistImageService(NullLogger<ArtistImageService>.Instance);
        int requestCount = 0;

        string? failed = await service.GetOrFetchArtistImageAsync(
            "artistkey",
            "Artist",
            () =>
            {
                Interlocked.Increment(ref requestCount);
                return Task.FromException<string?>(new InvalidOperationException("request failed"));
            });

        string? retried = await service.GetOrFetchArtistImageAsync(
            "artistkey",
            "Artist",
            () =>
            {
                Interlocked.Increment(ref requestCount);
                return Task.FromResult<string?>("cached-image-path");
            }).WaitAsync(TimeSpan.FromSeconds(5));

        Assert.Null(failed);
        Assert.Equal("cached-image-path", retried);
        Assert.Equal(2, requestCount);
    }
}
