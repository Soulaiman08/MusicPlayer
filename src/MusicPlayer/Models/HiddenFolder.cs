using SQLite;

namespace MusicPlayer.Models;

[Table("hidden_folders")]
public class HiddenFolder
{
    [PrimaryKey, AutoIncrement]
    public int Id { get; set; }
    
    [MaxLength(500)]
    public string FolderPath { get; set; } = string.Empty;
}
