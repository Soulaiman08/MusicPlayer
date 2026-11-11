namespace MusicPlayer.ImageLoading;

public enum ImageKind
{
    AlbumArt,
    Artist,
    Wallpaper
}

public enum ImageQuality
{
    Thumbnail, // ~80x80 (lists)
    Medium,    // ~220x220 (cards, mini player)
    Large,     // ~512x512
    Full       // Original or up to 1024x1024 (full player)
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

