using SQLite;

namespace MusicPlayer.Models;

[Table("album_art_cache")]
public class AlbumArtCache
{
    [PrimaryKey, AutoIncrement]
    public int Id { get; set; }
    
    [MaxLength(500)]
    public string TrackPath { get; set; } = string.Empty;
    
    [MaxLength(500)]
    public string CachePath { get; set; } = string.Empty;
    
    public long FileSize { get; set; }
    public long DateCached { get; set; }
}
