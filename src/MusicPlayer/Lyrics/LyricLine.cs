using System.ComponentModel;

namespace MusicPlayer.Lyrics;

public class LyricLine : INotifyPropertyChanged
{
    private bool _isCurrent;
    public TimeSpan Time { get; set; }
    public string Text { get; set; } = string.Empty;
    public bool IsCurrent
    {
        get => _isCurrent;
        set
        {
            if (_isCurrent == value) return;
            _isCurrent = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsCurrent)));
        }
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    
    public LyricLine(TimeSpan time, string text)
    {
        Time = time;
        Text = text;
    }
}
