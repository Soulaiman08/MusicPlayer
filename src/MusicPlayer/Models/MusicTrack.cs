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
}
