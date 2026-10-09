using System;
using System.IO;

namespace MusicPlayer.Utilities;

public static class AppPaths
{
    public static string AppDataPath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "MusicPlayer");
    
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
    }
}
