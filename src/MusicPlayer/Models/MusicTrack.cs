using SQLite;

namespace MusicPlayer.Models;

[Table("tracks")]
public class MusicTrack
{
    [PrimaryKey, AutoIncrement]
    public int Id { get; set; }
    
    [MaxLength(500)]
    public string FilePath { get; set; } = string.Empty;
    
    [MaxLength(300)]
    public string Title { get; set; } = string.Empty;
    
    [MaxLength(200)]
    public string Artist { get; set; } = string.Empty;
    
    [MaxLength(200)]
    public string Album { get; set; } = string.Empty;
    
    [MaxLength(100)]
    public string AlbumArtist { get; set; } = string.Empty;
    
    [MaxLength(100)]
    public string Genre { get; set; } = string.Empty;
    
    public int Year { get; set; }
    public int TrackNumber { get; set; }
    public int DiscNumber { get; set; }
    public long Duration { get; set; }
    public long FileSize { get; set; }
    public int Bitrate { get; set; }
    public int SampleRate { get; set; }
    public int Channels { get; set; }
    
    [MaxLength(500)]
    public string AlbumArtPath { get; set; } = string.Empty;
    
    public bool IsFavorite { get; set; }
    public bool IsHidden { get; set; }
    public int PlayCount { get; set; }
    public long DateAdded { get; set; }
    public long DateModified { get; set; }
    public long DateLastPlayed { get; set; }
    
    [MaxLength(500)]
    public string LyricPath { get; set; } = string.Empty;
    
    [MaxLength(500)]
    public string FolderPath { get; set; } = string.Empty;
    
    public int Quality { get; set; }

    // ── Aggregates for the card collections (artist / album / genre) ────────
    // The artist, album and genre grids show one card per collection, not one
    // card per track. Sorting those cards by the track fields of the
    // representative track was meaningless, so the cards carry the real
    // aggregates of the collection they represent.
    [Ignore]
    public int CardTrackCount { get; set; }

    [Ignore]
    public long CardTotalDuration { get; set; }

    [Ignore]
    public int CardNewestYear { get; set; }

    /// <summary>The most recent "date added" of the tracks of the collection.</summary>
    [Ignore]
    public long CardNewestDateAdded { get; set; }
}
