using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace MusicPlayer.Lyrics;

public sealed class LyricsFileService
{
    public IReadOnlyList<(string Folder, string[] Files)> FindFilesByFolder(
        string configuredFolders,
        string fallbackFolder)
    {
        string[] roots = configuredFolders.Split(';', StringSplitOptions.RemoveEmptyEntries);
        if (roots.Length == 0)
        {
            roots = [fallbackFolder];
        }

        var result = new List<(string Folder, string[] Files)>();
        foreach (string root in roots)
        {
            if (!Directory.Exists(root))
            {
                continue;
            }

            string[] files = Directory.GetFiles(root, "*.lrc", SearchOption.AllDirectories)
                .Take(200)
                .ToArray();
            if (files.Length == 0)
            {
                continue;
            }

            string folder = Path.GetFileName(root);
            if (string.IsNullOrEmpty(folder))
            {
                folder = root;
            }

            result.Add((folder, files));
        }

        return result;
    }

    public (string Text, bool Created) OpenForEditing(string path)
    {
        if (File.Exists(path))
        {
            return (File.ReadAllText(path), false);
        }

        File.WriteAllText(path, string.Empty);
        return (string.Empty, true);
    }

    public void Save(string path, string content) => File.WriteAllText(path, content);

    public void DeleteEmptyDraft(string path, string content, bool wasCreatedByEditor)
    {
        if (wasCreatedByEditor && IsEmpty(content) && File.Exists(path))
        {
            File.Delete(path);
        }
    }

    public bool IsEmpty(string content) =>
        !content.Split('\n', StringSplitOptions.RemoveEmptyEntries)
            .Any(line => line.Trim().Length > 0);
}
