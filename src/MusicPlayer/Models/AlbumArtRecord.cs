using SQLite;

namespace MusicPlayer.Models;

[Table("album_art_records")]
public sealed class AlbumArtRecord
{
    [PrimaryKey]
    [MaxLength(500)]
    public string NormalizedTrackPath { get; set; } = string.Empty;

    [Indexed]
    public int TrackId { get; set; }

    public long FileSizeBytes { get; set; }

    public long LastModifiedUtcTicks { get; set; }

    [MaxLength(64)]
    [Indexed]
    public string ContentHash { get; set; } = string.Empty;

    public int SourceKind { get; set; } // 1: Embedded, 2: Sidecar, 3: External, 0: Unknown

    [MaxLength(500)]
    public string? SourceArtPath { get; set; }

    [MaxLength(500)]
    public string? ThumbnailPath { get; set; }

    [MaxLength(500)]
    public string? MediumPath { get; set; }

    [MaxLength(500)]
    public string? LargePath { get; set; }

    [MaxLength(500)]
    public string? FullPath { get; set; }

    public int Width { get; set; }

    public int Height { get; set; }

    public int SchemaVersion { get; set; } = 4;

    public long LastVerifiedUtcTicks { get; set; }

    public bool HasArtwork { get; set; }
}

