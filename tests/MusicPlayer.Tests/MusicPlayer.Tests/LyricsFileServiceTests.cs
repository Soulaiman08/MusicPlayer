using MusicPlayer.Lyrics;

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
}
