using System;
using System.IO;

namespace MusicPlayer.Utilities;

/// <summary>
/// Unified, separator-aware containment check for file system paths.
/// Replaces fragile <c>string.StartsWith</c> comparisons such as
/// "C:\Music".StartsWith("C:\Music") which would wrongly match
/// "C:\Music2\song.mp3" as belonging to "C:\Music".
/// </summary>
public static class PathContainment
{
    /// <summary>
    /// Returns true when <paramref name="candidate"/> is equal to or located
    /// inside <paramref name="container"/>. Both paths are normalized to full
    /// paths; comparison is case-insensitive (Windows semantics).
    /// </summary>
    public static bool IsWithin(string? candidate, string? container)
    {
        if (string.IsNullOrWhiteSpace(candidate) || string.IsNullOrWhiteSpace(container))
            return false;

        string fullCandidate;
        string fullContainer;
        try
        {
            fullCandidate = Path.GetFullPath(candidate.Trim());
            fullContainer = Path.GetFullPath(container.Trim());
        }
        catch
        {
            return false;
        }

        // Normalize directory separators.
        fullCandidate = fullCandidate.Replace('/', '\\');
        fullContainer = fullContainer.Replace('/', '\\');

        // Ensure container ends with a separator so that "C:\Music" does not
        // match "C:\Music2". Drive roots ("C:\") and UNC roots already end
        // with a separator; for others append one.
        if (!fullContainer.EndsWith("\\", StringComparison.Ordinal))
            fullContainer += "\\";

        // If candidate is a directory without trailing separator, treat it as
        // a container boundary by appending one for the prefix test, but also
        // allow exact equality (candidate == container).
        if (string.Equals(fullCandidate.TrimEnd('\\'), fullContainer.TrimEnd('\\'), StringComparison.OrdinalIgnoreCase))
            return true;

        if (!fullCandidate.EndsWith("\\", StringComparison.Ordinal))
            fullCandidate += "\\";

        return fullCandidate.StartsWith(fullContainer, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Returns true when <paramref name="candidate"/> is strictly inside
    /// <paramref name="container"/> (not equal to it).
    /// </summary>
    public static bool IsStrictlyWithin(string? candidate, string? container)
    {
        if (string.IsNullOrWhiteSpace(candidate) || string.IsNullOrWhiteSpace(container))
            return false;
        if (string.Equals(Normalize(candidate), Normalize(container), StringComparison.OrdinalIgnoreCase))
            return false;
        return IsWithin(candidate, container);
    }

    private static string Normalize(string path)
    {
        try { return Path.GetFullPath(path.Trim()).Replace('/', '\\').TrimEnd('\\'); }
        catch { return path; }
    }
}
