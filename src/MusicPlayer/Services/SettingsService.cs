using System.ComponentModel;
using System.Runtime.CompilerServices;
using MusicPlayer.Database;

namespace MusicPlayer.Services;

public class SettingsService : INotifyPropertyChanged
{
    private readonly DatabaseService _db;

    public SettingsService(DatabaseService db) => _db = db;

    public event PropertyChangedEventHandler? PropertyChanged;
    public event Action<string>? SettingsChanged;

    protected void OnPropertyChanged([CallerMemberName] string? name = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
        if (name != null) SettingsChanged?.Invoke(name);
    }

    // === CORE PLAYER SETTINGS ===

    private float _volume;
    public float Volume { get => _volume; set { if(SetField(ref _volume, value)) _db.SetSetting("pref_volume", value.ToString()); } }

    private float _balance;
    public float Balance { get => _balance; set { if(SetField(ref _balance, value)) _db.SetSetting("pref_balance", value.ToString()); } }

    private float _speed = 1.0f;
    public float Speed { get => _speed; set { if(SetField(ref _speed, value)) _db.SetSetting("pref_speed", value.ToString()); } }

    private float _pitch;
    public float Pitch { get => _pitch; set { if(SetField(ref _pitch, value)) _db.SetSetting("pref_pitch", value.ToString()); } }

    private bool _shuffle;
    public bool Shuffle { get => _shuffle; set { if(SetField(ref _shuffle, value)) _db.SetSetting("pref_shuffle", value.ToString()); } }

    private int _repeatMode;
    public int RepeatMode { get => _repeatMode; set { if(SetField(ref _repeatMode, value)) _db.SetSetting("pref_repeat", value.ToString()); } }

    private bool _eqEnabled;
    public bool EqualizerEnabled { get => _eqEnabled; set { if(SetField(ref _eqEnabled, value)) _db.SetSetting("pref_enable_eq", value.ToString()); } }

    private string _eqPreset = "Normal";
    public string EqualizerPreset { get => _eqPreset; set { if(SetField(ref _eqPreset, value)) _db.SetSetting("pref_eq_preset", value); } }

    private bool _bassBoostEnabled;
    public bool BassBoostEnabled { get => _bassBoostEnabled; set { if(SetField(ref _bassBoostEnabled, value)) _db.SetSetting("pref_enable_bass_boost", value.ToString()); } }

    private float _bassBoostStrength;
    public float BassBoostStrength { get => _bassBoostStrength; set { if(SetField(ref _bassBoostStrength, value)) _db.SetSetting("pref_bass_boost", value.ToString()); } }

    private bool _virtualizerEnabled;
    public bool VirtualizerEnabled { get => _virtualizerEnabled; set { if(SetField(ref _virtualizerEnabled, value)) _db.SetSetting("pref_enable_virtualizer", value.ToString()); } }

    private float _virtualizerStrength;
    public float VirtualizerStrength { get => _virtualizerStrength; set { if(SetField(ref _virtualizerStrength, value)) _db.SetSetting("pref_virtualizer", value.ToString()); } }

    private int _soundEffect = -1;
    public int SoundEffect
    {
        get => _soundEffect;
        set
        {
            var selected = _db.GetSettingBool("pref_sound_effect_user_selected");
            var actual = selected ? _db.GetSettingInt("pref_sound_effect", -1) : -1;
            if (SetField(ref _soundEffect, value))
            {
                _db.SetSetting("pref_sound_effect", value.ToString());
                _db.SetSetting("pref_sound_effect_user_selected", bool.TrueString);
            }
        }
    }

    // === LIBRARY ===

    private bool _ignoreShortTracks;
    public bool IgnoreShortTracks { get => _ignoreShortTracks; set { if(SetField(ref _ignoreShortTracks, value)) _db.SetSetting("pref_ignore_60seconds_music", value.ToString()); } }

    private bool _excludeBySize = true;
    public bool ExcludeBySize { get => _excludeBySize; set { if(SetField(ref _excludeBySize, value)) _db.SetSetting("pref_exclude_music_by_size", value.ToString()); } }

    private string _scanFolders = "";
    public string ScanFolders { get => _scanFolders; set { if(SetField(ref _scanFolders, value)) _db.SetSetting("pref_scan_folders", value); } }

    // === DISPLAY ===

    private int _themeMode;
    public int ThemeMode { get => _themeMode; set { if(SetField(ref _themeMode, value)) _db.SetSetting("pref_theme_mode", value.ToString()); } }

    private bool _showLyrics = true;
    public bool ShowLyrics { get => _showLyrics; set { if(SetField(ref _showLyrics, value)) _db.SetSetting("pref_show_lyrics", value.ToString()); } }

    // === GENERAL ===

    private bool _isFirstRun = true;
    public bool IsFirstRun { get => _isFirstRun; set { if(SetField(ref _isFirstRun, value)) _db.SetSetting("is_first_run", value.ToString()); } }

    private bool _eqModified;
    public bool EqualizerModified { get => _eqModified; set { if(SetField(ref _eqModified, value)) _db.SetSetting("eq_modified", value.ToString()); } }

    // === 1. NORMAL SETTINGS ===

    private bool _showShuffleButton = true;
    public bool ShowShuffleButton { get => _showShuffleButton; set { if(SetField(ref _showShuffleButton, value)) _db.SetSetting("show_shuffle_button", value.ToString()); } }

    private bool _showQualityLogo;
    public bool ShowQualityLogo { get => _showQualityLogo; set { if(SetField(ref _showQualityLogo, value)) _db.SetSetting("show_quality_logo", value.ToString()); } }

    private bool _showForwardBackward;
    public bool ShowForwardBackward { get => _showForwardBackward; set { if(SetField(ref _showForwardBackward, value)) _db.SetSetting("show_forward_backward", value.ToString()); } }

    private int _timeForwardBackward = 15;
    public int TimeForwardBackward { get => _timeForwardBackward; set { if(SetField(ref _timeForwardBackward, value)) _db.SetSetting("time_forward_backward", value.ToString()); } }

    private bool _showHiddenFolders = true;
    public bool ShowHiddenFolders { get => _showHiddenFolders; set { if(SetField(ref _showHiddenFolders, value)) _db.SetSetting("show_hidden_folders", value.ToString()); } }

    private bool _showDirectories = true;
    public bool ShowDirectories { get => _showDirectories; set { if(SetField(ref _showDirectories, value)) _db.SetSetting("show_directory", value.ToString()); } }

    private string _libraryOrder = "title";
    public string LibraryOrder { get => _libraryOrder; set { if(SetField(ref _libraryOrder, value)) _db.SetSetting("pref_library_order", value); } }

    private bool _useEnglish;
    public bool UseEnglish { get => _useEnglish; set { if(SetField(ref _useEnglish, value)) _db.SetSetting("preference_use_english", value.ToString()); } }

    private bool _hideUpdateReminder;
    public bool HideUpdateReminder { get => _hideUpdateReminder; set { if(SetField(ref _hideUpdateReminder, value)) _db.SetSetting("hide_update_reminder", value.ToString()); } }

    private bool _checkForUpdate;
    public bool CheckForUpdate { get => _checkForUpdate; set { if(SetField(ref _checkForUpdate, value)) _db.SetSetting("check_for_update", value.ToString()); } }

    // === 2. LYRICS SETTINGS ===

    private bool _showDesktopLyrics;
    public bool ShowDesktopLyrics { get => _showDesktopLyrics; set { if(SetField(ref _showDesktopLyrics, value)) _db.SetSetting("show_desktop_lyrics", value.ToString()); } }

    private bool _bluetoothLyric = true;
    public bool BluetoothLyric { get => _bluetoothLyric; set { if(SetField(ref _bluetoothLyric, value)) _db.SetSetting("bluetooth_lyric", value.ToString()); } }

    private bool _statusBarLyrics;
    public bool StatusBarLyrics { get => _statusBarLyrics; set { if(SetField(ref _statusBarLyrics, value)) _db.SetSetting("sbar_lyric", value.ToString()); } }

    private int _lyricsFontSize = 26;
    public int LyricsFontSize { get => _lyricsFontSize; set { if(SetField(ref _lyricsFontSize, value)) _db.SetSetting("lyrics_font_size", value.ToString()); } }

    private string _lyricsColor = "#F0C040";
    public string LyricsColor { get => _lyricsColor; set { if(SetField(ref _lyricsColor, value)) _db.SetSetting("lyrics_color", value); } }

    private string _lyricsFontStyle = "Normal";
    public string LyricsFontStyle { get => _lyricsFontStyle; set { if(SetField(ref _lyricsFontStyle, value)) _db.SetSetting("lyrics_font_style", value); } }

    private double _lyricsSpeedOffset;
    public double LyricsSpeedOffset { get => _lyricsSpeedOffset; set { if(SetField(ref _lyricsSpeedOffset, value)) _db.SetSetting("lyrics_speed_offset", value.ToString()); } }

    // === 3. AUDIO SETTINGS ===

    private bool _volumeFade;
    public bool VolumeFade { get => _volumeFade; set { if(SetField(ref _volumeFade, value)) _db.SetSetting("pref_volume_fade", value.ToString()); } }

    private bool _gaplessPlayback = true;
    public bool GaplessPlayback { get => _gaplessPlayback; set { if(SetField(ref _gaplessPlayback, value)) _db.SetSetting("gapless_playback", value.ToString()); } }

    private int _crossfadeDuration = 3000;
    public int CrossfadeDuration { get => _crossfadeDuration; set { if(SetField(ref _crossfadeDuration, value)) _db.SetSetting("pref_fade_duration", value.ToString()); } }

    private bool _trackClickOperation;
    public bool TrackClickOperation { get => _trackClickOperation; set { if(SetField(ref _trackClickOperation, value)) _db.SetSetting("pref_track_click_operation", value.ToString()); } }

    private bool _replaySong;
    public bool ReplaySong { get => _replaySong; set { if(SetField(ref _replaySong, value)) _db.SetSetting("pref_replay_song", value.ToString()); } }

    private float _shakeLevel = 0.5f;
    public float ShakeLevel { get => _shakeLevel; set { if(SetField(ref _shakeLevel, value)) _db.SetSetting("shake_level", value.ToString()); } }

    private int _outputDevice = -1;
    public int OutputDevice { get => _outputDevice; set { if(SetField(ref _outputDevice, value)) _db.SetSetting("pref_output_device", value.ToString()); } }

    // === 4. REPLAYGAIN SETTINGS ===

    private int _replayGainMode;
    public int ReplayGainMode { get => _replayGainMode; set { if(SetField(ref _replayGainMode, value)) _db.SetSetting("replay_gain_mode", value.ToString()); } }

    private bool _replayGainPreamp;
    public bool ReplayGainPreqamp { get => _replayGainPreamp; set { if(SetField(ref _replayGainPreamp, value)) _db.SetSetting("replay_gain_preamp", value.ToString()); } }

    // === 5. PLAYLIST SETTINGS ===

    private bool _clickAddQueue;
    public bool ClickAddQueue { get => _clickAddQueue; set { if(SetField(ref _clickAddQueue, value)) _db.SetSetting("pref_click_add_queue", value.ToString()); } }

    private int _playlistAddPosition = 1;
    public int PlaylistAddPosition { get => _playlistAddPosition; set { if(SetField(ref _playlistAddPosition, value)) _db.SetSetting("pref_playlist_add_position", value.ToString()); } }

    private int _playlistTrackLimit;
    public int PlaylistTrackLimit { get => _playlistTrackLimit; set { if(SetField(ref _playlistTrackLimit, value)) _db.SetSetting("pref_playlist_track_limit", value.ToString()); } }

    // === 6. NOTIFICATION SETTINGS ===

    private bool _useNotificationBar;
    public bool UseNotificationBar { get => _useNotificationBar; set { if(SetField(ref _useNotificationBar, value)) _db.SetSetting("use_notification_bar", value.ToString()); } }

    private bool _classicNotification;
    public bool ClassicNotification { get => _classicNotification; set { if(SetField(ref _classicNotification, value)) _db.SetSetting("old_notification", value.ToString()); } }

    private bool _colorNotification = true;
    public bool ColorNotification { get => _colorNotification; set { if(SetField(ref _colorNotification, value)) _db.SetSetting("color_notification", value.ToString()); } }

    // === 7. LOCK SCREEN SETTINGS ===

    private bool _lockScreen = true;
    public bool LockScreen { get => _lockScreen; set { if(SetField(ref _lockScreen, value)) _db.SetSetting("pref_lock_screen", value.ToString()); } }

    private int _lockTimeFormat = 2;
    public int LockTimeFormat { get => _lockTimeFormat; set { if(SetField(ref _lockTimeFormat, value)) _db.SetSetting("lock_time_format", value.ToString()); } }

    private int _lockBackground = 1;
    public int LockBackground { get => _lockBackground; set { if(SetField(ref _lockBackground, value)) _db.SetSetting("lock_background", value.ToString()); } }

    // === 8. HEADSET SETTINGS ===

    private bool _headsetInPlay;
    public bool HeadsetInPlay { get => _headsetInPlay; set { if(SetField(ref _headsetInPlay, value)) _db.SetSetting("pref_headset_in_play", value.ToString()); } }

    private bool _headsetOutStop = true;
    public bool HeadsetOutStop { get => _headsetOutStop; set { if(SetField(ref _headsetOutStop, value)) _db.SetSetting("pref_headset_out_stop", value.ToString()); } }

    private bool _btAutoStart;
    public bool BTAutoStart { get => _btAutoStart; set { if(SetField(ref _btAutoStart, value)) _db.SetSetting("pref_bluetooth_auto_start", value.ToString()); } }

    private bool _btAutoStop = true;
    public bool BTAutoStop { get => _btAutoStop; set { if(SetField(ref _btAutoStop, value)) _db.SetSetting("pref_bluetooth_auto_stop", value.ToString()); } }

    private bool _headsetControlAllow = true;
    public bool HeadsetControlAllow { get => _headsetControlAllow; set { if(SetField(ref _headsetControlAllow, value)) _db.SetSetting("pref_headset_control_allow", value.ToString()); } }

    // === 9. REMAINING ===

    private bool _queueForSearching;
    public bool QueueForSearching { get => _queueForSearching; set { if(SetField(ref _queueForSearching, value)) _db.SetSetting("queue_for_searching", value.ToString()); } }

    private bool _keepAliveBackground;
    public bool KeepAliveBackground { get => _keepAliveBackground; set { if(SetField(ref _keepAliveBackground, value)) _db.SetSetting("keep_alive_background", value.ToString()); } }

    private bool _shakeChangeMusic;
    public bool ShakeChangeMusic { get => _shakeChangeMusic; set { if(SetField(ref _shakeChangeMusic, value)) _db.SetSetting("preference_shake_change_music", value.ToString()); } }

    private bool _swipeChangeSongs = true;
    public bool SwipeChangeSongs { get => _swipeChangeSongs; set { if(SetField(ref _swipeChangeSongs, value)) _db.SetSetting("swipe_change_songs", value.ToString()); } }

    private bool _simultaneousPlay;
    public bool SimultaneousPlay { get => _simultaneousPlay; set { if(SetField(ref _simultaneousPlay, value)) _db.SetSetting("simultaneous_play", value.ToString()); } }

    // === HELPERS ===

    private bool SetField<T>(ref T field, T value, [CallerMemberName] string? name = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value)) return false;
        field = value;
        OnPropertyChanged(name);
        return true;
    }

    public void LoadAll()
    {
        _volume = _db.GetSettingFloat("pref_volume", 0.8f);
        _balance = _db.GetSettingFloat("pref_balance");
        _speed = _db.GetSettingFloat("pref_speed", 1.0f);
        _pitch = _db.GetSettingFloat("pref_pitch");
        _shuffle = _db.GetSettingBool("pref_shuffle");
        _repeatMode = _db.GetSettingInt("pref_repeat");
        _eqEnabled = _db.GetSettingBool("pref_enable_eq");
        _eqPreset = _db.GetSetting("pref_eq_preset", "Normal");
        _bassBoostEnabled = _db.GetSettingBool("pref_enable_bass_boost");
        _bassBoostStrength = _db.GetSettingFloat("pref_bass_boost");
        _virtualizerEnabled = _db.GetSettingBool("pref_enable_virtualizer");
        _virtualizerStrength = _db.GetSettingFloat("pref_virtualizer");
        _soundEffect = _db.GetSettingBool("pref_sound_effect_user_selected") ? _db.GetSettingInt("pref_sound_effect", -1) : -1;
        _ignoreShortTracks = _db.GetSettingBool("pref_ignore_60seconds_music");
        _excludeBySize = _db.GetSettingBool("pref_exclude_music_by_size", true);
        _scanFolders = _db.GetSetting("pref_scan_folders", "");
        _themeMode = _db.GetSettingInt("pref_theme_mode");
        _showLyrics = _db.GetSettingBool("pref_show_lyrics", true);
        _isFirstRun = _db.GetSettingBool("is_first_run", true);
        _eqModified = _db.GetSettingBool("eq_modified");
        _showShuffleButton = _db.GetSettingBool("show_shuffle_button");
        _showQualityLogo = _db.GetSettingBool("show_quality_logo");
        _showForwardBackward = _db.GetSettingBool("show_forward_backward");
        _timeForwardBackward = _db.GetSettingInt("time_forward_backward", 15);
        _showHiddenFolders = _db.GetSettingBool("show_hidden_folders");
        _showDirectories = _db.GetSettingBool("show_directory");
        _libraryOrder = _db.GetSetting("pref_library_order", "title");
        _useEnglish = _db.GetSettingBool("preference_use_english");
        _hideUpdateReminder = _db.GetSettingBool("hide_update_reminder");
        _checkForUpdate = _db.GetSettingBool("check_for_update");
        _showDesktopLyrics = _db.GetSettingBool("show_desktop_lyrics");
        _bluetoothLyric = _db.GetSettingBool("bluetooth_lyric");
        _statusBarLyrics = _db.GetSettingBool("sbar_lyric");
        _lyricsFontSize = _db.GetSettingInt("lyrics_font_size", 26);
        _lyricsColor = _db.GetSetting("lyrics_color", "#F0C040");
        _lyricsFontStyle = _db.GetSetting("lyrics_font_style", "Normal");
        _lyricsSpeedOffset = _db.GetSettingFloat("lyrics_speed_offset", 0f);
        _volumeFade = _db.GetSettingBool("pref_volume_fade");
        _gaplessPlayback = _db.GetSettingBool("gapless_playback");
        _crossfadeDuration = _db.GetSettingInt("pref_fade_duration", 3000);
        _trackClickOperation = _db.GetSettingBool("pref_track_click_operation");
        _replaySong = _db.GetSettingBool("pref_replay_song");
        _shakeLevel = _db.GetSettingFloat("shake_level", 0.5f);
        _outputDevice = (int)_db.GetSettingFloat("pref_output_device", -1);
        _replayGainMode = _db.GetSettingInt("replay_gain_mode");
        _replayGainPreamp = _db.GetSettingBool("replay_gain_preamp");
        _clickAddQueue = _db.GetSettingBool("pref_click_add_queue");
        _playlistAddPosition = _db.GetSettingInt("pref_playlist_add_position", 1);
        _playlistTrackLimit = _db.GetSettingInt("pref_playlist_track_limit");
        _useNotificationBar = _db.GetSettingBool("use_notification_bar");
        _classicNotification = _db.GetSettingBool("old_notification");
        _colorNotification = _db.GetSettingBool("color_notification");
        _lockScreen = _db.GetSettingBool("pref_lock_screen");
        _lockTimeFormat = _db.GetSettingInt("lock_time_format", 2);
        _lockBackground = _db.GetSettingInt("lock_background", 1);
        _headsetInPlay = _db.GetSettingBool("pref_headset_in_play");
        _headsetOutStop = _db.GetSettingBool("pref_headset_out_stop");
        _btAutoStart = _db.GetSettingBool("pref_bluetooth_auto_start");
        _btAutoStop = _db.GetSettingBool("pref_bluetooth_auto_stop");
        _headsetControlAllow = _db.GetSettingBool("pref_headset_control_allow");
        _queueForSearching = _db.GetSettingBool("queue_for_searching");
        _keepAliveBackground = _db.GetSettingBool("keep_alive_background");
        _shakeChangeMusic = _db.GetSettingBool("preference_shake_change_music");
        _swipeChangeSongs = _db.GetSettingBool("swipe_change_songs", true);
        _simultaneousPlay = _db.GetSettingBool("simultaneous_play");
    }

    public void Reset() => _db.SetSetting("is_first_run", "True");
}
