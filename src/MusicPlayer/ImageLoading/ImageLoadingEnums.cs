namespace MusicPlayer.ImageLoading;

public enum ImageKind
{
    AlbumArt,
    Artist,
    Wallpaper
}

public enum ImageQuality
{
    Thumbnail, // ~100px (compact list rows)
    Small,     // ~160px (tracks grid, mini player)
    Medium,    // ~280px (albums & artists cards)
    Large,     // ~560px (detail pages)
    Full       // Original or up to 1024px (full player)
}

public enum ImagePriority
{
    Visible = 1,
    Playback = 2,
    NearScroll = 3,
    OpenPage = 4,
    Preload = 5,
    Background = 6
}

public enum SourceKind
{
    Unknown = 0,
    Embedded = 1,
    Sidecar = 2,
    External = 3
}

