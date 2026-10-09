using SQLite;

namespace MusicPlayer.Models;

[Table("playlists")]
public class Playlist
{
    [PrimaryKey, AutoIncrement]
    public int Id { get; set; }
    
    [MaxLength(200)]
    public string Name { get; set; } = string.Empty;
    
    public int SortOrder { get; set; }
    public long DateCreated { get; set; }
    public long DateModified { get; set; }
}

[Table("playlist_tracks")]
public class PlaylistTrack
{
    [PrimaryKey, AutoIncrement]
    public int Id { get; set; }
    
    public int PlaylistId { get; set; }
    public int TrackId { get; set; }
    public int Position { get; set; }
}
