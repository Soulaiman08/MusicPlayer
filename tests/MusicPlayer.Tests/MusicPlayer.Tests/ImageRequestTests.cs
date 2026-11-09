using System.Windows.Media;
using Microsoft.Extensions.Logging.Abstractions;
using MusicPlayer.Library;
using MusicPlayer.Services;

namespace MusicPlayer.Tests;

public class ImageRequestTests
{
    [Fact]
    public async Task AlbumArtRequestsForSamePathShareOneDecodeAndResult()
    {
        var loader = new AlbumArtLoader(null, initializeDiskCache: false);
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
        var loader = new AlbumArtLoader(null, initializeDiskCache: false);
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
