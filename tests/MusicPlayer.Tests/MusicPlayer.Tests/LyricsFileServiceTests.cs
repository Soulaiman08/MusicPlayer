using MusicPlayer.Lyrics;
using System.Threading;
using System.Threading.Tasks;

namespace MusicPlayer.Tests;

public class LyricsFileServiceTests
{
    [Fact]
    public void FindFilesByFolder_ReturnsLrcFilesForConfiguredFolders()
    {
        string root = Path.Combine(Path.GetTempPath(), "lyrics_test_" + Guid.NewGuid());
        string folder = Path.Combine(root, "Music");
        Directory.CreateDirectory(folder);
        try
        {
            string lrcPath = Path.Combine(folder, "track.lrc");
            File.WriteAllText(lrcPath, "[00:01.00]line");
            File.WriteAllText(Path.Combine(folder, "notes.txt"), "not lyrics");

            var groups = new LyricsFileService().FindFilesByFolder(folder, root);

            var group = Assert.Single(groups);
            Assert.Equal("Music", group.Folder);
            Assert.Equal(new[] { lrcPath }, group.Files);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void FindFilesByFolder_AppliesLimitDuringRecursiveEnumerationAndDeduplicatesRoots()
    {
        string root = Path.Combine(Path.GetTempPath(), "lyrics_test_" + Guid.NewGuid());
        string folder = Path.Combine(root, "Music");
        Directory.CreateDirectory(folder);
        try
        {
            for (int i = 0; i < 250; i++)
            {
                File.WriteAllText(Path.Combine(folder, $"{i:D3}.lrc"), "[00:01.00]line");
            }

            var groups = new LyricsFileService().FindFilesByFolder($"{folder};{folder}", root);

            var group = Assert.Single(groups);
            Assert.Equal(200, group.Files.Length);
            Assert.Equal(200, group.Files.Distinct(StringComparer.OrdinalIgnoreCase).Count());
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task FindFilesByFolderAsync_ObservesCancellation()
    {
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            new LyricsFileService().FindFilesByFolderAsync(
                string.Empty,
                Path.GetTempPath(),
                cancellation.Token));
    }

    [Fact]
    public void OpenForEditing_CreatesMissingDraftAndReadsExistingLyrics()
    {
        string root = Path.Combine(Path.GetTempPath(), "lyrics_test_" + Guid.NewGuid());
        Directory.CreateDirectory(root);
        try
        {
            var service = new LyricsFileService();
            string path = Path.Combine(root, "track.lrc");

            var created = service.OpenForEditing(path);

            Assert.True(created.Created);
            Assert.Empty(created.Text);
            Assert.True(File.Exists(path));

            service.Save(path, "[00:01.00]hello");
            var existing = service.OpenForEditing(path);

            Assert.False(existing.Created);
            Assert.Equal("[00:01.00]hello", existing.Text);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Theory]
    [InlineData("")]
    [InlineData(" \r\n\t")]
    [InlineData("\r\n")]
    public void IsEmpty_IgnoresBlankLines(string content)
    {
        Assert.True(new LyricsFileService().IsEmpty(content));
    }

    [Fact]
    public void DeleteEmptyDraft_OnlyDeletesEmptyEditorCreatedFiles()
    {
        string root = Path.Combine(Path.GetTempPath(), "lyrics_test_" + Guid.NewGuid());
        Directory.CreateDirectory(root);
        try
        {
            var service = new LyricsFileService();
            string path = Path.Combine(root, "draft.lrc");
            File.WriteAllText(path, string.Empty);

            service.DeleteEmptyDraft(path, string.Empty, wasCreatedByEditor: false);
            Assert.True(File.Exists(path));

            service.DeleteEmptyDraft(path, string.Empty, wasCreatedByEditor: true);
            Assert.False(File.Exists(path));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void DeleteEmptyDraft_PreservesNonEmptyDraft()
    {
        string root = Path.Combine(Path.GetTempPath(), "lyrics_test_" + Guid.NewGuid());
        Directory.CreateDirectory(root);
        try
        {
            string path = Path.Combine(root, "draft.lrc");
            File.WriteAllText(path, "[00:01.00]keep");

            new LyricsFileService().DeleteEmptyDraft(path, "[00:01.00]keep", wasCreatedByEditor: true);

            Assert.True(File.Exists(path));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task FindLyricForTrackAsync_ReadsLrcAndUsesTextFallback()
    {
        string root = Path.Combine(Path.GetTempPath(), "lyrics_test_" + Guid.NewGuid());
        Directory.CreateDirectory(root);
        try
        {
            string track = Path.Combine(root, "track.mp3");
            File.WriteAllText(Path.ChangeExtension(track, ".lrc"), "[00:01.00]lrc");

            LyricParser? lrc = await LyricParser.FindLyricForTrackAsync(track);
            Assert.NotNull(lrc);
            Assert.Contains(lrc!.Lines, line => line.Text == "lrc");

            File.Delete(Path.ChangeExtension(track, ".lrc"));
            File.WriteAllText(Path.ChangeExtension(track, ".txt"), "[00:02.00]text");
            LyricParser? text = await LyricParser.FindLyricForTrackAsync(track);
            Assert.NotNull(text);
            Assert.Contains(text!.Lines, line => line.Text == "text");
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task FindLyricForTrackAsync_ReturnsNullWhenNoSiblingLyricsExist()
    {
        string root = Path.Combine(Path.GetTempPath(), "lyrics_test_" + Guid.NewGuid());
        Directory.CreateDirectory(root);
        try
        {
            LyricParser? parser = await LyricParser.FindLyricForTrackAsync(Path.Combine(root, "missing.mp3"));
            Assert.Null(parser);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task FindLyricForTrackAsync_ObservesCancellation()
    {
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            LyricParser.FindLyricForTrackAsync(
                Path.Combine(Path.GetTempPath(), "missing.mp3"),
                cancellation.Token));
    }
}
