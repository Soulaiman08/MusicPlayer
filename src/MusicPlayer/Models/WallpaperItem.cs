using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace MusicPlayer.Models;

public class WallpaperItem : INotifyPropertyChanged
{
    public string FilePath { get; set; } = string.Empty;
    public string ThumbnailPath { get; set; } = string.Empty;
    private System.Windows.Media.ImageSource? _thumbnailSource;
    public System.Windows.Media.ImageSource? ThumbnailSource
    {
        get => _thumbnailSource ??= Converters.WallpaperThumbnailConverter.GetThumbnail(ThumbnailPath, 240);
        set => _thumbnailSource = value;
    }
    public string FileName { get; set; } = string.Empty;
    public string Category { get; set; } = string.Empty;

    private bool _isSelected;
    public bool IsSelected
    {
        get => _isSelected;
        set
        {
            if (_isSelected != value)
            {
                _isSelected = value;
                OnPropertyChanged();
            }
        }
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    protected void OnPropertyChanged([CallerMemberName] string? name = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
