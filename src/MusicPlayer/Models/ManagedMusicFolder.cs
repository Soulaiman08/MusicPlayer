using SQLite;

namespace MusicPlayer.Models;

[Table("managed_folders")]
public class ManagedMusicFolder
{
    [PrimaryKey, AutoIncrement]
    public int Id { get; set; }

    [MaxLength(500)]
    public string Path { get; set; } = string.Empty;

    [MaxLength(200)]
    public string Name { get; set; } = string.Empty;

    public bool Enabled { get; set; } = true;
    public long AddedDate { get; set; }
    public long LastScanDate { get; set; }
}
