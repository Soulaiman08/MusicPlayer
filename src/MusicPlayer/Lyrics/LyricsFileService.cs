using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using MusicPlayer.Utilities;

namespace MusicPlayer.Lyrics;

public sealed class LyricsFileService
{
    private const int MaxFilesPerFolder = 200;
    private readonly ILogger<LyricsFileService>? _logger;

    public LyricsFileService(ILogger<LyricsFileService>? logger = null)
    {
        _logger = logger;
    }

    public IReadOnlyList<(string Folder, string[] Files)> FindFilesByFolder(
        string configuredFolders,
        string fallbackFolder,
        CancellationToken cancellationToken = default)
    {
        string[] roots = configuredFolders.Split(';', StringSplitOptions.RemoveEmptyEntries);
        if (roots.Length == 0)
        {
            roots = [fallbackFolder];
        }

        var result = new List<(string Folder, string[] Files)>();
        var visitedRoots = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var visitedFiles = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (string root in roots)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!Directory.Exists(root))
            {
                continue;
            }

            string normalizedRoot;
            try
            {
                normalizedRoot = Path.GetFullPath(root);
            }
            catch (Exception ex) when (ex is ArgumentException or IOException or UnauthorizedAccessException)
            {
                _logger?.LogWarning(ex, "Unable to normalize lyrics folder {Folder}", root);
                continue;
            }

            if (!visitedRoots.Add(normalizedRoot))
            {
                continue;
            }

            string[] files = EnumerateLrcFiles(normalizedRoot, cancellationToken, visitedFiles);
            if (files.Length == 0)
            {
                continue;
            }

            string folder = Path.GetFileName(normalizedRoot);
            if (string.IsNullOrEmpty(folder))
            {
                folder = normalizedRoot;
            }

            result.Add((folder, files));
        }

        return result;
    }

    public Task<IReadOnlyList<(string Folder, string[] Files)>> FindFilesByFolderAsync(
        string configuredFolders,
        string fallbackFolder,
        CancellationToken cancellationToken = default)
    {
        return Task.Run(
            () => FindFilesByFolder(configuredFolders, fallbackFolder, cancellationToken),
            cancellationToken);
    }

    private string[] EnumerateLrcFiles(
        string root,
        CancellationToken cancellationToken,
        HashSet<string> visitedFiles)
    {
        var files = new List<string>(MaxFilesPerFolder);
        var pendingDirectories = new Stack<string>();
        var visitedDirectories = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        pendingDirectories.Push(root);

        while (pendingDirectories.Count > 0 && files.Count < MaxFilesPerFolder)
        {
            cancellationToken.ThrowIfCancellationRequested();
            string directory = pendingDirectories.Pop();
            if (!visitedDirectories.Add(directory))
            {
                continue;
            }

            try
            {
                foreach (string file in Directory.EnumerateFiles(directory, "*.lrc", SearchOption.TopDirectoryOnly))
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    if (PathContainment.IsStrictlyWithin(file, root) && visitedFiles.Add(file))
                    {
                        files.Add(file);
                        if (files.Count == MaxFilesPerFolder)
                        {
                            break;
                        }
                    }
                }
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                _logger?.LogWarning(ex, "Unable to enumerate lyrics files in {Folder}", directory);
            }

            if (files.Count == MaxFilesPerFolder)
            {
                break;
            }

            try
            {
                foreach (string child in Directory.EnumerateDirectories(directory, "*", SearchOption.TopDirectoryOnly))
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    try
                    {
                        if ((File.GetAttributes(child) & FileAttributes.ReparsePoint) == 0 &&
                            PathContainment.IsStrictlyWithin(child, root))
                        {
                            pendingDirectories.Push(child);
                        }
                    }
                    catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                    {
                        _logger?.LogWarning(ex, "Unable to inspect lyrics subfolder {Folder}", child);
                    }
                }
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                _logger?.LogWarning(ex, "Unable to enumerate lyrics subfolders in {Folder}", directory);
            }
        }

        return files.ToArray();
    }

    public (string Text, bool Created) OpenForEditing(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                return (File.ReadAllText(path), false);
            }

            File.WriteAllText(path, string.Empty);
            return (string.Empty, true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _logger?.LogError(ex, "Unable to open lyrics file {Path}", path);
            throw;
        }
    }

    public void Save(string path, string content)
    {
        try
        {
            File.WriteAllText(path, content);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _logger?.LogError(ex, "Unable to save lyrics file {Path}", path);
            throw;
        }
    }

    public void DeleteEmptyDraft(string path, string content, bool wasCreatedByEditor)
    {
        if (!wasCreatedByEditor || !IsEmpty(content) || !File.Exists(path))
        {
            return;
        }

        try
        {
            File.Delete(path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _logger?.LogError(ex, "Unable to delete empty lyrics draft {Path}", path);
            throw;
        }
    }

    public bool IsEmpty(string content) =>
        !content.Split('\n', StringSplitOptions.RemoveEmptyEntries)
            .Any(line => line.Trim().Length > 0);
}
