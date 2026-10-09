using SQLite;

namespace MusicPlayer.Models;

[Table("queue")]
public class QueueItem
{
    [PrimaryKey, AutoIncrement]
    public int Id { get; set; }
    
    public int TrackId { get; set; }
    public int Position { get; set; }
    public int CurrentIndex { get; set; }
    public bool ShuffleEnabled { get; set; }
    
    [MaxLength(1000)]
    public string ShuffleOrder { get; set; } = string.Empty;
}
