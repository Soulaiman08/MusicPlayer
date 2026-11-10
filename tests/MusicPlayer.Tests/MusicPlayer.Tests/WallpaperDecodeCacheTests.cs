using MusicPlayer.Utilities;

namespace MusicPlayer.Tests;

public class WallpaperDecodeCacheTests
{
    [Fact]
    public async Task PreloadAsync_AlreadyCancelledRequestReturnsWithoutStartingDecode()
    {
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        await WallpaperDecodeCache.PreloadAsync(
            new[] { Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".png") },
            3840,
            cancellation.Token);
    }

    [Fact]
    public async Task PreloadAsync_DuplicateSourcesCompleteWithOneCachedEntry()
    {
        string path = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".png");
        byte[] imageBytes = Convert.FromBase64String(
            "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mP8/x8AAwMCAO+/l1sAAAAASUVORK5CYII=");
        await File.WriteAllBytesAsync(path, imageBytes);
        string cacheKey = path + "|480";

        try
        {
            await WallpaperDecodeCache.PreloadAsync(new[] { path, path }, 480);

            Assert.True(WallpaperDecodeCache.TryGet(cacheKey, out var image));
            Assert.NotNull(image);
        }
        finally
        {
            File.Delete(path);
        }
    }
}
