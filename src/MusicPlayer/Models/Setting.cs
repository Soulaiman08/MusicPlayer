using SQLite;

namespace MusicPlayer.Models;

[Table("settings")]
public class Setting
{
    [PrimaryKey, MaxLength(200)]
    public string Key { get; set; } = string.Empty;
    
    [MaxLength(2000)]
    public string Value { get; set; } = string.Empty;
    
    public int Type { get; set; } // 0=string, 1=int, 2=bool, 3=float
}
