using MusicPlayer.Audio;
using MusicPlayer.Database;
using MusicPlayer.Models;
using Microsoft.Extensions.Logging;

namespace MusicPlayer.Services;

public class PlaybackService
{
    private readonly AudioEngine _engine;
    private readonly DatabaseService _db;
    private readonly ILogger<PlaybackService> _logger;

    public PlaybackService(AudioEngine engine, DatabaseService db, ILogger<PlaybackService> logger)
    {
        _engine = engine;
        _db = db;
        _logger = logger;

        _engine.TrackChanged += OnTrackChanged;
    }

    public AudioEngine Engine => _engine;

    public void PlayTrack(MusicTrack track)
    {
        _engine.LoadAndPlayAsync(track);
        UpdatePlayCount(track);
    }

    public void PlayList(List<MusicTrack> tracks, int startIndex = 0)
    {
        _engine.SetQueue(tracks, startIndex);
        if (tracks.Count > startIndex && startIndex >= 0)
        {
            _engine.LoadAndPlayAsync(tracks[startIndex]);
            UpdatePlayCount(tracks[startIndex]);
        }
    }

    public void PlayPause() { if (_engine.IsPlaying) _engine.Pause(); else _engine.Play(); }
    public void Stop() => _engine.Stop();
    public void Next() => _engine.Next();
    public void Previous() => _engine.Previous();
    public void Seek(TimeSpan position) => _engine.Seek(position);
    public void SeekRelative(double seconds) => _engine.SeekRelative(seconds);

    public void ToggleShuffle()
    {
        _engine.ShuffleEnabled = !_engine.ShuffleEnabled;
        _db.SetSetting("pref_shuffle", _engine.ShuffleEnabled.ToString());
    }

    public void ToggleRepeat()
    {
        var modes = new[] { PlayMode.Shuffle, PlayMode.LoopAll, PlayMode.Single, PlayMode.Order };
        var current = Array.IndexOf(modes, _engine.PlayMode);
        _engine.PlayMode = modes[(current + 1) % modes.Length];
        _db.SetSetting("pref_repeat", ((int)_engine.PlayMode).ToString());
    }

    private void UpdatePlayCount(MusicTrack track)
    {
        track.PlayCount++;
        track.DateLastPlayed = DateTime.Now.ToBinary();
        _db.UpdateTrack(track);
    }

    private void OnTrackChanged(object? sender, MusicTrack track) { }
}
