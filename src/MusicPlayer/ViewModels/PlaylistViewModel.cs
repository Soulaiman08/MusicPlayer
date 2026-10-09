using MusicPlayer.Models;

namespace MusicPlayer.ViewModels;

public class PlaylistViewModel
{
    public Playlist Model { get; }
    public int Id => Model.Id;
    public string Name { get => Model.Name; set => Model.Name = value; }
    public int TrackCount { get; set; }

    public PlaylistViewModel(Playlist playlist)
    {
        Model = playlist;
    }
}
