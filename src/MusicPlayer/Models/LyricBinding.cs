using SQLite;

namespace MusicPlayer.Models;

[Table("lyric_bindings")]
public class LyricBinding
{
    [PrimaryKey, AutoIncrement]
    public int Id { get; set; }
    
    [MaxLength(500)]
    public string TrackPath { get; set; } = string.Empty;
    
    [MaxLength(500)]
    public string LyricPath { get; set; } = string.Empty;
}
