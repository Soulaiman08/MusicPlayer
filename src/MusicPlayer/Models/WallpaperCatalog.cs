using System;
using System.Collections.Generic;
using System.Linq;

namespace MusicPlayer.Models;

/// <summary>
/// Static catalog of wallpapers bundled as WPF Resources.
/// Replaces the old filesystem scan of AppDomain.CurrentDomain.BaseDirectory/Wallpaper.
/// </summary>
public static class WallpaperCatalog
{
    public sealed record Entry(
        string Id,
        string Category,
        string FileName,
        string DisplayName,
        string ResourceUri,
        string ThumbnailUri);

    private static readonly Entry[] _entries =
    {
        new("builtin:Defaut/bg_005", "Defaut", "bg_005.png",
            "Default", "pack://application:,,,/Wallpaper/Defaut/bg_005.png",
            "pack://application:,,,/Wallpaper/Thumbnails/Defaut/bg_005.jpg"),
        new("builtin:LIve/01", "LIve", "01_gettyimages_543217268_super_resized.png",
            "Comet over forest", "pack://application:,,,/Wallpaper/LIve/01_gettyimages_543217268_super_resized.png",
            "pack://application:,,,/Wallpaper/Thumbnails/LIve/01_gettyimages_543217268_super_resized.jpg"),
        new("builtin:LIve/02", "LIve", "02_gettyimages_559175997_small_resized.png",
            "Comet over observatory", "pack://application:,,,/Wallpaper/LIve/02_gettyimages_559175997_small_resized.png",
            "pack://application:,,,/Wallpaper/Thumbnails/LIve/02_gettyimages_559175997_small_resized.jpg"),
        new("builtin:LIve/03", "LIve", "03_gettyimages_641740218_super_resized.png",
            "City bridge meteors", "pack://application:,,,/Wallpaper/LIve/03_gettyimages_641740218_super_resized.png",
            "pack://application:,,,/Wallpaper/Thumbnails/LIve/03_gettyimages_641740218_super_resized.jpg"),
        new("builtin:LIve/04", "LIve", "04_gettyimages_578830122_super_resized.png",
            "Milky way panorama", "pack://application:,,,/Wallpaper/LIve/04_gettyimages_578830122_super_resized.png",
            "pack://application:,,,/Wallpaper/Thumbnails/LIve/04_gettyimages_578830122_super_resized.jpg"),
        new("builtin:LIve/05", "LIve", "05_gettyimages_477920536_high_resized.png",
            "Meteor shower", "pack://application:,,,/Wallpaper/LIve/05_gettyimages_477920536_high_resized.png",
            "pack://application:,,,/Wallpaper/Thumbnails/LIve/05_gettyimages_477920536_high_resized.jpg"),
        new("builtin:LIve/06", "LIve", "06_gettyimages_159063292_super_resized.png",
            "Meteor over mountain", "pack://application:,,,/Wallpaper/LIve/06_gettyimages_159063292_super_resized.png",
            "pack://application:,,,/Wallpaper/Thumbnails/LIve/06_gettyimages_159063292_super_resized.jpg"),
        new("builtin:LIve/07", "LIve", "07_gettyimages_471782677_super_resized.png",
            "Meteor streaks", "pack://application:,,,/Wallpaper/LIve/07_gettyimages_471782677_super_resized.png",
            "pack://application:,,,/Wallpaper/Thumbnails/LIve/07_gettyimages_471782677_super_resized.jpg"),
        new("builtin:LIve/08", "LIve", "08_gettyimages_494290908_super_resized.png",
            "Milky way shooting star", "pack://application:,,,/Wallpaper/LIve/08_gettyimages_494290908_super_resized.png",
            "pack://application:,,,/Wallpaper/Thumbnails/LIve/08_gettyimages_494290908_super_resized.jpg"),
        new("builtin:LIve/09", "LIve", "09_gettyimages_513037171_super_resized.png",
            "Milky way vertical", "pack://application:,,,/Wallpaper/LIve/09_gettyimages_513037171_super_resized.png",
            "pack://application:,,,/Wallpaper/Thumbnails/LIve/09_gettyimages_513037171_super_resized.jpg"),
        new("builtin:LIve/10", "LIve", "10_gettyimages_559549785_small_resized.png",
            "Night landscape", "pack://application:,,,/Wallpaper/LIve/10_gettyimages_559549785_small_resized.png",
            "pack://application:,,,/Wallpaper/Thumbnails/LIve/10_gettyimages_559549785_small_resized.jpg"),
        new("builtin:LIve/11", "LIve", "11_gettyimages_589513190_medium_resized.png",
            "Comet over pines", "pack://application:,,,/Wallpaper/LIve/11_gettyimages_589513190_medium_resized.png",
            "pack://application:,,,/Wallpaper/Thumbnails/LIve/11_gettyimages_589513190_medium_resized.jpg"),
    };

    public static IReadOnlyList<Entry> All => _entries;

    public static Entry? FindById(string id)
        => _entries.FirstOrDefault(e => string.Equals(e.Id, id, StringComparison.OrdinalIgnoreCase));

    public static Entry? FindByFileName(string fileName)
        => _entries.FirstOrDefault(e => string.Equals(e.FileName, fileName, StringComparison.OrdinalIgnoreCase));

    /// <summary>
    /// Resolve an old-style path/URI to a catalog entry. Returns null if not found.
    /// Handles: absolute paths, pack URIs, old Assets/Backgrounds paths, and stable IDs.
    /// </summary>
    public static Entry? Resolve(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;

        // Stable ID
        var byId = FindById(value);
        if (byId != null) return byId;

        // Extract file name from path/URI
        string fileName = value;
        try
        {
            if (value.Contains('/')) fileName = value.Substring(value.LastIndexOf('/') + 1);
            if (value.Contains('\\')) fileName = value.Substring(value.LastIndexOf('\\') + 1);
        }
        catch { }

        // Match by file name
        var byName = FindByFileName(fileName);
        if (byName != null) return byName;

        // Old Assets/Backgrounds paths → default
        if (value.Contains("Assets/Backgrounds", StringComparison.OrdinalIgnoreCase) ||
            value.Contains("Assets\\Backgrounds", StringComparison.OrdinalIgnoreCase))
        {
            return FindById("builtin:Defaut/bg_005");
        }

        return null;
    }

    public const string DefaultId = "builtin:Defaut/bg_005";
}