using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace MusicPlayer.Models;

public class HomeCard : INotifyPropertyChanged
{
    private string _title = string.Empty;
    private string _iconKey = string.Empty;
    private string _count = string.Empty;
    private string _targetView = string.Empty;
    private bool _isSupported;

    public string Title { get => _title; set { _title = value; OnPropertyChanged(); } }
    public string IconKey { get => _iconKey; set { _iconKey = value; OnPropertyChanged(); } }
    public string Count { get => _count; set { _count = value; OnPropertyChanged(); } }
    public string TargetView { get => _targetView; set { _targetView = value; OnPropertyChanged(); } }
    public bool IsSupported { get => _isSupported; set { _isSupported = value; OnPropertyChanged(); } }

    public event PropertyChangedEventHandler? PropertyChanged;
    protected void OnPropertyChanged([CallerMemberName] string? name = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
