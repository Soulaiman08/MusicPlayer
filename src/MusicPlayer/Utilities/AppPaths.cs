using System;
using System.IO;

namespace MusicPlayer.Utilities;

public static class AppPaths
{
    /// <summary>
    /// Optional override for every data location (database, caches, logs,
    /// artist images). Set MUSICPLAYER_DATA_DIR to run the app against a
    /// throw-away data directory — required by the performance fixtures so a
    /// synthetic library can be measured without touching the real one.
    /// </summary>
    public static string? DataDirOverride =>
        Environment.GetEnvironmentVariable("MUSICPLAYER_DATA_DIR") is { Length: > 0 } value
            ? value
            : null;

    public static string AppDataPath => DataDirOverride ?? Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "MusicPlayer");

    /// <summary>Local (machine-wide) data root; artist images live here.</summary>
    public static string LocalDataPath => DataDirOverride ?? Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "MusicPlayer");

    public static string ArtistImagesPath => Path.Combine(LocalDataPath, "ArtistImages");

    public static string LogsPath => Path.Combine(AppDataPath, "Logs");
    public static string DatabasePath => Path.Combine(AppDataPath, "music.db");
    public static string CachePath => Path.Combine(AppDataPath, "Cache");
    public static string AlbumArtCachePath => Path.Combine(CachePath, "AlbumArt");
    public static string LyricsCachePath => Path.Combine(CachePath, "Lyrics");
    public static string SettingsPath => Path.Combine(AppDataPath, "settings.json");

    public static void EnsureDirectories()
    {
        Directory.CreateDirectory(AppDataPath);
        Directory.CreateDirectory(LogsPath);
        Directory.CreateDirectory(CachePath);
        Directory.CreateDirectory(AlbumArtCachePath);
        Directory.CreateDirectory(LyricsCachePath);
        Directory.CreateDirectory(ArtistImagesPath);
    }
}
