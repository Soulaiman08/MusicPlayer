using CommunityToolkit.Mvvm.ComponentModel;

namespace MusicPlayer.Models;

public class FolderItem : ObservableObject
{
    public string Name { get; set; } = string.Empty;
    public string Path { get; set; } = string.Empty;
    public int TrackCount { get; set; }
    public int Id { get; set; }
    public bool IsManaged { get; set; }
}
