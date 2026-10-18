using System;
using System.CodeDom.Compiler;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.ComponentModel.__Internals;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using Microsoft.Win32;
using MusicPlayer.Audio;
using MusicPlayer.DSP;
using MusicPlayer.Database;
using MusicPlayer.Library;
using MusicPlayer.Lyrics;
using MusicPlayer.Models;
using MusicPlayer.Services;
using MusicPlayer.Utilities;
using MusicPlayer.Views;
using TagLib;

namespace MusicPlayer.ViewModels;

public partial class MainViewModel : ViewModelBase
{
	private readonly PlaybackService _playback;

	private readonly DatabaseService _db;

	private readonly SettingsService _settings;

	private readonly NavigationService _nav;

	private readonly MusicScanner _scanner;

	private readonly AlbumArtLoader _albumArt;

	private readonly ArtistImageService _artistImages;

	private readonly ILogger<MainViewModel> _logger;

	private readonly WindowsIntegrationService? _winIntegration;

	private (List<MusicTrack> Items, List<KeyValuePair<string, string>> CachedImages)? _preloadedArtistResult;

	private DispatcherTimer? _lyricAdvanceTimer;

	private string _currentArtistName = string.Empty;

	private string _artistDetailActiveTab = "albums";

	private int _artistAlbumCount;

	private int _artistTrackCount;
	private string _currentView = "home";
	private bool _isDrawerOpen;
	private bool _showMiniPlayer = true;
	private bool _showFullPlayer;
	private bool _showEqualizer;
	private bool _showSoundEffects;
	private bool _showSpeedPitch;
	private bool _showLyrics;
	private bool _showSettings;
	private bool _showQueue;
	private bool _showPlaylistDetail;
	private bool _showNewPlaylistOverlay;
	private string _newPlaylistName = "My Playlist";
	private bool _showRenamePlaylistOverlay;
	private string _renamePlaylistName = string.Empty;
	private PlaylistViewModel? _renamingPlaylist;
	private string _statusBarMessage = string.Empty;
	private bool _isStatusBarVisible;
	private bool _isStatusBarHiddenLinkVisible;
	private bool _isStatusBarLoading;
	private int _currentPlaylistId;
	private Playlist? _currentPlaylist;
	private string _searchQuery = string.Empty;
	private bool _isSearching;
	private bool _isWindowMaximized;
	private string _librarySortOrder = "date_added";
	private bool _isLibrarySortDescending;
	private string _selectedLibraryTab = "songs";
	private bool _isSelectMode;
	private bool _isSongsSectionCollapsed;
	private bool _showTrackBottomSheet;
	private MusicTrack? _bottomSheetTrack;
	private bool _showTrackInfo;
	private bool _showSortMenu;
	private bool _showOverflowMenu;
	private bool _showLyricsSettings;
	private bool _showLyricsSpeedPanel;
	private MusicTrack? _currentTrack;
	private bool _isPlaying;
	private TimeSpan _position;
	private TimeSpan _duration;
	private float _volume = 0.8f;
	private float _previousVolumeBeforeMute = 80f;
	private float _balance;
	private float _speed = 1f;
	private float _pitch;
	private bool _isFavorite;
	private bool _shuffleEnabled;
	private PlayMode _playMode = PlayMode.LoopAll;
	private CurrentlyPlayingPage _currentlyPlayingPage;
	private string _currentLyricText = string.Empty;
	private int _currentLyricIndex = -1;
	private string _lyricsSourcePath = string.Empty;
	private bool _isScanning;
	private bool _isInitializing = true;
	private int _scanProgress;
	private string _scanStatus = string.Empty;
	private EqPreset _currentEqPreset = EqPreset.Presets[0];
	private bool _eqEnabled = true;
	private float _band0;
	private float _band1;
	private float _band2;
	private float _band3;
	private float _band4;
	private float _band5;
	private float _band6;
	private float _band7;
	private float _band8;
	private float _band9;
	private float _bassBoostStrength;
	private bool _bassBoostEnabled;
	private float _virtualizerStrength;
	private bool _virtualizerEnabled;
	private ReverbPreset _reverbPreset;
	private bool _reverbEnabled;
	private bool _loudnessEnabled;
	private float _loudnessGain;
	private int _currentSoundEffect = -1;
	private float _soundEffectIntensity = 1f;

	private CancellationTokenSource? _searchCts;

	private readonly Stack<string> _viewHistory = new Stack<string>();

	internal bool _navigatedFromFullPlayer;

	private bool _isSyncingSpeedText;

	private bool _isSyncingPitchText;

	private string _currentTab = "songs";
	private bool _isMagicSoundOn;
	private PlaylistViewModel? _selectedPlaylistForEdit;
	private BulkObservableCollection<MusicTrack> _playlistEditTracks = new BulkObservableCollection<MusicTrack>();
	private MusicTrack? _editingTrack;
	private string _editTitle = string.Empty;
	private string _editArtist = string.Empty;
	private string _editAlbum = string.Empty;
	private string _albumSearchQuery = string.Empty;
	private string _albumArtStatus = string.Empty;

	private List<string> _lrcAllFiles = new List<string>();

	private List<(string folder, string[] files)> _lrcFolderData = new List<(string, string[])>();
	private string _selectedLrcFolder = "ALL";
	private string _lrcEditText = string.Empty;
	private bool _lrcEditShowValidation;

	private string _lrcEditPath = string.Empty;

	private bool _lrcEditAutoCreated;
	private string _selectedTheme = "Dark";
	private string _selectedWallpaperCategory = "All";
	private string _backgroundImagePath = "pack://application:,,,/Assets/Backgrounds/bg_001.png";
	private double _wallpaperBlur = 0.0;
	private double _wallpaperAlpha = 51.0;
	private string _themeEditWallpaperPath = string.Empty;
	private double _themeEditBlur = 0.0;
	private System.Windows.Threading.DispatcherTimer? _themeEditBlurTimer;
	private double _themeEditAlpha = 51.0;
	private bool _isStatusBarLyricsEnabled;

	private CancellationTokenSource? _sleepTimerCts;
	private int _sleepTimerSelectedMinutes;
	private int _sleepTimerManualMinutes = 15;
	private bool _isManualSleepTimer;
	private bool _sleepTimerPlayToEnd;
	private System.Windows.Threading.DispatcherTimer? _sleepTimerTicker;
	private DateTime? _sleepTimerEndTime;
	private string _sleepTimerStatus = string.Empty;

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.RelayCommandGenerator", "8.4.0.0")]
	private RelayCommand<object?>? setPlayingPageCommand;

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.RelayCommandGenerator", "8.4.0.0")]
	private RelayCommand? toggleDrawerCommand;

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.RelayCommandGenerator", "8.4.0.0")]
	private RelayCommand? closeDrawerCommand;

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.RelayCommandGenerator", "8.4.0.0")]
	private RelayCommand? openFullPlayerCommand;

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.RelayCommandGenerator", "8.4.0.0")]
	private RelayCommand? closeFullPlayerCommand;

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.RelayCommandGenerator", "8.4.0.0")]
	private RelayCommand? goBackCommand;

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.RelayCommandGenerator", "8.4.0.0")]
	private RelayCommand? playPauseCommand;

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.RelayCommandGenerator", "8.4.0.0")]
	private RelayCommand? stopCommand;

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.RelayCommandGenerator", "8.4.0.0")]
	private RelayCommand? nextCommand;

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.RelayCommandGenerator", "8.4.0.0")]
	private RelayCommand? previousCommand;

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.RelayCommandGenerator", "8.4.0.0")]
	private RelayCommand<object?>? playFileCommand;

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.RelayCommandGenerator", "8.4.0.0")]
	private RelayCommand<object?>? playFilesCommand;

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.RelayCommandGenerator", "8.4.0.0")]
	private RelayCommand<object?>? seekRelativeCommand;

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.RelayCommandGenerator", "8.4.0.0")]
	private RelayCommand<string>? setSpeedPresetCommand;

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.RelayCommandGenerator", "8.4.0.0")]
	private RelayCommand? resetSpeedPitchCommand;

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.RelayCommandGenerator", "8.4.0.0")]
	private RelayCommand<object?>? toggleSoundEffectCommand;

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.RelayCommandGenerator", "8.4.0.0")]
	private RelayCommand<EqPreset?>? selectEqPresetCommand;

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.RelayCommandGenerator", "8.4.0.0")]
	private RelayCommand? toggleShuffleCommand;

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.RelayCommandGenerator", "8.4.0.0")]
	private RelayCommand? toggleRepeatCommand;

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.RelayCommandGenerator", "8.4.0.0")]
	private RelayCommand? toggleFavoriteCommand;

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.RelayCommandGenerator", "8.4.0.0")]
	private AsyncRelayCommand? shuffleAllCommand;

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.RelayCommandGenerator", "8.4.0.0")]
	private RelayCommand? toggleSelectModeCommand;

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.RelayCommandGenerator", "8.4.0.0")]
	private RelayCommand<MusicTrack?>? playTrackCommand;

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.RelayCommandGenerator", "8.4.0.0")]
	private RelayCommand<HomeCard?>? homeCardClickCommand;

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.RelayCommandGenerator", "8.4.0.0")]
	private RelayCommand<string>? navigateCommand;

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.RelayCommandGenerator", "8.4.0.0")]
	private RelayCommand? showEqualizerPanelCommand;

	private RelayCommand? resetEqualizerCommand;

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.RelayCommandGenerator", "8.4.0.0")]
	private RelayCommand? showSoundEffectsPanelCommand;

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.RelayCommandGenerator", "8.4.0.0")]
	private RelayCommand? showSpeedPitchPanelCommand;

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.RelayCommandGenerator", "8.4.0.0")]
	private RelayCommand? showLyricsPanelCommand;

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.RelayCommandGenerator", "8.4.0.0")]
	private RelayCommand? showSettingsPanelCommand;

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.RelayCommandGenerator", "8.4.0.0")]
	private RelayCommand? showQueuePanelCommand;

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.RelayCommandGenerator", "8.4.0.0")]
	private RelayCommand<string>? switchArtistDetailTabCommand;

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.RelayCommandGenerator", "8.4.0.0")]
	private RelayCommand? playArtistCommand;

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.RelayCommandGenerator", "8.4.0.0")]
	private RelayCommand? shuffleArtistCommand;

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.RelayCommandGenerator", "8.4.0.0")]
	private RelayCommand? addArtistToQueueCommand;

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.RelayCommandGenerator", "8.4.0.0")]
	private RelayCommand? playAlbumCommand;

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.RelayCommandGenerator", "8.4.0.0")]
	private RelayCommand? shuffleAlbumCommand;

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.RelayCommandGenerator", "8.4.0.0")]
	private RelayCommand? addAlbumToQueueCommand;

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.RelayCommandGenerator", "8.4.0.0")]
	private RelayCommand? playPlaylistDetailCommand;

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.RelayCommandGenerator", "8.4.0.0")]
	private RelayCommand? shufflePlaylistDetailCommand;

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.RelayCommandGenerator", "8.4.0.0")]
	private RelayCommand? playGenreDetailCommand;
	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.RelayCommandGenerator", "8.4.0.0")]
	private RelayCommand? shuffleGenreDetailCommand;

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.RelayCommandGenerator", "8.4.0.0")]
	private RelayCommand? addPlaylistDetailToQueueCommand;
	private RelayCommand<MusicTrack?>? removeTrackFromPlaylistCommand;

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.RelayCommandGenerator", "8.4.0.0")]
	private AsyncRelayCommand? addManagedFolderCommand;

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.RelayCommandGenerator", "8.4.0.0")]
	private AsyncRelayCommand<FolderItem?>? removeManagedFolderCommand;

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.RelayCommandGenerator", "8.4.0.0")]
	private RelayCommand? navigateToPlaylistsCommand;

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.RelayCommandGenerator", "8.4.0.0")]
	private RelayCommand? toggleSearchCommand;

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.RelayCommandGenerator", "8.4.0.0")]
	private RelayCommand? toggleMuteCommand;

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.RelayCommandGenerator", "8.4.0.0")]
	private RelayCommand? searchCommand;

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.RelayCommandGenerator", "8.4.0.0")]
	private RelayCommand<PlaylistViewModel?>? playPlaylistCommand;

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.RelayCommandGenerator", "8.4.0.0")]
	private RelayCommand<string?>? createPlaylistCommand;

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.RelayCommandGenerator", "8.4.0.0")]
	private RelayCommand<MusicTrack?>? addToQueueCommand;

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.RelayCommandGenerator", "8.4.0.0")]
	private RelayCommand<MusicTrack?>? removeFromQueueCommand;

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.RelayCommandGenerator", "8.4.0.0")]
	private RelayCommand? clearQueueCommand;

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.RelayCommandGenerator", "8.4.0.0")]
	private RelayCommand<MusicTrack?>? moveQueueUpCommand;

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.RelayCommandGenerator", "8.4.0.0")]
	private RelayCommand<MusicTrack?>? moveQueueDownCommand;

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.RelayCommandGenerator", "8.4.0.0")]
	private RelayCommand<MusicTrack?>? playQueueTrackCommand;

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.RelayCommandGenerator", "8.4.0.0")]
	private RelayCommand? saveQueueAsPlaylistCommand;

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.RelayCommandGenerator", "8.4.0.0")]
	private RelayCommand<LyricLine?>? seekToLyricCommand;

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.RelayCommandGenerator", "8.4.0.0")]
	private RelayCommand? openHiddenFoldersCommand;

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.RelayCommandGenerator", "8.4.0.0")]
	private RelayCommand? addHiddenFolderDialogCommand;

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.RelayCommandGenerator", "8.4.0.0")]
	private RelayCommand<HiddenFolder?>? removeHiddenFolderCommand;

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.RelayCommandGenerator", "8.4.0.0")]
	private RelayCommand? openHiddenFilesCommand;

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.RelayCommandGenerator", "8.4.0.0")]
	private RelayCommand<MusicTrack?>? unhideTrackCommand;

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.RelayCommandGenerator", "8.4.0.0")]
	private RelayCommand<MusicTrack?>? hideTrackCommand;

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.RelayCommandGenerator", "8.4.0.0")]
	private RelayCommand? addHiddenFilesDialogCommand;

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.RelayCommandGenerator", "8.4.0.0")]
	private RelayCommand? openScanSettingsCommand;

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.RelayCommandGenerator", "8.4.0.0")]
	private RelayCommand? addScanFolderDialogCommand;

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.RelayCommandGenerator", "8.4.0.0")]
	private RelayCommand<string?>? removeScanFolderCommand;

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.RelayCommandGenerator", "8.4.0.0")]
	private AsyncRelayCommand? openDuplicateFinderCommand;

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.RelayCommandGenerator", "8.4.0.0")]
	private RelayCommand<MusicTrack?>? deleteDuplicateCommand;

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.RelayCommandGenerator", "8.4.0.0")]
	private AsyncRelayCommand? openDeletedMusicCommand;

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.RelayCommandGenerator", "8.4.0.0")]
	private RelayCommand<MusicTrack?>? removeDeletedTrackCommand;

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.RelayCommandGenerator", "8.4.0.0")]
	private RelayCommand? clearAllDeletedCommand;

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.RelayCommandGenerator", "8.4.0.0")]
	private AsyncRelayCommand? openMusicSelectCommand;

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.RelayCommandGenerator", "8.4.0.0")]
	private AsyncRelayCommand? openMusicSetEditCommand;

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.RelayCommandGenerator", "8.4.0.0")]
	private RelayCommand? openPlaylistEditCommand;

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.RelayCommandGenerator", "8.4.0.0")]
	private RelayCommand<PlaylistViewModel?>? movePlaylistUpCommand;

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.RelayCommandGenerator", "8.4.0.0")]
	private RelayCommand<PlaylistViewModel?>? movePlaylistDownCommand;

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.RelayCommandGenerator", "8.4.0.0")]
	private RelayCommand? openPlaylistSelectCommand;

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.RelayCommandGenerator", "8.4.0.0")]
	private RelayCommand<PlaylistViewModel?>? selectPlaylistForAddCommand;

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.RelayCommandGenerator", "8.4.0.0")]
	private RelayCommand<PlaylistViewModel?>? deletePlaylistCommand;

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.RelayCommandGenerator", "8.4.0.0")]
	private RelayCommand<(PlaylistViewModel vm, string newName)>? renamePlaylistCommand;

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.RelayCommandGenerator", "8.4.0.0")]
	private RelayCommand<MusicTrack?>? openEditTagsCommand;

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.RelayCommandGenerator", "8.4.0.0")]
	private RelayCommand? saveEditTagsCommand;

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.RelayCommandGenerator", "8.4.0.0")]
	private RelayCommand? openBatchEditCommand;

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.RelayCommandGenerator", "8.4.0.0")]
	private RelayCommand<string?>? batchSetArtistCommand;

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.RelayCommandGenerator", "8.4.0.0")]
	private RelayCommand? openAlbumGetterCommand;

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.RelayCommandGenerator", "8.4.0.0")]
	private RelayCommand? searchAlbumArtCommand;

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.RelayCommandGenerator", "8.4.0.0")]
	private RelayCommand? openAlbumSaveCommand;

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.RelayCommandGenerator", "8.4.0.0")]
	private RelayCommand? saveAlbumArtCommand;

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.RelayCommandGenerator", "8.4.0.0")]
	private RelayCommand? openAudioEditorCommand;

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.RelayCommandGenerator", "8.4.0.0")]
	private RelayCommand? openLrcBrowserCommand;

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.RelayCommandGenerator", "8.4.0.0")]
	private RelayCommand? openLrcEditCommand;

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.RelayCommandGenerator", "8.4.0.0")]
	private RelayCommand? saveLrcEditCommand;

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.RelayCommandGenerator", "8.4.0.0")]
	private RelayCommand? openSleepTimerCommand;

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.RelayCommandGenerator", "8.4.0.0")]
	private RelayCommand<string?>? setSleepTimerCommand;

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.RelayCommandGenerator", "8.4.0.0")]
	private RelayCommand? cancelSleepTimerCommand;

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.RelayCommandGenerator", "8.4.0.0")]
	private RelayCommand<MusicTrack?>? openTrackBottomSheetCommand;

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.RelayCommandGenerator", "8.4.0.0")]
	private RelayCommand? closeTrackBottomSheetCommand;

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.RelayCommandGenerator", "8.4.0.0")]
	private RelayCommand<MusicTrack?>? openTrackInfoCommand;

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.RelayCommandGenerator", "8.4.0.0")]
	private RelayCommand? closeTrackInfoCommand;

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.RelayCommandGenerator", "8.4.0.0")]
	private RelayCommand? toggleSortMenuCommand;

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.RelayCommandGenerator", "8.4.0.0")]
	private RelayCommand? toggleOverflowMenuCommand;

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.RelayCommandGenerator", "8.4.0.0")]
	private RelayCommand<string?>? selectSortOptionCommand;

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.RelayCommandGenerator", "8.4.0.0")]
	private AsyncRelayCommand? refreshMetadataCommand;

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.RelayCommandGenerator", "8.4.0.0")]
	private RelayCommand? toggleSongsSectionCommand;

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.RelayCommandGenerator", "8.4.0.0")]
	private RelayCommand<MusicTrack?>? toggleTrackFavoriteCommand;

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.RelayCommandGenerator", "8.4.0.0")]
	private RelayCommand<MusicTrack?>? playNextCommand;

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.RelayCommandGenerator", "8.4.0.0")]
	private RelayCommand<MusicTrack?>? enqueueCommand;

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.RelayCommandGenerator", "8.4.0.0")]
	private RelayCommand<MusicTrack?>? setRingtoneCommand;

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.RelayCommandGenerator", "8.4.0.0")]
	private RelayCommand<MusicTrack?>? trimAudioCommand;

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.RelayCommandGenerator", "8.4.0.0")]
	private RelayCommand<MusicTrack?>? shareTrackCommand;

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.RelayCommandGenerator", "8.4.0.0")]
	private RelayCommand<MusicTrack?>? deleteTrackFromLibraryCommand;

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.RelayCommandGenerator", "8.4.0.0")]
	private AsyncRelayCommand? openLyricListCommand;

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.RelayCommandGenerator", "8.4.0.0")]
	private RelayCommand<string?>? bindLyricFileCommand;

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.RelayCommandGenerator", "8.4.0.0")]
	private RelayCommand? openStatusBarLyricsCommand;

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.RelayCommandGenerator", "8.4.0.0")]
	private RelayCommand? openThemeGalleryCommand;

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.RelayCommandGenerator", "8.4.0.0")]
	private RelayCommand<string?>? applyThemeCommand;

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.RelayCommandGenerator", "8.4.0.0")]
	private RelayCommand? openThemeEditorCommand;

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.RelayCommandGenerator", "8.4.0.0")]
	private RelayCommand<WallpaperItem?>? selectWallpaperCommand;

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.RelayCommandGenerator", "8.4.0.0")]
	private RelayCommand<string?>? selectWallpaperCategoryCommand;

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.RelayCommandGenerator", "8.4.0.0")]
	private RelayCommand<string?>? openGenreTracksCommand;

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.RelayCommandGenerator", "8.4.0.0")]
	private RelayCommand<string?>? openFolderTracksCommand;

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.RelayCommandGenerator", "8.4.0.0")]
	private RelayCommand<object?>? setOutputDeviceCommand;

	public SettingsService Settings => _settings;

	public BulkObservableCollection<MusicTrack> Tracks { get; } = new BulkObservableCollection<MusicTrack>();


	public BulkObservableCollection<MusicTrack> Albums { get; } = new BulkObservableCollection<MusicTrack>();


	public BulkObservableCollection<MusicTrack> Artists { get; } = new BulkObservableCollection<MusicTrack>();


	public BulkObservableCollection<MusicTrack> Genres { get; } = new BulkObservableCollection<MusicTrack>();


	public BulkObservableCollection<FolderItem> Folders { get; } = new BulkObservableCollection<FolderItem>();


	public BulkObservableCollection<MusicTrack> FolderDetailTracks { get; } = new BulkObservableCollection<MusicTrack>();


	public ConcurrentDictionary<string, string> ArtistImages { get; } = new ConcurrentDictionary<string, string>(StringComparer.OrdinalIgnoreCase);


	public ConcurrentDictionary<string, ImageSource> ArtistImageCache { get; } = new ConcurrentDictionary<string, ImageSource>(StringComparer.OrdinalIgnoreCase);


	// Canonical album cover per album name (global scope — see
	// DatabaseService.PickCanonicalTrack). Rebuilt on every artist-detail load
	// BEFORE the album cards are published, so card bindings resolve instantly
	// on the UI thread with no DB access. Key = trimmed album name.
	public ConcurrentDictionary<string, MusicTrack> AlbumCoverTracks { get; } = new ConcurrentDictionary<string, MusicTrack>(StringComparer.OrdinalIgnoreCase);


	public BulkObservableCollection<MusicTrack> ArtistDetailTracks { get; } = new BulkObservableCollection<MusicTrack>();

	public BulkObservableCollection<MusicTrack> GenreDetailTracks { get; } = new BulkObservableCollection<MusicTrack>();


	public BulkObservableCollection<MusicTrack> ArtistAlbums { get; } = new BulkObservableCollection<MusicTrack>();


	public BulkObservableCollection<MusicTrack> FilteredArtists { get; } = new BulkObservableCollection<MusicTrack>();


	public BulkObservableCollection<MusicTrack> FilteredAlbums { get; } = new BulkObservableCollection<MusicTrack>();


	public BulkObservableCollection<MusicTrack> FilteredGenres { get; } = new BulkObservableCollection<MusicTrack>();


	public bool IsSearchActive
	{
		get
		{
			if (IsSearching)
			{
				return !string.IsNullOrWhiteSpace(SearchQuery);
			}
			return false;
		}
	}

	public IEnumerable<MusicTrack> DisplayedArtists
	{
		get
		{
			if (!IsSearchActive)
			{
				return Artists;
			}
			return FilteredArtists;
		}
	}

	public IEnumerable<MusicTrack> DisplayedAlbums
	{
		get
		{
			if (!IsSearchActive)
			{
				return Albums;
			}
			return FilteredAlbums;
		}
	}

	public IEnumerable<MusicTrack> DisplayedGenres
	{
		get
		{
			if (!IsSearchActive)
			{
				return Genres;
			}
			return FilteredGenres;
		}
	}

	public string CurrentFolderName { get; set; } = string.Empty;

	public string CurrentGenreName { get; set; } = string.Empty;

	public string CurrentArtistName
	{
		get
		{
			return _currentArtistName;
		}
		set
		{
			if (_currentArtistName != value)
			{
				_currentArtistName = value;
				OnPropertyChanged("CurrentArtistName");
			}
		}
	}

	public string ArtistDetailActiveTab
	{
		get
		{
			return _artistDetailActiveTab;
		}
		set
		{
			if (_artistDetailActiveTab != value)
			{
				_artistDetailActiveTab = value;
				OnPropertyChanged("ArtistDetailActiveTab");
				OnPropertyChanged("IsArtistDetailAlbumsTab");
				OnPropertyChanged("IsArtistDetailTracksTab");
			}
		}
	}

	public bool IsArtistDetailAlbumsTab => ArtistDetailActiveTab == "albums";

	public bool IsArtistDetailTracksTab => ArtistDetailActiveTab == "tracks";

	public int ArtistAlbumCount
	{
		get
		{
			return _artistAlbumCount;
		}
		set
		{
			if (_artistAlbumCount != value)
			{
				_artistAlbumCount = value;
				OnPropertyChanged("ArtistAlbumCount");
				OnPropertyChanged("ArtistDetailStats");
			}
		}
	}

	public int ArtistTrackCount
	{
		get
		{
			return _artistTrackCount;
		}
		set
		{
			if (_artistTrackCount != value)
			{
				_artistTrackCount = value;
				OnPropertyChanged("ArtistTrackCount");
				OnPropertyChanged("ArtistDetailStats");
			}
		}
	}

	public string ArtistDetailStats => $"{ArtistAlbumCount} album{((ArtistAlbumCount != 1) ? "s" : "")} & {ArtistTrackCount} track{((ArtistTrackCount != 1) ? "s" : "")}";

	public string ArtistCollage1 { get; set; } = string.Empty;


	public string ArtistCollage2 { get; set; } = string.Empty;


	public string ArtistCollage3 { get; set; } = string.Empty;


	public string ArtistCollage4 { get; set; } = string.Empty;


	public BulkObservableCollection<MusicTrack> AlbumDetailTracks { get; } = new BulkObservableCollection<MusicTrack>();


	public string CurrentAlbumName { get; set; } = string.Empty;


	public string CurrentAlbumArtist { get; set; } = string.Empty;

	public BulkObservableCollection<MusicTrack> PlaylistDetailTracks { get; } = new BulkObservableCollection<MusicTrack>();

	public string CurrentPlaylistName { get; set; } = string.Empty;

	public int PlaylistDetailTrackCount { get; set; }


	public string CurrentAlbumYear { get; set; } = string.Empty;


	public string CurrentAlbumArtPath { get; set; } = string.Empty;


	public MusicTrack? CurrentAlbumArtTrack { get; set; }

	public int AlbumDetailTrackCount { get; set; }

	public MusicTrack? MoreOptionsContextTrack { get; set; }

	public BulkObservableCollection<MusicTrack> Queue { get; } = new BulkObservableCollection<MusicTrack>();

	private string _queueSummaryText = "Queue is empty";
	public string QueueSummaryText
	{
		get => _queueSummaryText;
		private set
		{
			if (_queueSummaryText == value) return;
			_queueSummaryText = value;
			OnPropertyChanged(nameof(QueueSummaryText));
		}
	}

	private void UpdateQueueSummary()
	{
		if (Queue.Count == 0)
		{
			QueueSummaryText = "Queue is empty";
			return;
		}
		long totalMs = 0;
		foreach (MusicTrack t in Queue)
			totalMs += t.Duration;
		var ts = TimeSpan.FromMilliseconds(totalMs);
		string dur = totalMs > 0
			? $"{Math.Max(1, (int)ts.TotalMinutes)} min"
			: "";
		QueueSummaryText = $"{Queue.Count} song{(Queue.Count == 1 ? "" : "s")}" + (dur.Length > 0 ? $" • {dur}" : "");
	}

	private string _queueFilterText = string.Empty;
	public string QueueFilterText
	{
		get => _queueFilterText;
		set
		{
			if (_queueFilterText != value)
			{
				_queueFilterText = value;
				OnPropertyChanged(nameof(QueueFilterText));
				ApplyQueueFilter();
			}
		}
	}

	public void ApplyQueueFilter()
	{
		try
		{
			var view = System.Windows.Data.CollectionViewSource.GetDefaultView(Queue);
			if (view == null) return;
			if (string.IsNullOrWhiteSpace(_queueFilterText))
			{
				view.Filter = null;
			}
			else
			{
				var filter = _queueFilterText.Trim();
				view.Filter = obj =>
				{
					if (obj is MusicTrack track)
					{
						return (track.Title?.Contains(filter, StringComparison.OrdinalIgnoreCase) ?? false) ||
						       (track.Artist?.Contains(filter, StringComparison.OrdinalIgnoreCase) ?? false) ||
						       (track.Album?.Contains(filter, StringComparison.OrdinalIgnoreCase) ?? false);
					}
					return false;
				};
			}
		}
		catch { }
	}

	public event Action? ScrollToCurrentTrackRequested;
	public void RequestScrollToCurrentTrack()
	{
		ScrollToCurrentTrackRequested?.Invoke();
	}

	public BulkObservableCollection<MusicTrack> SearchResults { get; } = new BulkObservableCollection<MusicTrack>();


	public BulkObservableCollection<MusicTrack> FavoriteTracks { get; } = new BulkObservableCollection<MusicTrack>();


	public BulkObservableCollection<MusicTrack> RecentPlayTracks { get; } = new BulkObservableCollection<MusicTrack>();


	public BulkObservableCollection<MusicTrack> RecentAddTracks { get; } = new BulkObservableCollection<MusicTrack>();


	public BulkObservableCollection<MusicTrack> MostPlayedTracks { get; } = new BulkObservableCollection<MusicTrack>();


	public BulkObservableCollection<PlaylistViewModel> Playlists { get; } = new BulkObservableCollection<PlaylistViewModel>();


	public ObservableCollection<LyricLine> Lyrics { get; } = new ObservableCollection<LyricLine>();


	public ObservableCollection<HomeCard> HomeCards { get; } = new ObservableCollection<HomeCard>();


	public ObservableCollection<EqPreset> EqPresets { get; } = new ObservableCollection<EqPreset>();


	public ObservableCollection<LibrarySortOption> LibrarySortOptions { get; } = new ObservableCollection<LibrarySortOption>
	{
		new LibrarySortOption("title_az", "A-Z"),
		new LibrarySortOption("title_za", "Z-A"),
		new LibrarySortOption("year", "Year"),
		new LibrarySortOption("artist", "Artist"),
		new LibrarySortOption("album", "Album"),
		new LibrarySortOption("folder", "Folder"),
		new LibrarySortOption("date_added", "Date added"),
		new LibrarySortOption("size", "Size"),
		new LibrarySortOption("duration", "Duration"),
		new LibrarySortOption("random", "Random"),
		new LibrarySortOption("reverse", "Reverse")
	};


	public int LyricsFontSize
	{
		get
		{
			return _settings.LyricsFontSize;
		}
		set
		{
			_settings.LyricsFontSize = value;
			OnPropertyChanged("LyricsFontSize");
			OnPropertyChanged("LyricsFontSizeLabel");
		}
	}

	public string LyricsFontSizeLabel => $"{LyricsFontSize}";

	public string LyricsColor
	{
		get
		{
			return _settings.LyricsColor;
		}
		set
		{
			_settings.LyricsColor = value;
			OnPropertyChanged("LyricsColor");
		}
	}

	public string LyricsFontStyle
	{
		get
		{
			return _settings.LyricsFontStyle;
		}
		set
		{
			_settings.LyricsFontStyle = value;
			OnPropertyChanged("LyricsFontStyle");
		}
	}

	public bool DesktopLyricsToggle
	{
		get
		{
			return _settings.ShowDesktopLyrics;
		}
		set
		{
			_settings.ShowDesktopLyrics = value;
			OnPropertyChanged("DesktopLyricsToggle");
			OnPropertyChanged("IsDesktopLyricsVisible");
		}
	}

	public int SleepTimerManualMinutes
	{
		get => _sleepTimerManualMinutes;
		set
		{
			if (value < 1) value = 1;
			if (value > 999) value = 999;
			if (_sleepTimerManualMinutes == value) return;
			_sleepTimerManualMinutes = value;
			OnPropertyChanged(nameof(SleepTimerManualMinutes));
			OnPropertyChanged(nameof(IsManualSleepTimerActive));
			OnPropertyChanged(nameof(SleepTimerPresetMinutes));
		}
	}

	public bool SleepTimerPlayToEnd
	{
		get => _sleepTimerPlayToEnd;
		set
		{
			if (_sleepTimerPlayToEnd == value) return;
			_sleepTimerPlayToEnd = value;
			OnPropertyChanged(nameof(SleepTimerPlayToEnd));
		}
	}

	public int SleepTimerSelectedMinutes
	{
		get => _sleepTimerSelectedMinutes;
		private set
		{
			if (_sleepTimerSelectedMinutes == value) return;
			_sleepTimerSelectedMinutes = value;
			OnPropertyChanged(nameof(SleepTimerSelectedMinutes));
			OnPropertyChanged(nameof(IsManualSleepTimerActive));
			OnPropertyChanged(nameof(SleepTimerPresetMinutes));
		}
	}

	public bool IsManualSleepTimerActive => _isManualSleepTimer && _sleepTimerSelectedMinutes > 0;

	public int SleepTimerPresetMinutes => _isManualSleepTimer ? -1 : _sleepTimerSelectedMinutes;

	public string SleepTimerStatus
	{
		get => _sleepTimerStatus;
		private set
		{
			if (_sleepTimerStatus == value) return;
			_sleepTimerStatus = value;
			OnPropertyChanged(nameof(SleepTimerStatus));
		}
	}

	public double LyricsSpeedOffset
	{
		get
		{
			return _settings.LyricsSpeedOffset;
		}
		set
		{
			_settings.LyricsSpeedOffset = value;
			OnPropertyChanged("LyricsSpeedOffset");
			OnPropertyChanged("LyricsSpeedLabel");
		}
	}

	public string LyricsSpeedLabel
	{
		get
		{
			if (LyricsSpeedOffset != 0.0)
			{
				return $"{LyricsSpeedOffset:+0.0;-0.0}S";
			}
			return "Normal";
		}
	}

	public int CurrentPlayingTrackId => CurrentTrack?.Id ?? (-1);

	public bool IsAlbumArtPage => CurrentlyPlayingPage == CurrentlyPlayingPage.AlbumArt;

	public bool IsLyricsPage => CurrentlyPlayingPage == CurrentlyPlayingPage.Lyrics;

	public bool IsVisualizerPage => CurrentlyPlayingPage == CurrentlyPlayingPage.Visualizer;

	public bool IsForwardBackwardVisible => _settings.ShowForwardBackward;

	public bool IsShuffleButtonVisible => _settings.ShowShuffleButton;

	public double SeekDuration => _settings.TimeForwardBackward;

	public bool IsDesktopLyricsVisible => _settings.ShowDesktopLyrics;

	public bool IsLyricsVisible => _settings.ShowLyrics;

	public bool ClickAddQueueEnabled => _settings.ClickAddQueue;

	public string PlaylistAddPositionMode
	{
		get
		{
			if (_settings.PlaylistAddPosition != 0)
			{
				return "End";
			}
			return "Top";
		}
	}

	public string PlaylistTrackLimitDisplay
	{
		get
		{
			if (_settings.PlaylistTrackLimit != 0)
			{
				return _settings.PlaylistTrackLimit.ToString();
			}
			return "No limit";
		}
	}

	public double PlaybackProgress
	{
		get
		{
			if (!(Duration.TotalSeconds > 0.0))
			{
				return 0.0;
			}
			return Position.TotalSeconds / Duration.TotalSeconds * 100.0;
		}
		set
		{
			if (!(Duration.TotalSeconds <= 0.0))
			{
				double value2 = Math.Clamp(value / 100.0 * Duration.TotalSeconds, 0.0, Duration.TotalSeconds);
				if (IsSeeking)
				{
					Position = TimeSpan.FromSeconds(value2);
				}
				else
				{
					_playback.Seek(TimeSpan.FromSeconds(value2));
					_winIntegration?.RefreshTimeline();
				}
			}
		}
	}

	public bool IsSeeking { get; set; }

	public string CurrentSpeedDisplay => $"{Speed:0.00}x";

	public string CurrentPitchDisplay => $"{PitchDisplay:0.00}";

	public float SpeedLog
	{
		get
		{
			return (float)Math.Log2(Speed);
		}
		set
		{
			float speed = (float)Math.Pow(2.0, Math.Clamp(value, -1.0, 1.0));
			Speed = speed;
		}
	}

	public float PitchDisplay
	{
		get
		{
			if (Pitch == 0f)
			{
				return 1f;
			}
			return (float)Math.Pow(2.0, (double)Pitch / 12.0);
		}
		set
		{
			float num = Math.Clamp(value, 0.5f, 2f);
			float value2 = (float)(12.0 * Math.Log2(num));
			Pitch = Math.Clamp(value2, -12f, 12f);
		}
	}

	public float PitchLog
	{
		get
		{
			if (Pitch != 0f)
			{
				return (float)((double)Pitch / 12.0);
			}
			return 0f;
		}
		set
		{
			float pitch = (float)Math.Clamp((double)value * 12.0, -12.0, 12.0);
			Pitch = pitch;
		}
	}

	public string SpeedText
	{
		get
		{
			return $"{Speed:0.00}";
		}
		set
		{
			if (!_isSyncingSpeedText)
			{
				if (float.TryParse(value?.Replace("x", "").Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out var result))
				{
					result = Math.Clamp(result, 0.5f, 2f);
					Speed = result;
				}
				OnPropertyChanged("SpeedText");
			}
		}
	}

	public string PitchText
	{
		get
		{
			return $"{PitchDisplay:0.00}";
		}
		set
		{
			if (!_isSyncingPitchText)
			{
				if (float.TryParse(value?.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out var result))
				{
					result = Math.Clamp(result, 0.5f, 2f);
					float value2 = (float)(12.0 * Math.Log2(result));
					Pitch = Math.Clamp(value2, -12f, 12f);
				}
				OnPropertyChanged("PitchText");
			}
		}
	}

	public bool IsTrackListView => CurrentView == "songs";

	public string CurrentTab
	{
		get
		{
			return _currentTab;
		}
		set
		{
			if (_currentTab != value)
			{
				_currentTab = value;
				OnPropertyChanged("CurrentTab");
				OnPropertyChanged("IsSongsTab");
				OnPropertyChanged("IsArtistsTab");
				OnPropertyChanged("IsAlbumsTab");
				OnPropertyChanged("IsGenresTab");
				OnPropertyChanged("IsFoldersTab");
			}
		}
	}

	public bool IsSongsTab => CurrentTab == "songs";

	public bool IsArtistsTab => CurrentTab == "artists";

	public bool IsAlbumsTab => CurrentTab == "albums";

	public bool IsGenresTab => CurrentTab == "genres";

	public bool IsFoldersTab => CurrentTab == "folders";

	public int CurrentTabIndex => CurrentTab switch
	{
		"songs" => 0, 
		"artists" => 1, 
		"albums" => 2, 
		"genres" => 3, 
		"folders" => 4, 
		_ => 0, 
	};

	public bool IsSearchableView
	{
		get
		{
			switch (CurrentView)
			{
			case "songs":
			case "recent":
			case "recent_add":
			case "most_played":
			case "favorites":
			case "folder_detail":
				return true;
			default:
				return false;
			}
		}
	}

	public bool CanNavigateBack
	{
		get
		{
			if (!(CurrentView != "home"))
			{
				return _viewHistory.Count > 0;
			}
			return true;
		}
	}

	public string CurrentTrackListTitle => CurrentView switch
	{
		"recent" => "RECENT PLAY", 
		"recent_add" => "RECENT ADD", 
		"most_played" => "MOST PLAYED", 
		"favorites" => "FAVORITES", 
		"folder_detail" => CurrentFolderName, 
		_ => "ALL SONGS", 
	};

	public ObservableCollection<MusicTrack> DisplayedTracks
	{
		get
		{
			if (IsSearching)
			{
				return SearchResults;
			}
			return CurrentView switch
			{
				"recent" => RecentPlayTracks, 
				"recent_add" => RecentAddTracks, 
				"most_played" => MostPlayedTracks, 
				"favorites" => FavoriteTracks, 
				"folder_detail" => FolderDetailTracks, 
				_ => Tracks, 
			};
		}
	}

	public bool HasNoSearchResults
	{
		get
		{
			bool flag = IsSearching && !string.IsNullOrWhiteSpace(SearchQuery);
			if (flag)
			{
				flag = SelectedLibraryTab switch
				{
					"artists" => FilteredArtists.Count == 0, 
					"albums" => FilteredAlbums.Count == 0, 
					"genres" => FilteredGenres.Count == 0, 
					_ => SearchResults.Count == 0, 
				};
			}
			return flag;
		}
	}

	public bool HasNoMusic
	{
		get
		{
			if (!IsSearching)
			{
				return DisplayedTracks.Count == 0;
			}
			return false;
		}
	}

	public BulkObservableCollection<HiddenFolder> HiddenFoldersList { get; } = new BulkObservableCollection<HiddenFolder>();

	public BulkObservableCollection<MusicTrack> HiddenTracks { get; } = new BulkObservableCollection<MusicTrack>();

	public BulkObservableCollection<string> ScanFolderList { get; } = new BulkObservableCollection<string>();


	public BulkObservableCollection<MusicTrack> DuplicateTracks { get; } = new BulkObservableCollection<MusicTrack>();


	public BulkObservableCollection<MusicTrack> DeletedTracks { get; } = new BulkObservableCollection<MusicTrack>();


	public BulkObservableCollection<MusicTrack> MusicSelectList { get; } = new BulkObservableCollection<MusicTrack>();


	public BulkObservableCollection<PlaylistViewModel> PlaylistSelectList { get; } = new BulkObservableCollection<PlaylistViewModel>();


	public BulkObservableCollection<string> LrcLocalFiles { get; } = new BulkObservableCollection<string>();


	public BulkObservableCollection<string> LrcFilteredFiles { get; } = new BulkObservableCollection<string>();


	public BulkObservableCollection<string> LrcFolderNames { get; } = new BulkObservableCollection<string>();


	public ObservableCollection<string> LrcSearchResults { get; } = new ObservableCollection<string>();


	public ObservableCollection<string> ThemePresets { get; } = new ObservableCollection<string> { "Dark", "Light", "Midnight Blue", "Forest", "Sunset", "Custom" };
	public BulkObservableCollection<WallpaperItem> Wallpapers { get; } = new BulkObservableCollection<WallpaperItem>();
	public BulkObservableCollection<string> WallpaperCategories { get; } = new BulkObservableCollection<string> { "All" };
	public string SelectedWallpaperCategory
	{
		get => _selectedWallpaperCategory;
		set
		{
			if (!EqualityComparer<string>.Default.Equals(_selectedWallpaperCategory, value))
			{
				OnPropertyChanging("SelectedWallpaperCategory");
				_selectedWallpaperCategory = value;
				OnPropertyChanged("SelectedWallpaperCategory");
				FilterWallpapers();
			}
		}
	}
	public string BackgroundImagePath
	{
		get => _backgroundImagePath;
		set
		{
			if (!EqualityComparer<string>.Default.Equals(_backgroundImagePath, value))
			{
				OnPropertyChanging("BackgroundImagePath");
				_backgroundImagePath = value;
				OnPropertyChanged("BackgroundImagePath");
			}
		}
	}

	public double WallpaperBlur
	{
		get => _wallpaperBlur;
		set
		{
			if (Math.Abs(_wallpaperBlur - value) > 0.01)
			{
				OnPropertyChanging(nameof(WallpaperBlur));
				_wallpaperBlur = value;
				OnPropertyChanged(nameof(WallpaperBlur));
			}
		}
	}

	public double WallpaperAlpha
	{
		get => _wallpaperAlpha;
		set
		{
			if (Math.Abs(_wallpaperAlpha - value) > 0.01)
			{
				OnPropertyChanging(nameof(WallpaperAlpha));
				_wallpaperAlpha = value;
				OnPropertyChanged(nameof(WallpaperAlpha));
				OnPropertyChanged(nameof(WallpaperAlphaOpacity));
			}
		}
	}

	public double WallpaperAlphaOpacity => Math.Clamp(_wallpaperAlpha / 255.0, 0.0, 1.0);

	public string ThemeEditWallpaperPath
	{
		get => _themeEditWallpaperPath;
		set
		{
			if (!EqualityComparer<string>.Default.Equals(_themeEditWallpaperPath, value))
			{
				OnPropertyChanging(nameof(ThemeEditWallpaperPath));
				_themeEditWallpaperPath = value;
				OnPropertyChanged(nameof(ThemeEditWallpaperPath));
				RebakeThemePreview();
			}
		}
	}

	public double ThemeEditBlur
	{
		get => _themeEditBlur;
		set
		{
			if (Math.Abs(_themeEditBlur - value) > 0.01)
			{
				OnPropertyChanging(nameof(ThemeEditBlur));
				_themeEditBlur = value;
				// Coalesce preview notifications while dragging: the editor preview
				// re-blurs per tick. The getter stays live (slider thumb + save both
				// read the field), only the broadcast is throttled to ~8Hz.
				if (_themeEditBlurTimer == null)
				{
					_themeEditBlurTimer = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromMilliseconds(120) };
					_themeEditBlurTimer.Tick += (_, _) =>
					{
						_themeEditBlurTimer?.Stop();
						PublishThemeEditBlur();
					};
					PublishThemeEditBlur();
					_themeEditBlurTimer.Start();
				}
				else if (!_themeEditBlurTimer.IsEnabled)
				{
					PublishThemeEditBlur();
					_themeEditBlurTimer.Start();
				}
			}
		}
	}

	private void PublishThemeEditBlur()
	{
		OnPropertyChanged(nameof(ThemeEditBlur));
		RebakeThemePreview();
	}

	private System.Windows.Media.ImageSource? _themePreviewImage;
	public System.Windows.Media.ImageSource? ThemePreviewImage
	{
		get => _themePreviewImage;
		private set
		{
			if (!ReferenceEquals(_themePreviewImage, value))
			{
				_themePreviewImage = value;
				OnPropertyChanged(nameof(ThemePreviewImage));
			}
		}
	}

	// Rebakes the editor preview at half resolution (~336x189, upscaled on
	// display): ~4x cheaper per refresh than re-rendering the live 672px blur
	// effect, visually identical for a blurred preview. Runs on the throttled
	// ticks (and path changes), never per-frame. Any failure keeps the previous
	// frame — never blanks the preview.
	private void RebakeThemePreview()
	{
		try
		{
			string? path = ThemeEditWallpaperPath;
			if (string.IsNullOrWhiteSpace(path)) { ThemePreviewImage = null; return; }
			if (Converters.WallpaperThumbnailConverter.IsCached(path, 700))
			{
				FinishRebake(path);
				return;
			}
			// Cold first view: decode off the UI thread so entry never stalls on a
			// synchronous JPEG decode; the baked frame lands (path-guarded) inside
			// the entry zoom instead. Previous frame (if any) stays meanwhile.
			string wantPath = path;
			double wantBlur = _themeEditBlur;
			Task.Run(() =>
			{
				try
				{
					var thumb = Converters.WallpaperThumbnailConverter.GetThumbnail(wantPath, 700);
					if (thumb == null) return;
					Dispatch(delegate
					{
						if (!string.Equals(ThemeEditWallpaperPath, wantPath, StringComparison.OrdinalIgnoreCase)) return;
						if (Math.Abs(_themeEditBlur - wantBlur) > 0.01 && wantBlur >= 0.5)
						{
							RebakeThemePreview();
							return;
						}
						FinishRebake(wantPath);
					});
				}
				catch { }
			});
		}
		catch { }
	}

	private void FinishRebake(string path)
	{
		try
		{
			var thumb = Converters.WallpaperThumbnailConverter.GetThumbnail(path, 700);
			if (thumb == null) return;
			double blur = _themeEditBlur;
			if (blur < 0.5) { ThemePreviewImage = thumb; return; }
			const int w = 336, h = 189;
			var target = new System.Windows.Media.Imaging.RenderTargetBitmap(w, h, 96, 96, System.Windows.Media.PixelFormats.Pbgra32);
			var visual = new System.Windows.Media.DrawingVisual();
			using (var ctx = visual.RenderOpen())
			{
				var ib = new System.Windows.Media.ImageBrush(thumb) { Stretch = System.Windows.Media.Stretch.UniformToFill };
				ctx.DrawRectangle(ib, null, new System.Windows.Rect(0, 0, w, h));
			}
			target.Render(visual);
			var blurred = new System.Windows.Media.Effects.BlurEffect { Radius = System.Math.Max(2, blur / 2), RenderingBias = System.Windows.Media.Effects.RenderingBias.Performance };
			var wrapper = new System.Windows.Controls.Image { Source = target, Effect = blurred };
			wrapper.Measure(new System.Windows.Size(w, h));
			wrapper.Arrange(new System.Windows.Rect(0, 0, w, h));
			var result = new System.Windows.Media.Imaging.RenderTargetBitmap(w, h, 96, 96, System.Windows.Media.PixelFormats.Pbgra32);
			result.Render(wrapper);
			result.Freeze();
			ThemePreviewImage = result;
		}
		catch { }
	}

	public double ThemeEditAlpha
	{
		get => _themeEditAlpha;
		set
		{
			if (Math.Abs(_themeEditAlpha - value) > 0.01)
			{
				OnPropertyChanging(nameof(ThemeEditAlpha));
				_themeEditAlpha = value;
				OnPropertyChanged(nameof(ThemeEditAlpha));
				OnPropertyChanged(nameof(ThemeEditAlphaOpacity));
			}
		}
	}

	public double ThemeEditAlphaOpacity => Math.Clamp(_themeEditAlpha / 255.0, 0.0, 1.0);

	private RelayCommand? _chooseThemePictureCommand;
	public IRelayCommand ChooseThemePictureCommand => _chooseThemePictureCommand ??= new RelayCommand(ChooseThemePicture);

	private RelayCommand? _resetThemeEditCommand;
	public IRelayCommand ResetThemeEditCommand => _resetThemeEditCommand ??= new RelayCommand(ResetThemeEdit);

	private RelayCommand? _saveThemeEditCommand;
	public IRelayCommand SaveThemeEditCommand => _saveThemeEditCommand ??= new RelayCommand(SaveThemeEdit);

	private RelayCommand? _cancelThemeEditCommand;
	public IRelayCommand CancelThemeEditCommand => _cancelThemeEditCommand ??= new RelayCommand(CancelThemeEdit);

	public BulkObservableCollection<WallpaperItem> FilteredWallpapers { get; } = new BulkObservableCollection<WallpaperItem>();


	public List<(int Id, string Name)> OutputDevices => AudioEngine.GetOutputDevices();

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.ObservablePropertyGenerator", "8.4.0.0")]
	[ExcludeFromCodeCoverage]
	public string CurrentView
	{
		get
		{
			return _currentView;
		}
		[MemberNotNull("_currentView")]
		set
		{
			if (!EqualityComparer<string>.Default.Equals(_currentView, value))
			{
				OnPropertyChanging("CurrentView");
				_currentView = value;
				OnCurrentViewChanged(value);
				OnPropertyChanged("CurrentView");
			}
		}
	}

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.ObservablePropertyGenerator", "8.4.0.0")]
	[ExcludeFromCodeCoverage]
	public bool IsDrawerOpen
	{
		get
		{
			return _isDrawerOpen;
		}
		set
		{
			if (!EqualityComparer<bool>.Default.Equals(_isDrawerOpen, value))
			{
				OnPropertyChanging("IsDrawerOpen");
				_isDrawerOpen = value;
				OnPropertyChanged("IsDrawerOpen");
			}
		}
	}

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.ObservablePropertyGenerator", "8.4.0.0")]
	[ExcludeFromCodeCoverage]
	public bool ShowMiniPlayer
	{
		get
		{
			return _showMiniPlayer;
		}
		set
		{
			if (!EqualityComparer<bool>.Default.Equals(_showMiniPlayer, value))
			{
				OnPropertyChanging("ShowMiniPlayer");
				_showMiniPlayer = value;
				OnPropertyChanged("ShowMiniPlayer");
			}
		}
	}

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.ObservablePropertyGenerator", "8.4.0.0")]
	[ExcludeFromCodeCoverage]
	public bool ShowFullPlayer
	{
		get
		{
			return _showFullPlayer;
		}
		set
		{
			if (!EqualityComparer<bool>.Default.Equals(_showFullPlayer, value))
			{
				OnPropertyChanging("ShowFullPlayer");
				_showFullPlayer = value;
				OnPropertyChanged("ShowFullPlayer");
			}
		}
	}

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.ObservablePropertyGenerator", "8.4.0.0")]
	[ExcludeFromCodeCoverage]
	public bool ShowEqualizer
	{
		get
		{
			return _showEqualizer;
		}
		set
		{
			if (!EqualityComparer<bool>.Default.Equals(_showEqualizer, value))
			{
				OnPropertyChanging("ShowEqualizer");
				_showEqualizer = value;
				OnPropertyChanged("ShowEqualizer");
			}
		}
	}

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.ObservablePropertyGenerator", "8.4.0.0")]
	[ExcludeFromCodeCoverage]
	public bool ShowSoundEffects
	{
		get
		{
			return _showSoundEffects;
		}
		set
		{
			if (!EqualityComparer<bool>.Default.Equals(_showSoundEffects, value))
			{
				OnPropertyChanging("ShowSoundEffects");
				_showSoundEffects = value;
				OnPropertyChanged("ShowSoundEffects");
			}
		}
	}

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.ObservablePropertyGenerator", "8.4.0.0")]
	[ExcludeFromCodeCoverage]
	public bool ShowSpeedPitch
	{
		get
		{
			return _showSpeedPitch;
		}
		set
		{
			if (!EqualityComparer<bool>.Default.Equals(_showSpeedPitch, value))
			{
				OnPropertyChanging("ShowSpeedPitch");
				_showSpeedPitch = value;
				OnPropertyChanged("ShowSpeedPitch");
			}
		}
	}

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.ObservablePropertyGenerator", "8.4.0.0")]
	[ExcludeFromCodeCoverage]
	public bool ShowLyrics
	{
		get
		{
			return _showLyrics;
		}
		set
		{
			if (!EqualityComparer<bool>.Default.Equals(_showLyrics, value))
			{
				OnPropertyChanging("ShowLyrics");
				_showLyrics = value;
				OnPropertyChanged("ShowLyrics");
			}
		}
	}

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.ObservablePropertyGenerator", "8.4.0.0")]
	[ExcludeFromCodeCoverage]
	public bool ShowSettings
	{
		get
		{
			return _showSettings;
		}
		set
		{
			if (!EqualityComparer<bool>.Default.Equals(_showSettings, value))
			{
				OnPropertyChanging("ShowSettings");
				_showSettings = value;
				OnPropertyChanged("ShowSettings");
			}
		}
	}

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.ObservablePropertyGenerator", "8.4.0.0")]
	[ExcludeFromCodeCoverage]
	public bool ShowQueue
	{
		get
		{
			return _showQueue;
		}
		set
		{
			if (!EqualityComparer<bool>.Default.Equals(_showQueue, value))
			{
				OnPropertyChanging("ShowQueue");
				_showQueue = value;
				OnPropertyChanged("ShowQueue");
			}
		}
	}

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.ObservablePropertyGenerator", "8.4.0.0")]
	[ExcludeFromCodeCoverage]
	public bool ShowPlaylistDetail
	{
		get
		{
			return _showPlaylistDetail;
		}
		set
		{
			if (!EqualityComparer<bool>.Default.Equals(_showPlaylistDetail, value))
			{
				OnPropertyChanging("ShowPlaylistDetail");
				_showPlaylistDetail = value;
				OnPropertyChanged("ShowPlaylistDetail");
			}
		}
	}

	public bool ShowNewPlaylistOverlay
	{
		get => _showNewPlaylistOverlay;
		set
		{
			if (!EqualityComparer<bool>.Default.Equals(_showNewPlaylistOverlay, value))
			{
				OnPropertyChanging("ShowNewPlaylistOverlay");
				_showNewPlaylistOverlay = value;
				OnPropertyChanged("ShowNewPlaylistOverlay");
			}
		}
	}

	public string NewPlaylistName
	{
		get => _newPlaylistName;
		set
		{
			if (!EqualityComparer<string>.Default.Equals(_newPlaylistName, value))
			{
				OnPropertyChanging("NewPlaylistName");
				_newPlaylistName = value;
				OnPropertyChanged("NewPlaylistName");
			}
		}
	}

	public bool ShowRenamePlaylistOverlay
	{
		get => _showRenamePlaylistOverlay;
		set
		{
			if (!EqualityComparer<bool>.Default.Equals(_showRenamePlaylistOverlay, value))
			{
				OnPropertyChanging("ShowRenamePlaylistOverlay");
				_showRenamePlaylistOverlay = value;
				OnPropertyChanged("ShowRenamePlaylistOverlay");
			}
		}
	}

	public string RenamePlaylistName
	{
		get => _renamePlaylistName;
		set
		{
			if (!EqualityComparer<string>.Default.Equals(_renamePlaylistName, value))
			{
				OnPropertyChanging("RenamePlaylistName");
				_renamePlaylistName = value;
				OnPropertyChanged("RenamePlaylistName");
			}
		}
	}

	public PlaylistViewModel? RenamingPlaylist
	{
		get => _renamingPlaylist;
		set
		{
			if (!EqualityComparer<PlaylistViewModel?>.Default.Equals(_renamingPlaylist, value))
			{
				OnPropertyChanging("RenamingPlaylist");
				_renamingPlaylist = value;
				OnPropertyChanged("RenamingPlaylist");
			}
		}
	}

	public string StatusBarMessage
	{
		get => _statusBarMessage;
		set
		{
			if (!EqualityComparer<string>.Default.Equals(_statusBarMessage, value))
			{
				OnPropertyChanging("StatusBarMessage");
				_statusBarMessage = value;
				OnPropertyChanged("StatusBarMessage");
			}
		}
	}

	public bool IsStatusBarVisible
	{
		get => _isStatusBarVisible;
		set
		{
			if (!EqualityComparer<bool>.Default.Equals(_isStatusBarVisible, value))
			{
				OnPropertyChanging("IsStatusBarVisible");
				_isStatusBarVisible = value;
				OnPropertyChanged("IsStatusBarVisible");
			}
		}
	}

	public bool IsStatusBarLoading
	{
		get => _isStatusBarLoading;
		set
		{
			if (_isStatusBarLoading == value) return;
			_isStatusBarLoading = value;
			OnPropertyChanged(nameof(IsStatusBarLoading));
		}
	}

	public void ShowStatusBarLoading(string message)
	{
		StatusBarMessage = message;
		IsStatusBarHiddenLinkVisible = false;
		IsStatusBarLoading = true;
		IsStatusBarVisible = true;
	}

	public void ShowStatusBar(string message, int durationMs = 2500)
	{
		StatusBarMessage = message;
		IsStatusBarHiddenLinkVisible = false;
		IsStatusBarLoading = false;
		IsStatusBarVisible = true;
		var timer = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromMilliseconds(durationMs) };
		timer.Tick += (_, _) =>
		{
			timer.Stop();
			if (!IsStatusBarLoading)
			{
				IsStatusBarVisible = false;
				IsStatusBarHiddenLinkVisible = false;
			}
		};
		timer.Start();
	}

	public bool IsStatusBarHiddenLinkVisible
	{
		get => _isStatusBarHiddenLinkVisible;
		set
		{
			if (_isStatusBarHiddenLinkVisible == value) return;
			_isStatusBarHiddenLinkVisible = value;
			OnPropertyChanged(nameof(IsStatusBarHiddenLinkVisible));
		}
	}

	public void ShowHiddenSuccessBar()
	{
		StatusBarMessage = "Hidden successfully! You can manage your music in ";
		IsStatusBarHiddenLinkVisible = true;
		IsStatusBarVisible = true;
		var timer = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromMilliseconds(4000) };
		timer.Tick += (_, _) =>
		{
			timer.Stop();
			IsStatusBarVisible = false;
			IsStatusBarHiddenLinkVisible = false;
		};
		timer.Start();
	}

	public int CurrentPlaylistId
	{
		get => _currentPlaylistId;
		set
		{
			if (!EqualityComparer<int>.Default.Equals(_currentPlaylistId, value))
			{
				OnPropertyChanging("CurrentPlaylistId");
				_currentPlaylistId = value;
				OnPropertyChanged("CurrentPlaylistId");
			}
		}
	}

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.ObservablePropertyGenerator", "8.4.0.0")]
	[ExcludeFromCodeCoverage]
	public Playlist? CurrentPlaylist
	{
		get
		{
			return _currentPlaylist;
		}
		set
		{
			if (!EqualityComparer<Playlist>.Default.Equals(_currentPlaylist, value))
			{
				OnPropertyChanging("CurrentPlaylist");
				_currentPlaylist = value;
				OnPropertyChanged("CurrentPlaylist");
			}
		}
	}

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.ObservablePropertyGenerator", "8.4.0.0")]
	[ExcludeFromCodeCoverage]
	public string SearchQuery
	{
		get
		{
			return _searchQuery;
		}
		[MemberNotNull("_searchQuery")]
		set
		{
			if (!EqualityComparer<string>.Default.Equals(_searchQuery, value))
			{
				OnPropertyChanging("SearchQuery");
				_searchQuery = value;
				OnSearchQueryChanged(value);
				OnPropertyChanged("SearchQuery");
			}
		}
	}

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.ObservablePropertyGenerator", "8.4.0.0")]
	[ExcludeFromCodeCoverage]
	public bool IsSearching
	{
		get
		{
			return _isSearching;
		}
		set
		{
			if (!EqualityComparer<bool>.Default.Equals(_isSearching, value))
			{
				OnPropertyChanging("IsSearching");
				_isSearching = value;
				OnIsSearchingChanged(value);
				OnPropertyChanged("IsSearching");
			}
		}
	}

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.ObservablePropertyGenerator", "8.4.0.0")]
	[ExcludeFromCodeCoverage]
	public bool IsWindowMaximized
	{
		get
		{
			return _isWindowMaximized;
		}
		set
		{
			if (!EqualityComparer<bool>.Default.Equals(_isWindowMaximized, value))
			{
				OnPropertyChanging("IsWindowMaximized");
				_isWindowMaximized = value;
				OnPropertyChanged("IsWindowMaximized");
			}
		}
	}

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.ObservablePropertyGenerator", "8.4.0.0")]
	[ExcludeFromCodeCoverage]
	public string LibrarySortOrder
	{
		get
		{
			return _librarySortOrder;
		}
		[MemberNotNull("_librarySortOrder")]
		set
		{
			if (!EqualityComparer<string>.Default.Equals(_librarySortOrder, value))
			{
				OnPropertyChanging("LibrarySortOrder");
				_librarySortOrder = value;
				OnPropertyChanged("LibrarySortOrder");
			}
		}
	}

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.ObservablePropertyGenerator", "8.4.0.0")]
	[ExcludeFromCodeCoverage]
	public bool IsLibrarySortDescending
	{
		get
		{
			return _isLibrarySortDescending;
		}
		set
		{
			if (!EqualityComparer<bool>.Default.Equals(_isLibrarySortDescending, value))
			{
				OnPropertyChanging("IsLibrarySortDescending");
				_isLibrarySortDescending = value;
				OnPropertyChanged("IsLibrarySortDescending");
			}
		}
	}

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.ObservablePropertyGenerator", "8.4.0.0")]
	[ExcludeFromCodeCoverage]
	public string SelectedLibraryTab
	{
		get
		{
			return _selectedLibraryTab;
		}
		[MemberNotNull("_selectedLibraryTab")]
		set
		{
			if (!EqualityComparer<string>.Default.Equals(_selectedLibraryTab, value))
			{
				OnPropertyChanging("SelectedLibraryTab");
				_selectedLibraryTab = value;
				OnPropertyChanged("SelectedLibraryTab");
			}
		}
	}

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.ObservablePropertyGenerator", "8.4.0.0")]
	[ExcludeFromCodeCoverage]
	public bool IsSelectMode
	{
		get
		{
			return _isSelectMode;
		}
		set
		{
			if (!EqualityComparer<bool>.Default.Equals(_isSelectMode, value))
			{
				OnPropertyChanging("IsSelectMode");
				_isSelectMode = value;
				OnPropertyChanged("IsSelectMode");
			}
		}
	}

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.ObservablePropertyGenerator", "8.4.0.0")]
	[ExcludeFromCodeCoverage]
	public bool IsSongsSectionCollapsed
	{
		get
		{
			return _isSongsSectionCollapsed;
		}
		set
		{
			if (!EqualityComparer<bool>.Default.Equals(_isSongsSectionCollapsed, value))
			{
				OnPropertyChanging("IsSongsSectionCollapsed");
				_isSongsSectionCollapsed = value;
				OnPropertyChanged("IsSongsSectionCollapsed");
			}
		}
	}

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.ObservablePropertyGenerator", "8.4.0.0")]
	[ExcludeFromCodeCoverage]
	public bool ShowTrackBottomSheet
	{
		get
		{
			return _showTrackBottomSheet;
		}
		set
		{
			if (!EqualityComparer<bool>.Default.Equals(_showTrackBottomSheet, value))
			{
				OnPropertyChanging("ShowTrackBottomSheet");
				_showTrackBottomSheet = value;
				OnPropertyChanged("ShowTrackBottomSheet");
			}
		}
	}

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.ObservablePropertyGenerator", "8.4.0.0")]
	[ExcludeFromCodeCoverage]
	public MusicTrack? BottomSheetTrack
	{
		get
		{
			return _bottomSheetTrack;
		}
		set
		{
			if (!EqualityComparer<MusicTrack>.Default.Equals(_bottomSheetTrack, value))
			{
				OnPropertyChanging("BottomSheetTrack");
				_bottomSheetTrack = value;
				OnPropertyChanged("BottomSheetTrack");
			}
		}
	}

	public List<MusicTrack>? PendingTracksToAdd { get; set; }

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.ObservablePropertyGenerator", "8.4.0.0")]
	[ExcludeFromCodeCoverage]
	public bool ShowTrackInfo
	{
		get
		{
			return _showTrackInfo;
		}
		set
		{
			if (!EqualityComparer<bool>.Default.Equals(_showTrackInfo, value))
			{
				OnPropertyChanging("ShowTrackInfo");
				_showTrackInfo = value;
				OnPropertyChanged("ShowTrackInfo");
			}
		}
	}

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.ObservablePropertyGenerator", "8.4.0.0")]
	[ExcludeFromCodeCoverage]
	public bool ShowSortMenu
	{
		get
		{
			return _showSortMenu;
		}
		set
		{
			if (!EqualityComparer<bool>.Default.Equals(_showSortMenu, value))
			{
				OnPropertyChanging("ShowSortMenu");
				_showSortMenu = value;
				OnPropertyChanged("ShowSortMenu");
			}
		}
	}

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.ObservablePropertyGenerator", "8.4.0.0")]
	[ExcludeFromCodeCoverage]
	public bool ShowOverflowMenu
	{
		get
		{
			return _showOverflowMenu;
		}
		set
		{
			if (!EqualityComparer<bool>.Default.Equals(_showOverflowMenu, value))
			{
				OnPropertyChanging("ShowOverflowMenu");
				_showOverflowMenu = value;
				OnPropertyChanged("ShowOverflowMenu");
			}
		}
	}

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.ObservablePropertyGenerator", "8.4.0.0")]
	[ExcludeFromCodeCoverage]
	public bool ShowLyricsSettings
	{
		get
		{
			return _showLyricsSettings;
		}
		set
		{
			if (!EqualityComparer<bool>.Default.Equals(_showLyricsSettings, value))
			{
				OnPropertyChanging("ShowLyricsSettings");
				_showLyricsSettings = value;
				OnPropertyChanged("ShowLyricsSettings");
			}
		}
	}

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.ObservablePropertyGenerator", "8.4.0.0")]
	[ExcludeFromCodeCoverage]
	public bool ShowLyricsSpeedPanel
	{
		get
		{
			return _showLyricsSpeedPanel;
		}
		set
		{
			if (!EqualityComparer<bool>.Default.Equals(_showLyricsSpeedPanel, value))
			{
				OnPropertyChanging("ShowLyricsSpeedPanel");
				_showLyricsSpeedPanel = value;
				OnPropertyChanged("ShowLyricsSpeedPanel");
			}
		}
	}

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.ObservablePropertyGenerator", "8.4.0.0")]
	[ExcludeFromCodeCoverage]
	public MusicTrack? CurrentTrack
	{
		get
		{
			return _currentTrack;
		}
		set
		{
			if (!EqualityComparer<MusicTrack>.Default.Equals(_currentTrack, value))
			{
				OnPropertyChanging("CurrentTrack");
				_currentTrack = value;
				OnCurrentTrackChanged(value);
				OnPropertyChanged("CurrentTrack");
			}
		}
	}

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.ObservablePropertyGenerator", "8.4.0.0")]
	[ExcludeFromCodeCoverage]
	public bool IsPlaying
	{
		get
		{
			return _isPlaying;
		}
		set
		{
			if (!EqualityComparer<bool>.Default.Equals(_isPlaying, value))
			{
				OnPropertyChanging("IsPlaying");
				_isPlaying = value;
				OnPropertyChanged("IsPlaying");
			}
		}
	}

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.ObservablePropertyGenerator", "8.4.0.0")]
	[ExcludeFromCodeCoverage]
	public TimeSpan Position
	{
		get
		{
			return _position;
		}
		set
		{
			if (!EqualityComparer<TimeSpan>.Default.Equals(_position, value))
			{
				OnPropertyChanging("Position");
				_position = value;
				OnPropertyChanged("Position");
			}
		}
	}

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.ObservablePropertyGenerator", "8.4.0.0")]
	[ExcludeFromCodeCoverage]
	public TimeSpan Duration
	{
		get
		{
			return _duration;
		}
		set
		{
			if (!EqualityComparer<TimeSpan>.Default.Equals(_duration, value))
			{
				OnPropertyChanging("Duration");
				_duration = value;
				OnPropertyChanged("Duration");
			}
		}
	}

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.ObservablePropertyGenerator", "8.4.0.0")]
	[ExcludeFromCodeCoverage]
	public float Volume
	{
		get
		{
			return _volume;
		}
		set
		{
			if (!EqualityComparer<float>.Default.Equals(_volume, value))
			{
				OnPropertyChanging("Volume");
				_volume = value;
				OnVolumeChanged(value);
				OnPropertyChanged("Volume");
			}
		}
	}

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.ObservablePropertyGenerator", "8.4.0.0")]
	[ExcludeFromCodeCoverage]
	public float Balance
	{
		get
		{
			return _balance;
		}
		set
		{
			if (!EqualityComparer<float>.Default.Equals(_balance, value))
			{
				OnPropertyChanging("Balance");
				_balance = value;
				OnBalanceChanged(value);
				OnPropertyChanged("Balance");
			}
		}
	}

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.ObservablePropertyGenerator", "8.4.0.0")]
	[ExcludeFromCodeCoverage]
	public float Speed
	{
		get
		{
			return _speed;
		}
		set
		{
			if (!EqualityComparer<float>.Default.Equals(_speed, value))
			{
				OnPropertyChanging("Speed");
				_speed = value;
				OnSpeedChanged(value);
				OnPropertyChanged("Speed");
			}
		}
	}

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.ObservablePropertyGenerator", "8.4.0.0")]
	[ExcludeFromCodeCoverage]
	public float Pitch
	{
		get
		{
			return _pitch;
		}
		set
		{
			if (!EqualityComparer<float>.Default.Equals(_pitch, value))
			{
				OnPropertyChanging("Pitch");
				_pitch = value;
				OnPitchChanged(value);
				OnPropertyChanged("Pitch");
			}
		}
	}

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.ObservablePropertyGenerator", "8.4.0.0")]
	[ExcludeFromCodeCoverage]
	public bool IsFavorite
	{
		get
		{
			return _isFavorite;
		}
		set
		{
			if (!EqualityComparer<bool>.Default.Equals(_isFavorite, value))
			{
				OnPropertyChanging("IsFavorite");
				_isFavorite = value;
				OnPropertyChanged("IsFavorite");
			}
		}
	}

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.ObservablePropertyGenerator", "8.4.0.0")]
	[ExcludeFromCodeCoverage]
	public bool ShuffleEnabled
	{
		get
		{
			return _shuffleEnabled;
		}
		set
		{
			if (!EqualityComparer<bool>.Default.Equals(_shuffleEnabled, value))
			{
				OnPropertyChanging("ShuffleEnabled");
				_shuffleEnabled = value;
				OnPropertyChanged("ShuffleEnabled");
			}
		}
	}

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.ObservablePropertyGenerator", "8.4.0.0")]
	[ExcludeFromCodeCoverage]
	public PlayMode PlayMode
	{
		get
		{
			return _playMode;
		}
		set
		{
			if (!EqualityComparer<PlayMode>.Default.Equals(_playMode, value))
			{
				OnPropertyChanging("PlayMode");
				_playMode = value;
				OnPropertyChanged("PlayMode");
			}
		}
	}

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.ObservablePropertyGenerator", "8.4.0.0")]
	[ExcludeFromCodeCoverage]
	public CurrentlyPlayingPage CurrentlyPlayingPage
	{
		get
		{
			return _currentlyPlayingPage;
		}
		set
		{
			if (!EqualityComparer<CurrentlyPlayingPage>.Default.Equals(_currentlyPlayingPage, value))
			{
				OnPropertyChanging("CurrentlyPlayingPage");
				_currentlyPlayingPage = value;
				OnCurrentlyPlayingPageChanged(value);
				OnPropertyChanged("CurrentlyPlayingPage");
			}
		}
	}

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.ObservablePropertyGenerator", "8.4.0.0")]
	[ExcludeFromCodeCoverage]
	public string CurrentLyricText
	{
		get
		{
			return _currentLyricText;
		}
		[MemberNotNull("_currentLyricText")]
		set
		{
			if (!EqualityComparer<string>.Default.Equals(_currentLyricText, value))
			{
				OnPropertyChanging("CurrentLyricText");
				_currentLyricText = value;
				OnPropertyChanged("CurrentLyricText");
			}
		}
	}

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.ObservablePropertyGenerator", "8.4.0.0")]
	[ExcludeFromCodeCoverage]
	public int CurrentLyricIndex
	{
		get
		{
			return _currentLyricIndex;
		}
		set
		{
			if (!EqualityComparer<int>.Default.Equals(_currentLyricIndex, value))
			{
				OnPropertyChanging("CurrentLyricIndex");
				_currentLyricIndex = value;
				OnPropertyChanged("CurrentLyricIndex");
			}
		}
	}

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.ObservablePropertyGenerator", "8.4.0.0")]
	[ExcludeFromCodeCoverage]
	public string LyricsSourcePath
	{
		get
		{
			return _lyricsSourcePath;
		}
		[MemberNotNull("_lyricsSourcePath")]
		set
		{
			if (!EqualityComparer<string>.Default.Equals(_lyricsSourcePath, value))
			{
				OnPropertyChanging("LyricsSourcePath");
				_lyricsSourcePath = value;
				OnPropertyChanged("LyricsSourcePath");
			}
		}
	}

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.ObservablePropertyGenerator", "8.4.0.0")]
	[ExcludeFromCodeCoverage]
	public bool IsScanning
	{
		get
		{
			return _isScanning;
		}
		set
		{
			if (!EqualityComparer<bool>.Default.Equals(_isScanning, value))
			{
				OnPropertyChanging("IsScanning");
				_isScanning = value;
				OnPropertyChanged("IsScanning");
			}
		}
	}

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.ObservablePropertyGenerator", "8.4.0.0")]
	[ExcludeFromCodeCoverage]
	public bool IsInitializing
	{
		get
		{
			return _isInitializing;
		}
		set
		{
			if (!EqualityComparer<bool>.Default.Equals(_isInitializing, value))
			{
				OnPropertyChanging("IsInitializing");
				_isInitializing = value;
				OnPropertyChanged("IsInitializing");
			}
		}
	}

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.ObservablePropertyGenerator", "8.4.0.0")]
	[ExcludeFromCodeCoverage]
	public int ScanProgress
	{
		get
		{
			return _scanProgress;
		}
		set
		{
			if (!EqualityComparer<int>.Default.Equals(_scanProgress, value))
			{
				OnPropertyChanging("ScanProgress");
				_scanProgress = value;
				OnPropertyChanged("ScanProgress");
			}
		}
	}

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.ObservablePropertyGenerator", "8.4.0.0")]
	[ExcludeFromCodeCoverage]
	public string ScanStatus
	{
		get
		{
			return _scanStatus;
		}
		[MemberNotNull("_scanStatus")]
		set
		{
			if (!EqualityComparer<string>.Default.Equals(_scanStatus, value))
			{
				OnPropertyChanging("ScanStatus");
				_scanStatus = value;
				OnPropertyChanged("ScanStatus");
			}
		}
	}

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.ObservablePropertyGenerator", "8.4.0.0")]
	[ExcludeFromCodeCoverage]
	public EqPreset CurrentEqPreset
	{
		get
		{
			return _currentEqPreset;
		}
		[MemberNotNull("_currentEqPreset")]
		set
		{
			if (!EqualityComparer<EqPreset>.Default.Equals(_currentEqPreset, value))
			{
				OnPropertyChanging("CurrentEqPreset");
				_currentEqPreset = value;
				OnCurrentEqPresetChanged(value);
				OnPropertyChanged("CurrentEqPreset");
			}
		}
	}

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.ObservablePropertyGenerator", "8.4.0.0")]
	[ExcludeFromCodeCoverage]
	public bool EqEnabled
	{
		get
		{
			return _eqEnabled;
		}
		set
		{
			if (!EqualityComparer<bool>.Default.Equals(_eqEnabled, value))
			{
				OnPropertyChanging("EqEnabled");
				_eqEnabled = value;
				OnEqEnabledChanged(value);
				OnPropertyChanged("EqEnabled");
			}
		}
	}

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.ObservablePropertyGenerator", "8.4.0.0")]
	[ExcludeFromCodeCoverage]
	public float Band0
	{
		get
		{
			return _band0;
		}
		set
		{
			if (!EqualityComparer<float>.Default.Equals(_band0, value))
			{
				OnPropertyChanging("Band0");
				_band0 = value;
				OnBand0Changed(value);
				OnPropertyChanged("Band0");
			}
		}
	}

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.ObservablePropertyGenerator", "8.4.0.0")]
	[ExcludeFromCodeCoverage]
	public float Band1
	{
		get
		{
			return _band1;
		}
		set
		{
			if (!EqualityComparer<float>.Default.Equals(_band1, value))
			{
				OnPropertyChanging("Band1");
				_band1 = value;
				OnBand1Changed(value);
				OnPropertyChanged("Band1");
			}
		}
	}

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.ObservablePropertyGenerator", "8.4.0.0")]
	[ExcludeFromCodeCoverage]
	public float Band2
	{
		get
		{
			return _band2;
		}
		set
		{
			if (!EqualityComparer<float>.Default.Equals(_band2, value))
			{
				OnPropertyChanging("Band2");
				_band2 = value;
				OnBand2Changed(value);
				OnPropertyChanged("Band2");
			}
		}
	}

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.ObservablePropertyGenerator", "8.4.0.0")]
	[ExcludeFromCodeCoverage]
	public float Band3
	{
		get
		{
			return _band3;
		}
		set
		{
			if (!EqualityComparer<float>.Default.Equals(_band3, value))
			{
				OnPropertyChanging("Band3");
				_band3 = value;
				OnBand3Changed(value);
				OnPropertyChanged("Band3");
			}
		}
	}

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.ObservablePropertyGenerator", "8.4.0.0")]
	[ExcludeFromCodeCoverage]
	public float Band4
	{
		get
		{
			return _band4;
		}
		set
		{
			if (!EqualityComparer<float>.Default.Equals(_band4, value))
			{
				OnPropertyChanging("Band4");
				_band4 = value;
				OnBand4Changed(value);
				OnPropertyChanged("Band4");
			}
		}
	}

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.ObservablePropertyGenerator", "8.4.0.0")]
	[ExcludeFromCodeCoverage]
	public float Band5
	{
		get
		{
			return _band5;
		}
		set
		{
			if (!EqualityComparer<float>.Default.Equals(_band5, value))
			{
				OnPropertyChanging("Band5");
				_band5 = value;
				OnBand5Changed(value);
				OnPropertyChanged("Band5");
			}
		}
	}

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.ObservablePropertyGenerator", "8.4.0.0")]
	[ExcludeFromCodeCoverage]
	public float Band6
	{
		get
		{
			return _band6;
		}
		set
		{
			if (!EqualityComparer<float>.Default.Equals(_band6, value))
			{
				OnPropertyChanging("Band6");
				_band6 = value;
				OnBand6Changed(value);
				OnPropertyChanged("Band6");
			}
		}
	}

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.ObservablePropertyGenerator", "8.4.0.0")]
	[ExcludeFromCodeCoverage]
	public float Band7
	{
		get
		{
			return _band7;
		}
		set
		{
			if (!EqualityComparer<float>.Default.Equals(_band7, value))
			{
				OnPropertyChanging("Band7");
				_band7 = value;
				OnBand7Changed(value);
				OnPropertyChanged("Band7");
			}
		}
	}

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.ObservablePropertyGenerator", "8.4.0.0")]
	[ExcludeFromCodeCoverage]
	public float Band8
	{
		get
		{
			return _band8;
		}
		set
		{
			if (!EqualityComparer<float>.Default.Equals(_band8, value))
			{
				OnPropertyChanging("Band8");
				_band8 = value;
				OnBand8Changed(value);
				OnPropertyChanged("Band8");
			}
		}
	}

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.ObservablePropertyGenerator", "8.4.0.0")]
	[ExcludeFromCodeCoverage]
	public float Band9
	{
		get
		{
			return _band9;
		}
		set
		{
			if (!EqualityComparer<float>.Default.Equals(_band9, value))
			{
				OnPropertyChanging("Band9");
				_band9 = value;
				OnBand9Changed(value);
				OnPropertyChanged("Band9");
			}
		}
	}

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.ObservablePropertyGenerator", "8.4.0.0")]
	[ExcludeFromCodeCoverage]
	public float BassBoostStrength
	{
		get
		{
			return _bassBoostStrength;
		}
		set
		{
			if (!EqualityComparer<float>.Default.Equals(_bassBoostStrength, value))
			{
				OnPropertyChanging("BassBoostStrength");
				_bassBoostStrength = value;
				OnBassBoostStrengthChanged(value);
				OnPropertyChanged("BassBoostStrength");
			}
		}
	}

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.ObservablePropertyGenerator", "8.4.0.0")]
	[ExcludeFromCodeCoverage]
	public bool BassBoostEnabled
	{
		get
		{
			return _bassBoostEnabled;
		}
		set
		{
			if (!EqualityComparer<bool>.Default.Equals(_bassBoostEnabled, value))
			{
				OnPropertyChanging("BassBoostEnabled");
				_bassBoostEnabled = value;
				OnBassBoostEnabledChanged(value);
				OnPropertyChanged("BassBoostEnabled");
			}
		}
	}

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.ObservablePropertyGenerator", "8.4.0.0")]
	[ExcludeFromCodeCoverage]
	public float VirtualizerStrength
	{
		get
		{
			return _virtualizerStrength;
		}
		set
		{
			if (!EqualityComparer<float>.Default.Equals(_virtualizerStrength, value))
			{
				OnPropertyChanging("VirtualizerStrength");
				_virtualizerStrength = value;
				OnVirtualizerStrengthChanged(value);
				OnPropertyChanged("VirtualizerStrength");
			}
		}
	}

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.ObservablePropertyGenerator", "8.4.0.0")]
	[ExcludeFromCodeCoverage]
	public bool VirtualizerEnabled
	{
		get
		{
			return _virtualizerEnabled;
		}
		set
		{
			if (!EqualityComparer<bool>.Default.Equals(_virtualizerEnabled, value))
			{
				OnPropertyChanging("VirtualizerEnabled");
				_virtualizerEnabled = value;
				OnVirtualizerEnabledChanged(value);
				OnPropertyChanged("VirtualizerEnabled");
			}
		}
	}

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.ObservablePropertyGenerator", "8.4.0.0")]
	[ExcludeFromCodeCoverage]
	public ReverbPreset ReverbPreset
	{
		get
		{
			return _reverbPreset;
		}
		set
		{
			if (!EqualityComparer<ReverbPreset>.Default.Equals(_reverbPreset, value))
			{
				OnPropertyChanging("ReverbPreset");
				_reverbPreset = value;
				OnReverbPresetChanged(value);
				OnPropertyChanged("ReverbPreset");
			}
		}
	}

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.ObservablePropertyGenerator", "8.4.0.0")]
	[ExcludeFromCodeCoverage]
	public bool ReverbEnabled
	{
		get
		{
			return _reverbEnabled;
		}
		set
		{
			if (!EqualityComparer<bool>.Default.Equals(_reverbEnabled, value))
			{
				OnPropertyChanging("ReverbEnabled");
				_reverbEnabled = value;
				OnReverbEnabledChanged(value);
				OnPropertyChanged("ReverbEnabled");
			}
		}
	}

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.ObservablePropertyGenerator", "8.4.0.0")]
	[ExcludeFromCodeCoverage]
	public bool LoudnessEnabled
	{
		get
		{
			return _loudnessEnabled;
		}
		set
		{
			if (!EqualityComparer<bool>.Default.Equals(_loudnessEnabled, value))
			{
				OnPropertyChanging("LoudnessEnabled");
				_loudnessEnabled = value;
				OnLoudnessEnabledChanged(value);
				OnPropertyChanged("LoudnessEnabled");
			}
		}
	}

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.ObservablePropertyGenerator", "8.4.0.0")]
	[ExcludeFromCodeCoverage]
	public float LoudnessGain
	{
		get
		{
			return _loudnessGain;
		}
		set
		{
			if (!EqualityComparer<float>.Default.Equals(_loudnessGain, value))
			{
				OnPropertyChanging("LoudnessGain");
				_loudnessGain = value;
				OnLoudnessGainChanged(value);
				OnPropertyChanged("LoudnessGain");
			}
		}
	}

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.ObservablePropertyGenerator", "8.4.0.0")]
	[ExcludeFromCodeCoverage]
	public int CurrentSoundEffect
	{
		get
		{
			return _currentSoundEffect;
		}
		set
		{
			if (!EqualityComparer<int>.Default.Equals(_currentSoundEffect, value))
			{
				OnPropertyChanging("CurrentSoundEffect");
				_currentSoundEffect = value;
				OnCurrentSoundEffectChanged(value);
				OnPropertyChanged("CurrentSoundEffect");
			}
		}
	}

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.ObservablePropertyGenerator", "8.4.0.0")]
	[ExcludeFromCodeCoverage]
	public float SoundEffectIntensity
	{
		get
		{
			return _soundEffectIntensity;
		}
		set
		{
			if (!EqualityComparer<float>.Default.Equals(_soundEffectIntensity, value))
			{
				OnPropertyChanging("SoundEffectIntensity");
				_soundEffectIntensity = value;
				OnPropertyChanged("SoundEffectIntensity");
			}
		}
	}

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.ObservablePropertyGenerator", "8.4.0.0")]
	[ExcludeFromCodeCoverage]
	public bool IsMagicSoundOn
	{
		get
		{
			return _isMagicSoundOn;
		}
		set
		{
			if (!EqualityComparer<bool>.Default.Equals(_isMagicSoundOn, value))
			{
				OnPropertyChanging("IsMagicSoundOn");
				_isMagicSoundOn = value;
				OnIsMagicSoundOnChanged(value);
				OnPropertyChanged("IsMagicSoundOn");
			}
		}
	}

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.ObservablePropertyGenerator", "8.4.0.0")]
	[ExcludeFromCodeCoverage]
	public PlaylistViewModel? SelectedPlaylistForEdit
	{
		get
		{
			return _selectedPlaylistForEdit;
		}
		set
		{
			if (!EqualityComparer<PlaylistViewModel>.Default.Equals(_selectedPlaylistForEdit, value))
			{
				OnPropertyChanging("SelectedPlaylistForEdit");
				_selectedPlaylistForEdit = value;
				OnPropertyChanged("SelectedPlaylistForEdit");
			}
		}
	}

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.ObservablePropertyGenerator", "8.4.0.0")]
	[ExcludeFromCodeCoverage]
	public BulkObservableCollection<MusicTrack> PlaylistEditTracks
	{
		get
		{
			return _playlistEditTracks;
		}
		[MemberNotNull("_playlistEditTracks")]
		set
		{
			if (!EqualityComparer<BulkObservableCollection<MusicTrack>>.Default.Equals(_playlistEditTracks, value))
			{
				OnPropertyChanging("PlaylistEditTracks");
				_playlistEditTracks = value;
				OnPropertyChanged("PlaylistEditTracks");
			}
		}
	}

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.ObservablePropertyGenerator", "8.4.0.0")]
	[ExcludeFromCodeCoverage]
	public MusicTrack? EditingTrack
	{
		get
		{
			return _editingTrack;
		}
		set
		{
			if (!EqualityComparer<MusicTrack>.Default.Equals(_editingTrack, value))
			{
				OnPropertyChanging("EditingTrack");
				_editingTrack = value;
				OnPropertyChanged("EditingTrack");
			}
		}
	}

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.ObservablePropertyGenerator", "8.4.0.0")]
	[ExcludeFromCodeCoverage]
	public string EditTitle
	{
		get
		{
			return _editTitle;
		}
		[MemberNotNull("_editTitle")]
		set
		{
			if (!EqualityComparer<string>.Default.Equals(_editTitle, value))
			{
				OnPropertyChanging("EditTitle");
				_editTitle = value;
				OnPropertyChanged("EditTitle");
			}
		}
	}

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.ObservablePropertyGenerator", "8.4.0.0")]
	[ExcludeFromCodeCoverage]
	public string EditArtist
	{
		get
		{
			return _editArtist;
		}
		[MemberNotNull("_editArtist")]
		set
		{
			if (!EqualityComparer<string>.Default.Equals(_editArtist, value))
			{
				OnPropertyChanging("EditArtist");
				_editArtist = value;
				OnPropertyChanged("EditArtist");
			}
		}
	}

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.ObservablePropertyGenerator", "8.4.0.0")]
	[ExcludeFromCodeCoverage]
	public string EditAlbum
	{
		get
		{
			return _editAlbum;
		}
		[MemberNotNull("_editAlbum")]
		set
		{
			if (!EqualityComparer<string>.Default.Equals(_editAlbum, value))
			{
				OnPropertyChanging("EditAlbum");
				_editAlbum = value;
				OnPropertyChanged("EditAlbum");
			}
		}
	}

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.ObservablePropertyGenerator", "8.4.0.0")]
	[ExcludeFromCodeCoverage]
	public string AlbumSearchQuery
	{
		get
		{
			return _albumSearchQuery;
		}
		[MemberNotNull("_albumSearchQuery")]
		set
		{
			if (!EqualityComparer<string>.Default.Equals(_albumSearchQuery, value))
			{
				OnPropertyChanging("AlbumSearchQuery");
				_albumSearchQuery = value;
				OnPropertyChanged("AlbumSearchQuery");
			}
		}
	}

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.ObservablePropertyGenerator", "8.4.0.0")]
	[ExcludeFromCodeCoverage]
	public string AlbumArtStatus
	{
		get
		{
			return _albumArtStatus;
		}
		[MemberNotNull("_albumArtStatus")]
		set
		{
			if (!EqualityComparer<string>.Default.Equals(_albumArtStatus, value))
			{
				OnPropertyChanging("AlbumArtStatus");
				_albumArtStatus = value;
				OnPropertyChanged("AlbumArtStatus");
			}
		}
	}

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.ObservablePropertyGenerator", "8.4.0.0")]
	[ExcludeFromCodeCoverage]
	public string SelectedLrcFolder
	{
		get
		{
			return _selectedLrcFolder;
		}
		[MemberNotNull("_selectedLrcFolder")]
		set
		{
			if (!EqualityComparer<string>.Default.Equals(_selectedLrcFolder, value))
			{
				OnPropertyChanging("SelectedLrcFolder");
				_selectedLrcFolder = value;
				OnSelectedLrcFolderChanged(value);
				OnPropertyChanged("SelectedLrcFolder");
			}
		}
	}

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.ObservablePropertyGenerator", "8.4.0.0")]
	[ExcludeFromCodeCoverage]
	public string LrcEditText
	{
		get
		{
			return _lrcEditText;
		}
		[MemberNotNull("_lrcEditText")]
		set
		{
			if (!EqualityComparer<string>.Default.Equals(_lrcEditText, value))
			{
				OnPropertyChanging("LrcEditText");
				_lrcEditText = value;
				OnPropertyChanged("LrcEditText");
			}
		}
	}

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.ObservablePropertyGenerator", "8.4.0.0")]
	[ExcludeFromCodeCoverage]
	public bool LrcEditShowValidation
	{
		get
		{
			return _lrcEditShowValidation;
		}
		set
		{
			if (!EqualityComparer<bool>.Default.Equals(_lrcEditShowValidation, value))
			{
				OnPropertyChanging("LrcEditShowValidation");
				_lrcEditShowValidation = value;
				OnPropertyChanged("LrcEditShowValidation");
			}
		}
	}

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.ObservablePropertyGenerator", "8.4.0.0")]
	[ExcludeFromCodeCoverage]
	public string SelectedTheme
	{
		get
		{
			return _selectedTheme;
		}
		[MemberNotNull("_selectedTheme")]
		set
		{
			if (!EqualityComparer<string>.Default.Equals(_selectedTheme, value))
			{
				OnPropertyChanging("SelectedTheme");
				_selectedTheme = value;
				OnPropertyChanged("SelectedTheme");
			}
		}
	}

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.ObservablePropertyGenerator", "8.4.0.0")]
	[ExcludeFromCodeCoverage]
	public bool IsStatusBarLyricsEnabled
	{
		get
		{
			return _isStatusBarLyricsEnabled;
		}
		set
		{
			if (!EqualityComparer<bool>.Default.Equals(_isStatusBarLyricsEnabled, value))
			{
				OnPropertyChanging("IsStatusBarLyricsEnabled");
				_isStatusBarLyricsEnabled = value;
				OnPropertyChanged("IsStatusBarLyricsEnabled");
			}
		}
	}

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.RelayCommandGenerator", "8.4.0.0")]
	[ExcludeFromCodeCoverage]
	public IRelayCommand<object?> SetPlayingPageCommand => setPlayingPageCommand ?? (setPlayingPageCommand = new RelayCommand<object>(SetPlayingPage));

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.RelayCommandGenerator", "8.4.0.0")]
	[ExcludeFromCodeCoverage]
	public IRelayCommand ToggleDrawerCommand => toggleDrawerCommand ?? (toggleDrawerCommand = new RelayCommand(ToggleDrawer));

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.RelayCommandGenerator", "8.4.0.0")]
	[ExcludeFromCodeCoverage]
	public IRelayCommand CloseDrawerCommand => closeDrawerCommand ?? (closeDrawerCommand = new RelayCommand(CloseDrawer));

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.RelayCommandGenerator", "8.4.0.0")]
	[ExcludeFromCodeCoverage]
	public IRelayCommand OpenFullPlayerCommand => openFullPlayerCommand ?? (openFullPlayerCommand = new RelayCommand(OpenFullPlayer));

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.RelayCommandGenerator", "8.4.0.0")]
	[ExcludeFromCodeCoverage]
	public IRelayCommand CloseFullPlayerCommand => closeFullPlayerCommand ?? (closeFullPlayerCommand = new RelayCommand(CloseFullPlayer));

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.RelayCommandGenerator", "8.4.0.0")]
	[ExcludeFromCodeCoverage]
	public IRelayCommand GoBackCommand => goBackCommand ?? (goBackCommand = new RelayCommand(GoBack));

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.RelayCommandGenerator", "8.4.0.0")]
	[ExcludeFromCodeCoverage]
	public IRelayCommand PlayPauseCommand => playPauseCommand ?? (playPauseCommand = new RelayCommand(PlayPause));

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.RelayCommandGenerator", "8.4.0.0")]
	[ExcludeFromCodeCoverage]
	public IRelayCommand StopCommand => stopCommand ?? (stopCommand = new RelayCommand(Stop));

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.RelayCommandGenerator", "8.4.0.0")]
	[ExcludeFromCodeCoverage]
	public IRelayCommand NextCommand => nextCommand ?? (nextCommand = new RelayCommand(Next));

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.RelayCommandGenerator", "8.4.0.0")]
	[ExcludeFromCodeCoverage]
	public IRelayCommand PreviousCommand => previousCommand ?? (previousCommand = new RelayCommand(Previous));

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.RelayCommandGenerator", "8.4.0.0")]
	[ExcludeFromCodeCoverage]
	public IRelayCommand<object?> PlayFileCommand => playFileCommand ?? (playFileCommand = new RelayCommand<object>(PlayFile));

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.RelayCommandGenerator", "8.4.0.0")]
	[ExcludeFromCodeCoverage]
	public IRelayCommand<object?> PlayFilesCommand => playFilesCommand ?? (playFilesCommand = new RelayCommand<object>(PlayFiles));

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.RelayCommandGenerator", "8.4.0.0")]
	[ExcludeFromCodeCoverage]
	public IRelayCommand<object?> SeekRelativeCommand => seekRelativeCommand ?? (seekRelativeCommand = new RelayCommand<object>(SeekRelative));

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.RelayCommandGenerator", "8.4.0.0")]
	[ExcludeFromCodeCoverage]
	public IRelayCommand<string> SetSpeedPresetCommand => setSpeedPresetCommand ?? (setSpeedPresetCommand = new RelayCommand<string>(SetSpeedPreset));

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.RelayCommandGenerator", "8.4.0.0")]
	[ExcludeFromCodeCoverage]
	public IRelayCommand ResetSpeedPitchCommand => resetSpeedPitchCommand ?? (resetSpeedPitchCommand = new RelayCommand(ResetSpeedPitch));

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.RelayCommandGenerator", "8.4.0.0")]
	[ExcludeFromCodeCoverage]
	public IRelayCommand<object?> ToggleSoundEffectCommand => toggleSoundEffectCommand ?? (toggleSoundEffectCommand = new RelayCommand<object>(ToggleSoundEffect));

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.RelayCommandGenerator", "8.4.0.0")]
	[ExcludeFromCodeCoverage]
	public IRelayCommand<EqPreset?> SelectEqPresetCommand => selectEqPresetCommand ?? (selectEqPresetCommand = new RelayCommand<EqPreset>(SelectEqPreset));

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.RelayCommandGenerator", "8.4.0.0")]
	[ExcludeFromCodeCoverage]
	public IRelayCommand ToggleShuffleCommand => toggleShuffleCommand ?? (toggleShuffleCommand = new RelayCommand(ToggleShuffle));

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.RelayCommandGenerator", "8.4.0.0")]
	[ExcludeFromCodeCoverage]
	public IRelayCommand ToggleRepeatCommand => toggleRepeatCommand ?? (toggleRepeatCommand = new RelayCommand(ToggleRepeat));

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.RelayCommandGenerator", "8.4.0.0")]
	[ExcludeFromCodeCoverage]
	public IRelayCommand ToggleFavoriteCommand => toggleFavoriteCommand ?? (toggleFavoriteCommand = new RelayCommand(ToggleFavorite));

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.RelayCommandGenerator", "8.4.0.0")]
	[ExcludeFromCodeCoverage]
	public IAsyncRelayCommand ShuffleAllCommand => shuffleAllCommand ?? (shuffleAllCommand = new AsyncRelayCommand(ShuffleAllAsync));

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.RelayCommandGenerator", "8.4.0.0")]
	[ExcludeFromCodeCoverage]
	public IRelayCommand ToggleSelectModeCommand => toggleSelectModeCommand ?? (toggleSelectModeCommand = new RelayCommand(ToggleSelectMode));

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.RelayCommandGenerator", "8.4.0.0")]
	[ExcludeFromCodeCoverage]
	public IRelayCommand<MusicTrack?> PlayTrackCommand => playTrackCommand ?? (playTrackCommand = new RelayCommand<MusicTrack>(PlayTrack));

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.RelayCommandGenerator", "8.4.0.0")]
	[ExcludeFromCodeCoverage]
	public IRelayCommand<HomeCard?> HomeCardClickCommand => homeCardClickCommand ?? (homeCardClickCommand = new RelayCommand<HomeCard>(OnHomeCardClick));

	private string _selectedSettingsSection = "All";
	public string SelectedSettingsSection
	{
		get => _selectedSettingsSection;
		set
		{
			if (!string.Equals(_selectedSettingsSection, value, StringComparison.Ordinal))
			{
				_selectedSettingsSection = string.IsNullOrEmpty(value) ? "All" : value;
				OnPropertyChanged(nameof(SelectedSettingsSection));
			}
		}
	}

	private string _settingsSearchText = string.Empty;
	public string SettingsSearchText
	{
		get => _settingsSearchText;
		set
		{
			if (!string.Equals(_settingsSearchText, value, StringComparison.Ordinal))
			{
				_settingsSearchText = value ?? string.Empty;
				OnPropertyChanged(nameof(SettingsSearchText));
			}
		}
	}

	private RelayCommand<string>? selectSettingsSectionCommand;
	public IRelayCommand<string?> SelectSettingsSectionCommand => selectSettingsSectionCommand ?? (selectSettingsSectionCommand = new RelayCommand<string>(s => { if (!string.IsNullOrEmpty(s)) SelectedSettingsSection = s; }));

	public string AppVersion => System.Reflection.Assembly.GetExecutingAssembly().GetName().Version?.ToString(3) ?? string.Empty;

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.RelayCommandGenerator", "8.4.0.0")]
	[ExcludeFromCodeCoverage]
	public IRelayCommand<string> NavigateCommand => navigateCommand ?? (navigateCommand = new RelayCommand<string>(Navigate));

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.RelayCommandGenerator", "8.4.0.0")]
	[ExcludeFromCodeCoverage]
	public IRelayCommand ShowEqualizerPanelCommand => showEqualizerPanelCommand ?? (showEqualizerPanelCommand = new RelayCommand(ShowEqualizerPanel));

	public IRelayCommand ResetEqualizerCommand => resetEqualizerCommand ?? (resetEqualizerCommand = new RelayCommand(ResetEqualizer));

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.RelayCommandGenerator", "8.4.0.0")]
	[ExcludeFromCodeCoverage]
	public IRelayCommand ShowSoundEffectsPanelCommand => showSoundEffectsPanelCommand ?? (showSoundEffectsPanelCommand = new RelayCommand(ShowSoundEffectsPanel));

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.RelayCommandGenerator", "8.4.0.0")]
	[ExcludeFromCodeCoverage]
	public IRelayCommand ShowSpeedPitchPanelCommand => showSpeedPitchPanelCommand ?? (showSpeedPitchPanelCommand = new RelayCommand(ShowSpeedPitchPanel));

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.RelayCommandGenerator", "8.4.0.0")]
	[ExcludeFromCodeCoverage]
	public IRelayCommand ShowLyricsPanelCommand => showLyricsPanelCommand ?? (showLyricsPanelCommand = new RelayCommand(ShowLyricsPanel));

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.RelayCommandGenerator", "8.4.0.0")]
	[ExcludeFromCodeCoverage]
	public IRelayCommand ShowSettingsPanelCommand => showSettingsPanelCommand ?? (showSettingsPanelCommand = new RelayCommand(ShowSettingsPanel));

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.RelayCommandGenerator", "8.4.0.0")]
	[ExcludeFromCodeCoverage]
	public IRelayCommand ShowQueuePanelCommand => showQueuePanelCommand ?? (showQueuePanelCommand = new RelayCommand(ShowQueuePanel));

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.RelayCommandGenerator", "8.4.0.0")]
	[ExcludeFromCodeCoverage]
	public IRelayCommand<string> SwitchArtistDetailTabCommand => switchArtistDetailTabCommand ?? (switchArtistDetailTabCommand = new RelayCommand<string>(SwitchArtistDetailTab));

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.RelayCommandGenerator", "8.4.0.0")]
	[ExcludeFromCodeCoverage]
	public IRelayCommand PlayArtistCommand => playArtistCommand ?? (playArtistCommand = new RelayCommand(PlayArtist));

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.RelayCommandGenerator", "8.4.0.0")]
	[ExcludeFromCodeCoverage]
	public IRelayCommand ShuffleArtistCommand => shuffleArtistCommand ?? (shuffleArtistCommand = new RelayCommand(ShuffleArtist));

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.RelayCommandGenerator", "8.4.0.0")]
	[ExcludeFromCodeCoverage]
	public IRelayCommand AddArtistToQueueCommand => addArtistToQueueCommand ?? (addArtistToQueueCommand = new RelayCommand(AddArtistToQueue));

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.RelayCommandGenerator", "8.4.0.0")]
	[ExcludeFromCodeCoverage]
	public IRelayCommand PlayAlbumCommand => playAlbumCommand ?? (playAlbumCommand = new RelayCommand(PlayAlbum));

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.RelayCommandGenerator", "8.4.0.0")]
	[ExcludeFromCodeCoverage]
	public IRelayCommand ShuffleAlbumCommand => shuffleAlbumCommand ?? (shuffleAlbumCommand = new RelayCommand(ShuffleAlbum));

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.RelayCommandGenerator", "8.4.0.0")]
	[ExcludeFromCodeCoverage]
	public IRelayCommand AddAlbumToQueueCommand => addAlbumToQueueCommand ?? (addAlbumToQueueCommand = new RelayCommand(AddAlbumToQueue));

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.RelayCommandGenerator", "8.4.0.0")]
	[ExcludeFromCodeCoverage]
	public IRelayCommand PlayPlaylistDetailCommand => playPlaylistDetailCommand ?? (playPlaylistDetailCommand = new RelayCommand(PlayPlaylistDetail));

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.RelayCommandGenerator", "8.4.0.0")]
	[ExcludeFromCodeCoverage]
	public IRelayCommand ShufflePlaylistDetailCommand => shufflePlaylistDetailCommand ?? (shufflePlaylistDetailCommand = new RelayCommand(ShufflePlaylistDetail));

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.RelayCommandGenerator", "8.4.0.0")]
	[ExcludeFromCodeCoverage]
	public IRelayCommand PlayGenreDetailCommand => playGenreDetailCommand ?? (playGenreDetailCommand = new RelayCommand(PlayGenreDetail));

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.RelayCommandGenerator", "8.4.0.0")]
	[ExcludeFromCodeCoverage]
	public IRelayCommand ShuffleGenreDetailCommand => shuffleGenreDetailCommand ?? (shuffleGenreDetailCommand = new RelayCommand(ShuffleGenreDetail));

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.RelayCommandGenerator", "8.4.0.0")]
	[ExcludeFromCodeCoverage]
	public IRelayCommand AddPlaylistDetailToQueueCommand => addPlaylistDetailToQueueCommand ?? (addPlaylistDetailToQueueCommand = new RelayCommand(AddPlaylistDetailToQueue));

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.RelayCommandGenerator", "8.4.0.0")]
	[ExcludeFromCodeCoverage]
	public IRelayCommand<MusicTrack?> RemoveTrackFromPlaylistCommand => removeTrackFromPlaylistCommand ?? (removeTrackFromPlaylistCommand = new RelayCommand<MusicTrack>(RemoveTrackFromPlaylist));

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.RelayCommandGenerator", "8.4.0.0")]
	[ExcludeFromCodeCoverage]
	public IAsyncRelayCommand AddManagedFolderCommand => addManagedFolderCommand ?? (addManagedFolderCommand = new AsyncRelayCommand(AddManagedFolderAsync));

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.RelayCommandGenerator", "8.4.0.0")]
	[ExcludeFromCodeCoverage]
	public IAsyncRelayCommand<FolderItem?> RemoveManagedFolderCommand => removeManagedFolderCommand ?? (removeManagedFolderCommand = new AsyncRelayCommand<FolderItem>(RemoveManagedFolder));

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.RelayCommandGenerator", "8.4.0.0")]
	[ExcludeFromCodeCoverage]
	public IRelayCommand NavigateToPlaylistsCommand => navigateToPlaylistsCommand ?? (navigateToPlaylistsCommand = new RelayCommand(NavigateToPlaylists));

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.RelayCommandGenerator", "8.4.0.0")]
	[ExcludeFromCodeCoverage]
	public IRelayCommand ToggleSearchCommand => toggleSearchCommand ?? (toggleSearchCommand = new RelayCommand(ToggleSearch));

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.RelayCommandGenerator", "8.4.0.0")]
	[ExcludeFromCodeCoverage]
	public IRelayCommand ToggleMuteCommand => toggleMuteCommand ?? (toggleMuteCommand = new RelayCommand(ToggleMute));

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.RelayCommandGenerator", "8.4.0.0")]
	[ExcludeFromCodeCoverage]
	public IRelayCommand SearchCommand => searchCommand ?? (searchCommand = new RelayCommand(Search));

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.RelayCommandGenerator", "8.4.0.0")]
	[ExcludeFromCodeCoverage]
	public IRelayCommand<PlaylistViewModel?> PlayPlaylistCommand => playPlaylistCommand ?? (playPlaylistCommand = new RelayCommand<PlaylistViewModel>(PlayPlaylist));

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.RelayCommandGenerator", "8.4.0.0")]
	[ExcludeFromCodeCoverage]
	public IRelayCommand<string?> CreatePlaylistCommand => createPlaylistCommand ?? (createPlaylistCommand = new RelayCommand<string>(CreatePlaylist));

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.RelayCommandGenerator", "8.4.0.0")]
	[ExcludeFromCodeCoverage]
	public IRelayCommand<MusicTrack?> AddToQueueCommand => addToQueueCommand ?? (addToQueueCommand = new RelayCommand<MusicTrack>(AddToQueue));

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.RelayCommandGenerator", "8.4.0.0")]
	[ExcludeFromCodeCoverage]
	public IRelayCommand<MusicTrack?> RemoveFromQueueCommand => removeFromQueueCommand ?? (removeFromQueueCommand = new RelayCommand<MusicTrack>(RemoveFromQueue));

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.RelayCommandGenerator", "8.4.0.0")]
	[ExcludeFromCodeCoverage]
	public IRelayCommand ClearQueueCommand => clearQueueCommand ?? (clearQueueCommand = new RelayCommand(ClearQueue));

	private RelayCommand? clearUpcomingQueueCommand;
	public IRelayCommand ClearUpcomingQueueCommand => clearUpcomingQueueCommand ?? (clearUpcomingQueueCommand = new RelayCommand(ClearUpcomingQueue));

	private RelayCommand? scrollToCurrentTrackCommand;
	public IRelayCommand ScrollToCurrentTrackCommand => scrollToCurrentTrackCommand ?? (scrollToCurrentTrackCommand = new RelayCommand(RequestScrollToCurrentTrack));

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.RelayCommandGenerator", "8.4.0.0")]
	[ExcludeFromCodeCoverage]
	public IRelayCommand<MusicTrack?> MoveQueueUpCommand => moveQueueUpCommand ?? (moveQueueUpCommand = new RelayCommand<MusicTrack>(MoveQueueUp));

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.RelayCommandGenerator", "8.4.0.0")]
	[ExcludeFromCodeCoverage]
	public IRelayCommand<MusicTrack?> MoveQueueDownCommand => moveQueueDownCommand ?? (moveQueueDownCommand = new RelayCommand<MusicTrack>(MoveQueueDown));

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.RelayCommandGenerator", "8.4.0.0")]
	[ExcludeFromCodeCoverage]
	public IRelayCommand<MusicTrack?> PlayQueueTrackCommand => playQueueTrackCommand ?? (playQueueTrackCommand = new RelayCommand<MusicTrack>(PlayQueueTrack));

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.RelayCommandGenerator", "8.4.0.0")]
	[ExcludeFromCodeCoverage]
	public IRelayCommand SaveQueueAsPlaylistCommand => saveQueueAsPlaylistCommand ?? (saveQueueAsPlaylistCommand = new RelayCommand(SaveQueueAsPlaylist));

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.RelayCommandGenerator", "8.4.0.0")]
	[ExcludeFromCodeCoverage]
	public IRelayCommand<LyricLine?> SeekToLyricCommand => seekToLyricCommand ?? (seekToLyricCommand = new RelayCommand<LyricLine>(SeekToLyric));

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.RelayCommandGenerator", "8.4.0.0")]
	[ExcludeFromCodeCoverage]
	public IRelayCommand OpenHiddenFoldersCommand => openHiddenFoldersCommand ?? (openHiddenFoldersCommand = new RelayCommand(OpenHiddenFolders));

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.RelayCommandGenerator", "8.4.0.0")]
	[ExcludeFromCodeCoverage]
	public IRelayCommand AddHiddenFolderDialogCommand => addHiddenFolderDialogCommand ?? (addHiddenFolderDialogCommand = new RelayCommand(AddHiddenFolderDialog));

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.RelayCommandGenerator", "8.4.0.0")]
	[ExcludeFromCodeCoverage]
	public IRelayCommand<HiddenFolder?> RemoveHiddenFolderCommand => removeHiddenFolderCommand ?? (removeHiddenFolderCommand = new RelayCommand<HiddenFolder>(RemoveHiddenFolder));

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.RelayCommandGenerator", "8.4.0.0")]
	[ExcludeFromCodeCoverage]
	public IRelayCommand OpenHiddenFilesCommand => openHiddenFilesCommand ?? (openHiddenFilesCommand = new RelayCommand(OpenHiddenFiles));

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.RelayCommandGenerator", "8.4.0.0")]
	[ExcludeFromCodeCoverage]
	public IRelayCommand<MusicTrack?> UnhideTrackCommand => unhideTrackCommand ?? (unhideTrackCommand = new RelayCommand<MusicTrack>(UnhideTrack));

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.RelayCommandGenerator", "8.4.0.0")]
	[ExcludeFromCodeCoverage]
	public IRelayCommand<MusicTrack?> HideTrackCommand => hideTrackCommand ?? (hideTrackCommand = new RelayCommand<MusicTrack>(HideTrack));

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.RelayCommandGenerator", "8.4.0.0")]
	[ExcludeFromCodeCoverage]
	public IRelayCommand AddHiddenFilesDialogCommand => addHiddenFilesDialogCommand ?? (addHiddenFilesDialogCommand = new RelayCommand(AddHiddenFilesDialog));

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.RelayCommandGenerator", "8.4.0.0")]
	[ExcludeFromCodeCoverage]
	public IRelayCommand OpenScanSettingsCommand => openScanSettingsCommand ?? (openScanSettingsCommand = new RelayCommand(OpenScanSettings));

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.RelayCommandGenerator", "8.4.0.0")]
	[ExcludeFromCodeCoverage]
	public IRelayCommand AddScanFolderDialogCommand => addScanFolderDialogCommand ?? (addScanFolderDialogCommand = new RelayCommand(AddScanFolderDialog));

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.RelayCommandGenerator", "8.4.0.0")]
	[ExcludeFromCodeCoverage]
	public IRelayCommand<string?> RemoveScanFolderCommand => removeScanFolderCommand ?? (removeScanFolderCommand = new RelayCommand<string>(RemoveScanFolder));

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.RelayCommandGenerator", "8.4.0.0")]
	[ExcludeFromCodeCoverage]
	public IAsyncRelayCommand OpenDuplicateFinderCommand => openDuplicateFinderCommand ?? (openDuplicateFinderCommand = new AsyncRelayCommand(OpenDuplicateFinderAsync));

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.RelayCommandGenerator", "8.4.0.0")]
	[ExcludeFromCodeCoverage]
	public IRelayCommand<MusicTrack?> DeleteDuplicateCommand => deleteDuplicateCommand ?? (deleteDuplicateCommand = new RelayCommand<MusicTrack>(DeleteDuplicate));

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.RelayCommandGenerator", "8.4.0.0")]
	[ExcludeFromCodeCoverage]
	public IAsyncRelayCommand OpenDeletedMusicCommand => openDeletedMusicCommand ?? (openDeletedMusicCommand = new AsyncRelayCommand(OpenDeletedMusicAsync));

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.RelayCommandGenerator", "8.4.0.0")]
	[ExcludeFromCodeCoverage]
	public IRelayCommand<MusicTrack?> RemoveDeletedTrackCommand => removeDeletedTrackCommand ?? (removeDeletedTrackCommand = new RelayCommand<MusicTrack>(RemoveDeletedTrack));

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.RelayCommandGenerator", "8.4.0.0")]
	[ExcludeFromCodeCoverage]
	public IRelayCommand ClearAllDeletedCommand => clearAllDeletedCommand ?? (clearAllDeletedCommand = new RelayCommand(ClearAllDeleted));

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.RelayCommandGenerator", "8.4.0.0")]
	[ExcludeFromCodeCoverage]
	public IAsyncRelayCommand OpenMusicSelectCommand => openMusicSelectCommand ?? (openMusicSelectCommand = new AsyncRelayCommand(OpenMusicSelectAsync));

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.RelayCommandGenerator", "8.4.0.0")]
	[ExcludeFromCodeCoverage]
	public IAsyncRelayCommand OpenMusicSetEditCommand => openMusicSetEditCommand ?? (openMusicSetEditCommand = new AsyncRelayCommand(OpenMusicSetEditAsync));

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.RelayCommandGenerator", "8.4.0.0")]
	[ExcludeFromCodeCoverage]
	public IRelayCommand OpenPlaylistEditCommand => openPlaylistEditCommand ?? (openPlaylistEditCommand = new RelayCommand(OpenPlaylistEdit));

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.RelayCommandGenerator", "8.4.0.0")]
	[ExcludeFromCodeCoverage]
	public IRelayCommand<PlaylistViewModel?> MovePlaylistUpCommand => movePlaylistUpCommand ?? (movePlaylistUpCommand = new RelayCommand<PlaylistViewModel>(MovePlaylistUp));

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.RelayCommandGenerator", "8.4.0.0")]
	[ExcludeFromCodeCoverage]
	public IRelayCommand<PlaylistViewModel?> MovePlaylistDownCommand => movePlaylistDownCommand ?? (movePlaylistDownCommand = new RelayCommand<PlaylistViewModel>(MovePlaylistDown));

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.RelayCommandGenerator", "8.4.0.0")]
	[ExcludeFromCodeCoverage]
	public IRelayCommand OpenPlaylistSelectCommand => openPlaylistSelectCommand ?? (openPlaylistSelectCommand = new RelayCommand(OpenPlaylistSelect));

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.RelayCommandGenerator", "8.4.0.0")]
	[ExcludeFromCodeCoverage]
	public IRelayCommand<PlaylistViewModel?> SelectPlaylistForAddCommand => selectPlaylistForAddCommand ?? (selectPlaylistForAddCommand = new RelayCommand<PlaylistViewModel>(SelectPlaylistForAdd));

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.RelayCommandGenerator", "8.4.0.0")]
	[ExcludeFromCodeCoverage]
	public IRelayCommand<PlaylistViewModel?> DeletePlaylistCommand => deletePlaylistCommand ?? (deletePlaylistCommand = new RelayCommand<PlaylistViewModel>(DeletePlaylist));

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.RelayCommandGenerator", "8.4.0.0")]
	[ExcludeFromCodeCoverage]
	public IRelayCommand<(PlaylistViewModel vm, string newName)> RenamePlaylistCommand => renamePlaylistCommand ?? (renamePlaylistCommand = new RelayCommand<(PlaylistViewModel, string)>(RenamePlaylist));

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.RelayCommandGenerator", "8.4.0.0")]
	[ExcludeFromCodeCoverage]
	public IRelayCommand<MusicTrack?> OpenEditTagsCommand => openEditTagsCommand ?? (openEditTagsCommand = new RelayCommand<MusicTrack>(OpenEditTags));

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.RelayCommandGenerator", "8.4.0.0")]
	[ExcludeFromCodeCoverage]
	public IRelayCommand SaveEditTagsCommand => saveEditTagsCommand ?? (saveEditTagsCommand = new RelayCommand(SaveEditTags));

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.RelayCommandGenerator", "8.4.0.0")]
	[ExcludeFromCodeCoverage]
	public IRelayCommand OpenBatchEditCommand => openBatchEditCommand ?? (openBatchEditCommand = new RelayCommand(OpenBatchEdit));

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.RelayCommandGenerator", "8.4.0.0")]
	[ExcludeFromCodeCoverage]
	public IRelayCommand<string?> BatchSetArtistCommand => batchSetArtistCommand ?? (batchSetArtistCommand = new RelayCommand<string>(BatchSetArtist));

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.RelayCommandGenerator", "8.4.0.0")]
	[ExcludeFromCodeCoverage]
	public IRelayCommand OpenAlbumGetterCommand => openAlbumGetterCommand ?? (openAlbumGetterCommand = new RelayCommand(OpenAlbumGetter));

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.RelayCommandGenerator", "8.4.0.0")]
	[ExcludeFromCodeCoverage]
	public IRelayCommand SearchAlbumArtCommand => searchAlbumArtCommand ?? (searchAlbumArtCommand = new RelayCommand(SearchAlbumArt));

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.RelayCommandGenerator", "8.4.0.0")]
	[ExcludeFromCodeCoverage]
	public IRelayCommand OpenAlbumSaveCommand => openAlbumSaveCommand ?? (openAlbumSaveCommand = new RelayCommand(OpenAlbumSave));

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.RelayCommandGenerator", "8.4.0.0")]
	[ExcludeFromCodeCoverage]
	public IRelayCommand SaveAlbumArtCommand => saveAlbumArtCommand ?? (saveAlbumArtCommand = new RelayCommand(SaveAlbumArt));

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.RelayCommandGenerator", "8.4.0.0")]
	[ExcludeFromCodeCoverage]
	public IRelayCommand OpenAudioEditorCommand => openAudioEditorCommand ?? (openAudioEditorCommand = new RelayCommand(OpenAudioEditor));

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.RelayCommandGenerator", "8.4.0.0")]
	[ExcludeFromCodeCoverage]
	public IRelayCommand OpenLrcBrowserCommand => openLrcBrowserCommand ?? (openLrcBrowserCommand = new RelayCommand(OpenLrcBrowser));

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.RelayCommandGenerator", "8.4.0.0")]
	[ExcludeFromCodeCoverage]
	public IRelayCommand OpenLrcEditCommand => openLrcEditCommand ?? (openLrcEditCommand = new RelayCommand(OpenLrcEdit));

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.RelayCommandGenerator", "8.4.0.0")]
	[ExcludeFromCodeCoverage]
	public IRelayCommand SaveLrcEditCommand => saveLrcEditCommand ?? (saveLrcEditCommand = new RelayCommand(SaveLrcEdit));

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.RelayCommandGenerator", "8.4.0.0")]
	[ExcludeFromCodeCoverage]
	public IRelayCommand OpenSleepTimerCommand => openSleepTimerCommand ?? (openSleepTimerCommand = new RelayCommand(OpenSleepTimer));

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.RelayCommandGenerator", "8.4.0.0")]
	[ExcludeFromCodeCoverage]
	public IRelayCommand<string?> SetSleepTimerCommand => setSleepTimerCommand ?? (setSleepTimerCommand = new RelayCommand<string>(SetSleepTimer));

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.RelayCommandGenerator", "8.4.0.0")]
	[ExcludeFromCodeCoverage]
	public IRelayCommand CancelSleepTimerCommand => cancelSleepTimerCommand ?? (cancelSleepTimerCommand = new RelayCommand(CancelSleepTimer));

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.RelayCommandGenerator", "8.4.0.0")]
	[ExcludeFromCodeCoverage]
	public IRelayCommand<MusicTrack?> OpenTrackBottomSheetCommand => openTrackBottomSheetCommand ?? (openTrackBottomSheetCommand = new RelayCommand<MusicTrack>(OpenTrackBottomSheet));

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.RelayCommandGenerator", "8.4.0.0")]
	[ExcludeFromCodeCoverage]
	public IRelayCommand CloseTrackBottomSheetCommand => closeTrackBottomSheetCommand ?? (closeTrackBottomSheetCommand = new RelayCommand(CloseTrackBottomSheet));

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.RelayCommandGenerator", "8.4.0.0")]
	[ExcludeFromCodeCoverage]
	public IRelayCommand<MusicTrack?> OpenTrackInfoCommand => openTrackInfoCommand ?? (openTrackInfoCommand = new RelayCommand<MusicTrack>(OpenTrackInfo));

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.RelayCommandGenerator", "8.4.0.0")]
	[ExcludeFromCodeCoverage]
	public IRelayCommand CloseTrackInfoCommand => closeTrackInfoCommand ?? (closeTrackInfoCommand = new RelayCommand(CloseTrackInfo));

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.RelayCommandGenerator", "8.4.0.0")]
	[ExcludeFromCodeCoverage]
	public IRelayCommand ToggleSortMenuCommand => toggleSortMenuCommand ?? (toggleSortMenuCommand = new RelayCommand(ToggleSortMenu));

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.RelayCommandGenerator", "8.4.0.0")]
	[ExcludeFromCodeCoverage]
	public IRelayCommand ToggleOverflowMenuCommand => toggleOverflowMenuCommand ?? (toggleOverflowMenuCommand = new RelayCommand(ToggleOverflowMenu));

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.RelayCommandGenerator", "8.4.0.0")]
	[ExcludeFromCodeCoverage]
	public IRelayCommand<string?> SelectSortOptionCommand => selectSortOptionCommand ?? (selectSortOptionCommand = new RelayCommand<string>(SelectSortOption));

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.RelayCommandGenerator", "8.4.0.0")]
	[ExcludeFromCodeCoverage]
	public IAsyncRelayCommand RefreshMetadataCommand => refreshMetadataCommand ?? (refreshMetadataCommand = new AsyncRelayCommand(RefreshMetadataAsync));

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.RelayCommandGenerator", "8.4.0.0")]
	[ExcludeFromCodeCoverage]
	public IRelayCommand ToggleSongsSectionCommand => toggleSongsSectionCommand ?? (toggleSongsSectionCommand = new RelayCommand(ToggleSongsSection));

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.RelayCommandGenerator", "8.4.0.0")]
	[ExcludeFromCodeCoverage]
	public IRelayCommand<MusicTrack?> ToggleTrackFavoriteCommand => toggleTrackFavoriteCommand ?? (toggleTrackFavoriteCommand = new RelayCommand<MusicTrack>(ToggleTrackFavorite));

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.RelayCommandGenerator", "8.4.0.0")]
	[ExcludeFromCodeCoverage]
	public IRelayCommand<MusicTrack?> PlayNextCommand => playNextCommand ?? (playNextCommand = new RelayCommand<MusicTrack>(PlayNext));

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.RelayCommandGenerator", "8.4.0.0")]
	[ExcludeFromCodeCoverage]
	public IRelayCommand<MusicTrack?> EnqueueCommand => enqueueCommand ?? (enqueueCommand = new RelayCommand<MusicTrack>(Enqueue));

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.RelayCommandGenerator", "8.4.0.0")]
	[ExcludeFromCodeCoverage]
	public IRelayCommand<MusicTrack?> SetRingtoneCommand => setRingtoneCommand ?? (setRingtoneCommand = new RelayCommand<MusicTrack>(SetRingtone));

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.RelayCommandGenerator", "8.4.0.0")]
	[ExcludeFromCodeCoverage]
	public IRelayCommand<MusicTrack?> TrimAudioCommand => trimAudioCommand ?? (trimAudioCommand = new RelayCommand<MusicTrack>(TrimAudio));

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.RelayCommandGenerator", "8.4.0.0")]
	[ExcludeFromCodeCoverage]
	public IRelayCommand<MusicTrack?> ShareTrackCommand => shareTrackCommand ?? (shareTrackCommand = new RelayCommand<MusicTrack>(ShareTrack));

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.RelayCommandGenerator", "8.4.0.0")]
	[ExcludeFromCodeCoverage]
	public IRelayCommand<MusicTrack?> DeleteTrackFromLibraryCommand => deleteTrackFromLibraryCommand ?? (deleteTrackFromLibraryCommand = new RelayCommand<MusicTrack>(DeleteTrackFromLibrary));

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.RelayCommandGenerator", "8.4.0.0")]
	[ExcludeFromCodeCoverage]
	public IAsyncRelayCommand OpenLyricListCommand => openLyricListCommand ?? (openLyricListCommand = new AsyncRelayCommand(OpenLyricListAsync));

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.RelayCommandGenerator", "8.4.0.0")]
	[ExcludeFromCodeCoverage]
	public IRelayCommand<string?> BindLyricFileCommand => bindLyricFileCommand ?? (bindLyricFileCommand = new RelayCommand<string>(BindLyricFile));

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.RelayCommandGenerator", "8.4.0.0")]
	[ExcludeFromCodeCoverage]
	public IRelayCommand OpenStatusBarLyricsCommand => openStatusBarLyricsCommand ?? (openStatusBarLyricsCommand = new RelayCommand(OpenStatusBarLyrics));

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.RelayCommandGenerator", "8.4.0.0")]
	[ExcludeFromCodeCoverage]
	public IRelayCommand OpenThemeGalleryCommand => openThemeGalleryCommand ?? (openThemeGalleryCommand = new RelayCommand(OpenThemeGallery));

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.RelayCommandGenerator", "8.4.0.0")]
	[ExcludeFromCodeCoverage]
	public IRelayCommand<string?> ApplyThemeCommand => applyThemeCommand ?? (applyThemeCommand = new RelayCommand<string>(ApplyTheme));

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.RelayCommandGenerator", "8.4.0.0")]
	[ExcludeFromCodeCoverage]
	public IRelayCommand OpenThemeEditorCommand => openThemeEditorCommand ?? (openThemeEditorCommand = new RelayCommand(OpenThemeEditor));

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.RelayCommandGenerator", "8.4.0.0")]
	[ExcludeFromCodeCoverage]
	public IRelayCommand<WallpaperItem?> SelectWallpaperCommand => selectWallpaperCommand ?? (selectWallpaperCommand = new RelayCommand<WallpaperItem>(SelectWallpaper));

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.RelayCommandGenerator", "8.4.0.0")]
	[ExcludeFromCodeCoverage]
	public IRelayCommand<string?> SelectWallpaperCategoryCommand => selectWallpaperCategoryCommand ?? (selectWallpaperCategoryCommand = new RelayCommand<string>(SelectWallpaperCategory));

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.RelayCommandGenerator", "8.4.0.0")]
	[ExcludeFromCodeCoverage]
	public IRelayCommand<string?> OpenGenreTracksCommand => openGenreTracksCommand ?? (openGenreTracksCommand = new RelayCommand<string>(OpenGenreTracks));

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.RelayCommandGenerator", "8.4.0.0")]
	[ExcludeFromCodeCoverage]
	public IRelayCommand<string?> OpenFolderTracksCommand => openFolderTracksCommand ?? (openFolderTracksCommand = new RelayCommand<string>(OpenFolderTracks));

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.RelayCommandGenerator", "8.4.0.0")]
	[ExcludeFromCodeCoverage]
	public IRelayCommand<object?> SetOutputDeviceCommand => setOutputDeviceCommand ?? (setOutputDeviceCommand = new RelayCommand<object>(SetOutputDevice));
	private void SetPlayingPage(object? page)
	{
		CurrentlyPlayingPage result;
		if (page is CurrentlyPlayingPage currentlyPlayingPage)
		{
			CurrentlyPlayingPage = currentlyPlayingPage;
		}
		else if (page is string value && Enum.TryParse<CurrentlyPlayingPage>(value, ignoreCase: true, out result))
		{
			CurrentlyPlayingPage = result;
		}
	}

	public void ApplySwipe(int horizontalDelta)
	{
		CurrentlyPlayingPage currentlyPlayingPage = CurrentlyPlayingPage switch
		{
			CurrentlyPlayingPage.AlbumArt => (horizontalDelta < 0) ? CurrentlyPlayingPage.Lyrics : CurrentlyPlayingPage.Visualizer, 
			CurrentlyPlayingPage.Visualizer => (horizontalDelta >= 0) ? CurrentlyPlayingPage.Visualizer : CurrentlyPlayingPage.AlbumArt, 
			CurrentlyPlayingPage.Lyrics => (horizontalDelta <= 0) ? CurrentlyPlayingPage.Lyrics : CurrentlyPlayingPage.AlbumArt, 
			_ => CurrentlyPlayingPage.AlbumArt, 
		};
		if (currentlyPlayingPage != CurrentlyPlayingPage)
		{
			CurrentlyPlayingPage = currentlyPlayingPage;
		}
	}

	public void LoadTabData(string tab)
	{
		switch (tab)
		{
		case "albums":
			if (Albums.Count == 0)
			{
				LoadAlbums();
			}
			break;
		case "artists":
			if (Artists.Count == 0)
			{
				LoadArtists();
			}
			break;
		case "genres":
			if (Genres.Count == 0)
			{
				LoadGenres();
			}
			break;
		case "folders":
			if (Folders.Count == 0)
			{
				LoadFolders();
			}
			break;
		case "songs":
			if (Tracks.Count == 0)
			{
				LoadLibrary();
			}
			break;
		}
	}

	private static void PerfTabData(string tab, int count)
	{
		try
		{
			if (Environment.GetEnvironmentVariable("MUSICPLAYER_PERFTEST") == "1")
				LogHelper.LogInfo($"TAB-DATA {tab} count={count} relMs={MusicPlayer.Views.SmoothScrollHelper.TabRelMs():0}");
		}
		catch { }
	}

	// Generation counter for progressive tab fills — a newer fill invalidates any
	// older one still pumping chunks (rapid tab re-entry).
	private int _tabFillGen;

	/// <summary>
	/// Fills a tab collection in small chunks, one per rendered frame, instead of a
	/// single ReplaceAll. Realizing 130+ WrapPanel tiles (their images, clips and
	/// text) in one dispatch costs a 250–450ms UI-thread freeze; the same work split
	/// into ~10-item chunks stays within a frame budget and the grid fills in front
	/// of the user instead of freezing the tab slide.
	/// </summary>
	private async Task ReplaceAllChunkedAsync(BulkObservableCollection<MusicTrack> target, List<MusicTrack> items)
	{
		int gen = Interlocked.Increment(ref _tabFillGen);
		// 6 per chunk, not 10: realizing a tile (clip + image + text) costs
		// ~3–4ms, so 10 landed 40–50ms frames inside the tab-switch window the
		// user is already watching; 6 keeps every fill frame under ~25ms.
		const int chunk = 6;
		try
		{
			if (items.Count <= chunk)
			{
				await DispatchAsync(delegate
				{
					if (gen == _tabFillGen) target.ReplaceAll(items);
				});
				return;
			}
			var first = items.GetRange(0, chunk);
			await DispatchAsync(delegate
			{
				if (gen == _tabFillGen) target.ReplaceAll(first);
			});
			int i = chunk;
			long maxChunkMs = 0;
			var swChunk = new System.Diagnostics.Stopwatch();
			while (i < items.Count)
			{
				int take = Math.Min(chunk, items.Count - i);
				var next = items.GetRange(i, take);
				i += take;
				await System.Windows.Threading.Dispatcher.Yield(System.Windows.Threading.DispatcherPriority.Render);
				if (gen != _tabFillGen)
				{
					if (Environment.GetEnvironmentVariable("MUSICPLAYER_PERFTEST") == "1")
						LogHelper.LogInfo($"TAB-FILL abort gen {gen}->{_tabFillGen} at {i}/{items.Count} count={target.Count}");
					return;
				}
				swChunk.Restart();
				target.AddRangeIncremental(next);
				swChunk.Stop();
				if (swChunk.ElapsedMilliseconds > maxChunkMs) maxChunkMs = swChunk.ElapsedMilliseconds;
			}
			if (Environment.GetEnvironmentVariable("MUSICPLAYER_PERFTEST") == "1")
				LogHelper.LogInfo($"TAB-FILL done count={target.Count}/{items.Count} maxChunkMs={maxChunkMs}");
		}
		catch (Exception ex)
		{
			if (Environment.GetEnvironmentVariable("MUSICPLAYER_PERFTEST") == "1")
				LogHelper.LogInfo($"TAB-FILL ex gen={gen} count={target.Count}: {ex.GetType().Name}: {ex.Message}");
		}
	}

	public ObservableCollection<MusicTrack> GetCurrentViewSource()
	{
		return CurrentView switch
		{
			"recent" => RecentPlayTracks, 
			"recent_add" => RecentAddTracks, 
			"most_played" => MostPlayedTracks, 
			"favorites" => FavoriteTracks, 
			"folder_detail" => FolderDetailTracks, 
			_ => Tracks, 
		};
	}

	public MainViewModel(PlaybackService playback, DatabaseService db, SettingsService settings, NavigationService nav, MusicScanner scanner, AlbumArtLoader albumArt, ArtistImageService artistImages, ILogger<MainViewModel> logger, WindowsIntegrationService? winIntegration = null)
	{
		_playback = playback;
		_db = db;
		_settings = settings;
		_nav = nav;
		_scanner = scanner;
		_albumArt = albumArt;
		_artistImages = artistImages;
		_logger = logger;
		_winIntegration = winIntegration;
		EqPreset[] presets = EqPreset.Presets;
		foreach (EqPreset item in presets)
		{
			EqPresets.Add(item);
		}
		WireAudioEvents();
		InitializeHomeCards();
		Tracks.CollectionChanged += delegate
		{
			OnPropertyChanged("HasNoMusic");
		};
		MostPlayedTracks.CollectionChanged += delegate
		{
			OnPropertyChanged("HasNoMusic");
		};
		RecentAddTracks.CollectionChanged += delegate
		{
			OnPropertyChanged("HasNoMusic");
		};
		FavoriteTracks.CollectionChanged += delegate
		{
			OnPropertyChanged("HasNoMusic");
		};
		RecentPlayTracks.CollectionChanged += delegate
		{
			OnPropertyChanged("HasNoMusic");
		};
		FolderDetailTracks.CollectionChanged += delegate
		{
			OnPropertyChanged("HasNoMusic");
		};
		SearchResults.CollectionChanged += delegate
		{
			OnPropertyChanged("HasNoMusic");
			OnPropertyChanged("HasNoSearchResults");
		};
	}

	public async Task InitializeAsync(Action<string>? statusCallback = null)
	{
		statusCallback?.Invoke("Loading settings...");
		await Task.Run(delegate
		{
			LoadSettings();
		});
		statusCallback?.Invoke("Loading library...");
		await Task.Run(delegate
		{
			_db.CleanOrphanTracks();
		});
		statusCallback?.Invoke("Scanning folders...");
		await Task.Run(delegate
		{
			_scanner.ForceResetScanning();
		});
		List<ManagedMusicFolder> list = await Task.Run(() => _db.GetManagedFolders());
		_logger.LogInformation("Found {Count} managed folders to scan", list.Count);
		foreach (ManagedMusicFolder mf in list)
		{
			_logger.LogInformation("Scanning folder: {Path}", mf.Path);
			try
			{
				await _scanner.ScanFolderAsync(mf.Path);
				await Task.Run(delegate
				{
					_db.UpdateManagedFolderLastScan(mf.Id);
				});
				_logger.LogInformation("Scan completed for: {Path}", mf.Path);
			}
			catch (Exception exception)
			{
				_logger.LogError(exception, "Failed to scan folder: {Folder}", mf.Path);
			}
		}
		await Task.Run(delegate
		{
			_db.RemoveMissingTracks();
		});
		await Task.Run(() => _scanner.BackfillDateAdded());
		Task<(int TrackCount, int FolderCount, int FavoriteCount, int RecentPlayCount, int RecentAddCount, int MostPlayedCount)> countsTask = Task.Run(() => _db.GetAllCounts());
		Task<List<MusicTrack>> tracksTask = Task.Run(() => _db.GetManagedFolderTracksSorted((LibrarySortOrder == "date_added") ? "date_added" : ((LibrarySortOrder == "duration") ? "duration" : ((LibrarySortOrder == "size") ? "size" : ((LibrarySortOrder == "year") ? "year" : "title"))), (LibrarySortOrder == "date_added") ? (!IsLibrarySortDescending) : IsLibrarySortDescending).Take(5000).ToList());
		await Task.WhenAll(countsTask, tracksTask);
		(int TrackCount, int FolderCount, int FavoriteCount, int RecentPlayCount, int RecentAddCount, int MostPlayedCount) counts = countsTask.Result;
		List<MusicTrack> sortedTracks = tracksTask.Result;
		await DispatchAsync(delegate
		{
			if (HomeCards.Count >= 6)
			{
				HomeCards[0].Count = counts.TrackCount.ToString();
				HomeCards[1].Count = counts.FolderCount.ToString();
				HomeCards[2].Count = counts.FavoriteCount.ToString();
				HomeCards[3].Count = counts.RecentPlayCount.ToString();
				HomeCards[4].Count = counts.RecentAddCount.ToString();
				HomeCards[5].Count = counts.MostPlayedCount.ToString();
			}
		});
		await Task.Yield();
		await DispatchAsync(delegate
		{
			Tracks.ReplaceAll(sortedTracks);
		});
		statusCallback?.Invoke("Loading artists...");
		try
		{
			_preloadedArtistResult = await BuildArtistItemsAsync();
		}
		catch
		{
		}
		List<KeyValuePair<string, string>> preloadedImages = _preloadedArtistResult?.CachedImages;
		if (preloadedImages != null && preloadedImages.Count > 0)
		{
			statusCallback?.Invoke("Loading artist art...");
			await Task.Run(delegate
			{
				PreloadArtistImages(preloadedImages);
			});
		}
		if (sortedTracks != null && sortedTracks.Count > 0)
		{
			await _albumArt.PreloadTrackArtwork(sortedTracks.Take(60));
		}
		GC.Collect(0);
		LoadPlaylists();
		RestoreQueueOnStartup();
		Queue.CollectionChanged += (_, _) => UpdateQueueSummary();
		UpdateQueueSummary();
		IsInitializing = false;
		// Row-thumbnail warmup runs immediately in background so the entire library is cached in RAM.
		_ = Task.Run(async delegate
		{
			await WarmUpAndRefreshAsync(sortedTracks);
		});
		_ = Task.Run(async delegate
		{
			// 30s-quiet (not 4s): wallpaper decoding only runs during a genuine
			// pause, aborts per file the instant the user interacts, and retries
			// until it completes — so returning to the app never meets a
			// wallpaper job already in flight (was a source of return-lag).
			while (true)
			{
				await MusicPlayer.Views.SmoothScrollHelper.WaitUntilIdleForBackgroundAsync(
					quietMs: 30000, minAgeMs: 45000, maxWaitMs: int.MaxValue);
				if (await EnsureWallpapersLoadedAsync(false, true)) break;
			}
		});
		// Re-crop all existing cached artist images with smart face-centered square crop.
		// Cancelled while the user interacts, resumed when they pause — re-cropping is
		// idempotent (already square files are skipped), so restarting stays cheap.
		_ = Task.Run(RunUpgradeArtistCacheAsync);
	}

	private async Task RunUpgradeArtistCacheAsync()
	{
		try
		{
			await MusicPlayer.Views.SmoothScrollHelper.WaitUntilIdleForBackgroundAsync(quietMs: 30000, minAgeMs: 45000, maxWaitMs: int.MaxValue);
			while (true)
			{
				using CancellationTokenSource cts = new CancellationTokenSource();
				Task work = _artistImages.UpgradeExistingCacheAsync(cts.Token);
				Task watchdog = Task.Run(async delegate
				{
					while (!work.IsCompleted)
					{
						if (MusicPlayer.Views.SmoothScrollHelper.IsInteractingNow())
						{
							cts.Cancel();
							break;
						}
						await Task.Delay(400);
					}
				});
				await work;
				await watchdog;
				if (!cts.IsCancellationRequested) break;
				await MusicPlayer.Views.SmoothScrollHelper.WaitUntilIdleForBackgroundAsync(quietMs: 30000, minAgeMs: 45000, maxWaitMs: int.MaxValue);
			}
		}
		catch
		{
		}
	}

	private async Task WarmUpAndRefreshAsync(List<MusicTrack> tracks)
	{
		var sw = System.Diagnostics.Stopwatch.StartNew();
		await _albumArt.WarmUpArtCacheAsync(tracks);
		long warmMs = sw.ElapsedMilliseconds;
		await DispatchAsync(delegate
		{
			var list = new List<MusicTrack>(Tracks);
			Tracks.ReplaceAll(list);
		});
		LogHelper.LogInfo($"WARMUP done warmMs={warmMs} totalMs={sw.ElapsedMilliseconds}");
	}

	private static Task DispatchAsync(Action action)
	{
		Action action2 = action;
		TaskCompletionSource tcs = new TaskCompletionSource();
		Application current = Application.Current;
		if (current != null)
		{
			((DispatcherObject)current).Dispatcher.BeginInvoke((Delegate)(Action)delegate
			{
				try
				{
					action2();
					tcs.SetResult();
				}
				catch (Exception exception)
				{
					tcs.SetException(exception);
				}
			}, Array.Empty<object>());
		}
		return tcs.Task;
	}

	private void WireAudioEvents()
	{
		_playback.Engine.PlaybackStarted += delegate
		{
			Dispatch(delegate
			{
				int currentManagedThreadId = Environment.CurrentManagedThreadId;
				long num = UtilSecondTimestamp();
				IsPlaying = true;
				CurrentTrack = _playback.Engine.CurrentTrack;
				Duration = _playback.Engine.Duration;
				RefreshQueue();
				long num2 = UtilSecondTimestamp();
				UpdateHomeCardCounts();
				_winIntegration?.ReportPlaying();
				App.PreventSleep();
				long num3 = UtilSecondTimestamp();
				LogHelper.LogInfo($"[PLAYBACK] UI Thread={currentManagedThreadId}: PlaybackStarted handler total={num3 - num}ms (fields+queue={num2 - num}ms, cards+sleep={num3 - num2}ms)");
			});
		};
		_playback.Engine.PlaybackPaused += delegate
		{
			Dispatch(delegate
			{
				DispatcherTimer? lyricAdvanceTimer2 = _lyricAdvanceTimer;
				if (lyricAdvanceTimer2 != null)
				{
					lyricAdvanceTimer2.Stop();
				}
				_lyricAdvanceTimer = null;
				IsPlaying = false;
				_winIntegration?.ReportPaused();
				App.AllowSleep();
			});
		};
		_playback.Engine.PlaybackStopped += delegate
		{
			Dispatch(delegate
			{
				DispatcherTimer? lyricAdvanceTimer = _lyricAdvanceTimer;
				if (lyricAdvanceTimer != null)
				{
					lyricAdvanceTimer.Stop();
				}
				_lyricAdvanceTimer = null;
				IsPlaying = false;
				Position = TimeSpan.Zero;
				OnPropertyChanged("PlaybackProgress");
				_winIntegration?.ReportStopped();
				App.AllowSleep();
			});
		};
		_playback.Engine.PositionChanged += delegate(object? _, TimeSpan pos)
		{
			Dispatch(delegate
			{
				if (!IsSeeking)
				{
					Position = pos;
					Duration = _playback.Engine.Duration;
					OnPropertyChanged("PlaybackProgress");
					UpdateLyricProgress(pos);
					_winIntegration?.ReportPositionTick();
				}
			});
		};
		_playback.Engine.TrackChanged += delegate(object? _, MusicTrack track)
		{
			MusicTrack track2 = track;
			Dispatch(delegate
			{
				CurrentTrack = track2;
				IsFavorite = track2?.IsFavorite ?? false;
				LoadLyrics(track2);
				if (track2 != null)
				{
					_winIntegration?.UpdateTrackInfo(track2);
				}
			});
		};
	}

	private static void Dispatch(Action action)
	{
		Application current = Application.Current;
		if (current != null)
		{
			((DispatcherObject)current).Dispatcher.BeginInvoke((Delegate)action, Array.Empty<object>());
		}
	}

	private void InitializeHomeCards()
	{
		HomeCards.Clear();
		HomeCards.Add(new HomeCard
		{
			Title = "ALL SONGS",
			IconKey = "Icon.Songs",
			Count = "0",
			TargetView = "songs",
			IsSupported = true
		});
		HomeCards.Add(new HomeCard
		{
			Title = "FOLDER",
			IconKey = "Icon.Folder",
			Count = "0",
			TargetView = "folders",
			IsSupported = true
		});
		HomeCards.Add(new HomeCard
		{
			Title = "FAVORITE",
			IconKey = "Icon.Favorite",
			Count = "0",
			TargetView = "favorites",
			IsSupported = true
		});
		HomeCards.Add(new HomeCard
		{
			Title = "RECENT PLAY",
			IconKey = "Icon.Recent",
			Count = "0",
			TargetView = "recent",
			IsSupported = true
		});
		HomeCards.Add(new HomeCard
		{
			Title = "RECENT ADD",
			IconKey = "Icon.RecentAdd",
			Count = "0",
			TargetView = "recent_add",
			IsSupported = true
		});
		HomeCards.Add(new HomeCard
		{
			Title = "MOST PLAY",
			IconKey = "Icon.MostPlayed",
			Count = "0",
			TargetView = "most_played",
			IsSupported = true
		});
	}
	private void ToggleDrawer()
	{
		IsDrawerOpen = !IsDrawerOpen;
	}
	private void CloseDrawer()
	{
		IsDrawerOpen = false;
	}
	private void OpenFullPlayer()
	{
		LoadLyrics(CurrentTrack);
		CurrentlyPlayingPage = CurrentlyPlayingPage.AlbumArt;
		ShowFullPlayer = true;
	}
	private void CloseFullPlayer()
	{
		ShowFullPlayer = false;
	}
	private void GoBack()
	{
		if (CloseTopmostOverlay())
		{
			return;
		}
		if (CurrentView == "lrc_editor")
		{
			CleanupLrcEditIfEmpty();
		}
		bool num = ShowFullPlayer || _navigatedFromFullPlayer;
		_navigatedFromFullPlayer = false;
		if (_viewHistory.Count > 0)
		{
			string target = _viewHistory.Pop();
			// Collapse consecutive duplicates left by double-invocations: popping
			// to the view we already show notifies nothing, so the back silently
			// does nothing while a hidden detail page stays invisible (ghost page).
			while (_viewHistory.Count > 0 && string.Equals(target, CurrentView, StringComparison.OrdinalIgnoreCase))
			{
				LogHelper.LogInfo($"GoBack: skipping duplicate history entry '{target}'");
				target = _viewHistory.Pop();
			}
			if (string.Equals(target, CurrentView, StringComparison.OrdinalIgnoreCase))
				LogHelper.LogInfo($"GoBack: no-op, already on '{CurrentView}'");
			else
				CurrentView = target;
		}
		else if (CurrentView != "home")
		{
			CurrentView = "home";
		}
		bool flag = num;
		if (flag)
		{
			bool flag2;
			switch (CurrentView)
			{
			case "songs":
			case "artists":
			case "albums":
			case "genres":
			case "folders":
				flag2 = true;
				break;
			default:
				flag2 = false;
				break;
			}
			flag = flag2;
		}
		if (flag)
		{
			ShowFullPlayer = true;
		}
		OnPropertyChanged("CanNavigateBack");
	}



	public bool CloseTopmostOverlay()
	{
		if (ShowTrackBottomSheet)
		{
			ShowTrackBottomSheet = false;
			return true;
		}
		if (ShowTrackInfo)
		{
			ShowTrackInfo = false;
			return true;
		}
		if (ShowSortMenu)
		{
			ShowSortMenu = false;
			return true;
		}
		if (ShowOverflowMenu)
		{
			ShowOverflowMenu = false;
			return true;
		}
		if (IsDrawerOpen)
		{
			IsDrawerOpen = false;
			return true;
		}
		if (ShowQueue)
		{
			ShowQueue = false;
			return true;
		}
		if (ShowSoundEffects)
		{
			ShowSoundEffects = false;
			return true;
		}
		if (ShowEqualizer)
		{
			ShowEqualizer = false;
			return true;
		}
		if (ShowSpeedPitch)
		{
			ShowSpeedPitch = false;
			return true;
		}
		if (ShowLyrics)
		{
			ShowLyrics = false;
			return true;
		}
		if (ShowFullPlayer)
		{
			ShowFullPlayer = false;
			return true;
		}
		return false;
	}
	private void PlayPause()
	{
		if (_playback.Engine.CurrentTrack == null && Tracks.Count > 0)
		{
			PlayTrack(Tracks[0]);
		}
		else
		{
			_playback.PlayPause();
		}
	}
	private void Stop()
	{
		_playback.Stop();
	}
	private void Next()
	{
		_playback.Next();
	}
	private void Previous()
	{
		_playback.Previous();
	}
	private void PlayFile(object? pathParam)
	{
		string path = pathParam as string;
		if (path == null || !System.IO.File.Exists(path))
		{
			return;
		}
		string extension = Path.GetExtension(path);
		if (!new string[10] { ".mp3", ".wav", ".flac", ".m4a", ".ogg", ".wma", ".opus", ".aiff", ".ape", ".ac3" }.Contains<string>(extension, StringComparer.OrdinalIgnoreCase))
		{
			return;
		}
		MusicTrack musicTrack = Tracks.FirstOrDefault((MusicTrack t) => string.Equals(t.FilePath, path, StringComparison.OrdinalIgnoreCase));
		if (musicTrack != null)
		{
			_playback.PlayTrack(musicTrack);
			return;
		}
		try
		{
			using TagLib.File file = TagLib.File.Create(path);
			FileInfo fileInfo = new FileInfo(path);
			MusicTrack musicTrack2 = new MusicTrack
			{
				Title = (file.Tag.Title ?? Path.GetFileNameWithoutExtension(path)),
				Artist = (file.Tag.FirstPerformer ?? "Unknown Artist"),
				Album = (file.Tag.Album ?? "Unknown Album"),
				AlbumArtist = (file.Tag.FirstAlbumArtist ?? ""),
				Genre = (file.Tag.FirstGenre ?? ""),
				Year = (int)file.Tag.Year,
				TrackNumber = (int)file.Tag.Track,
				DiscNumber = (int)file.Tag.Disc,
				FilePath = path,
				Duration = (long)file.Properties.Duration.TotalMilliseconds,
				FileSize = fileInfo.Length,
				Bitrate = file.Properties.AudioBitrate,
				SampleRate = file.Properties.AudioSampleRate,
				Channels = file.Properties.AudioChannels,
				DateAdded = DateTimeOffset.UtcNow.ToUnixTimeSeconds(),
				DateModified = new DateTimeOffset(fileInfo.LastWriteTime).ToUnixTimeSeconds()
			};
			_db.InsertTrack(musicTrack2);
			Tracks.Add(musicTrack2);
			_playback.PlayTrack(musicTrack2);
		}
		catch (Exception exception)
		{
			_logger.LogError(exception, "Error playing dropped file: {Path}", path);
		}
	}
	private void PlayFiles(object? pathsParam)
	{
		if (!(pathsParam is IEnumerable<string> source))
		{
			return;
		}
		List<string> list = source.ToList();
		if (list.Count == 0)
		{
			return;
		}
		foreach (string item in list)
		{
			PlayFile(item);
		}
	}
	private void SeekRelative(object? secondsParam)
	{
		double result;
		if (secondsParam is double seconds)
		{
			_playback.SeekRelative(seconds);
		}
		else if (secondsParam != null && double.TryParse(secondsParam.ToString(), NumberStyles.Float, CultureInfo.InvariantCulture, out result))
		{
			_playback.SeekRelative(result);
		}
	}
	private void SetSpeedPreset(string speedStr)
	{
		if (float.TryParse(speedStr, NumberStyles.Float, CultureInfo.InvariantCulture, out var result))
		{
			Speed = result;
		}
	}
	private void ResetSpeedPitch()
	{
		Speed = 1f;
		Pitch = 0f;
	}
	private void ToggleSoundEffect(object? param)
	{
		if (param != null && int.TryParse(param.ToString(), out var result))
		{
			CurrentSoundEffect = ((CurrentSoundEffect == result) ? (-1) : result);
		}
	}
	private void SelectEqPreset(EqPreset? preset)
	{
		if (preset != null)
		{
			CurrentEqPreset = preset;
			Band0 = preset.BandGains[0];
			Band1 = preset.BandGains[1];
			Band2 = preset.BandGains[2];
			Band3 = preset.BandGains[3];
			Band4 = preset.BandGains[4];
			Band5 = preset.BandGains[5];
			Band6 = preset.BandGains[6];
			Band7 = preset.BandGains[7];
			Band8 = preset.BandGains[8];
			Band9 = preset.BandGains[9];
			_playback.Engine.SetAllBands(preset.BandGains);
			_settings.EqualizerPreset = preset.Name;
		}
	}
	private void ToggleShuffle()
	{
		_playback.ToggleShuffle();
		ShuffleEnabled = _playback.Engine.ShuffleEnabled;
	}
	private void ToggleRepeat()
	{
		_playback.ToggleRepeat();
		PlayMode = _playback.Engine.PlayMode;
	}
	private void ToggleFavorite()
	{
		if (CurrentTrack != null)
		{
			_db.ToggleFavorite(CurrentTrack.Id);
			CurrentTrack.IsFavorite = !CurrentTrack.IsFavorite;
			IsFavorite = CurrentTrack.IsFavorite;
			UpdateHomeCardCounts();
			if (CurrentView == "favorites")
			{
				LoadFavoriteTracks();
			}
		}
	}

	public void ToggleFavoriteForTrack(MusicTrack? track)
	{
		if (track != null)
		{
			_db.ToggleFavorite(track.Id);
			track.IsFavorite = !track.IsFavorite;
			UpdateHomeCardCounts();
			if (CurrentView == "favorites")
			{
				LoadFavoriteTracks();
			}
		}
	}
	private async Task ShuffleAllAsync()
	{
		List<MusicTrack> list = await Task.Run(() => _db.GetAllTracks());
		if (list.Count != 0)
		{
			Random rng = new Random();
			List<MusicTrack> list2 = list.OrderBy((MusicTrack _) => rng.Next()).ToList();
			_playback.PlayList(list2);
			CurrentTrack = list2[0];
			IsPlaying = true;
			ShuffleEnabled = true;
			_playback.Engine.ShuffleEnabled = true;
		}
	}
	private void ToggleSelectMode()
	{
		ShowOverflowMenu = false;
		IsSelectMode = !IsSelectMode;
	}
	private void PlayTrack(MusicTrack? track)
	{
		MusicTrack track2 = track;
		if (track2 != null)
		{
			int currentManagedThreadId = Environment.CurrentManagedThreadId;
			LogHelper.LogInfo($"[PLAYBACK] UI Thread={currentManagedThreadId}: Click track '{track2.Title}' (Id={track2.Id})");
			long num = UtilSecondTimestamp();
			List<MusicTrack> list = GetContextTracks();
			if (list.Count == 0 || !list.Any((MusicTrack t) => t.Id == track2.Id))
			{
				list = ((Tracks.Count > 0) ? Tracks.ToList() : new List<MusicTrack> { track2 });
			}
			long num2 = UtilSecondTimestamp();
			int num3 = list.FindIndex((MusicTrack t) => t.Id == track2.Id);
			long num4 = UtilSecondTimestamp();
			_playback.PlayList(list, (num3 >= 0) ? num3 : 0);
			long num5 = UtilSecondTimestamp();
			CurrentTrack = track2;
			IsPlaying = true;
			LoadLyrics(track2);
			long num6 = UtilSecondTimestamp();
			LogHelper.LogInfo($"[PLAYBACK] UI Thread={currentManagedThreadId}: PlayTrack synchronous took {num6 - num}ms (context={num2 - num}ms find={num4 - num2}ms playList={num5 - num4}ms ui/meta={num6 - num5}ms)");
		}
	}

	private static long UtilSecondTimestamp()
	{
		return DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
	}
	private void OnHomeCardClick(HomeCard? card)
	{
		if (card != null)
		{
			Navigate(card.TargetView);
		}
	}

	public void NavigateToFolder(string folderPath)
	{
		// Never record a consecutive duplicate: popping back to the view we are
		// already on notifies nothing (silent no-op GoBack) and strands later
		// backs — the ghost empty page. Mirrors Navigate().
		if (!string.Equals(CurrentView, "folder_detail", StringComparison.OrdinalIgnoreCase))
			_viewHistory.Push(CurrentView);
		CurrentView = "folder_detail";
		IsDrawerOpen = false;
		LoadFolderDetail(folderPath);
		OnPropertyChanged("CanNavigateBack");
	}

	public void NavigateToArtist(string artistName)
	{
		string prev = CurrentView;
		// See NavigateToFolder: no consecutive duplicates in history.
		if (!string.Equals(CurrentView, "artist_detail", StringComparison.OrdinalIgnoreCase))
			_viewHistory.Push(CurrentView);
		CurrentView = "artist_detail";
		LogHelper.LogInfo($"NavigateToArtist: '{artistName}' prevView='{prev}' newView='{CurrentView}'");
		IsDrawerOpen = false;
		LoadArtistDetail(artistName);
		OnPropertyChanged("CanNavigateBack");
	}

	public void NavigateToGenre(string genre)
	{
		// See NavigateToFolder: no consecutive duplicates in history.
		if (!string.Equals(CurrentView, "genre_detail", StringComparison.OrdinalIgnoreCase))
			_viewHistory.Push(CurrentView);
		CurrentView = "genre_detail";
		IsDrawerOpen = false;
		LoadGenreDetail(genre);
		OnPropertyChanged("CanNavigateBack");
	}

	public void NavigateToAlbum(string albumName, string artistName, MusicTrack? artSource = null)
	{
		// See NavigateToFolder: no consecutive duplicates in history.
		if (!string.Equals(CurrentView, "album_detail", StringComparison.OrdinalIgnoreCase))
			_viewHistory.Push(CurrentView);
		CurrentView = "album_detail";
		IsDrawerOpen = false;
		LoadAlbumDetail(albumName, artistName, artSource);
		OnPropertyChanged("CanNavigateBack");
	}
	private void Navigate(string page)
	{
		MusicPlayer.Views.SmoothScrollHelper.MarkInteraction();
		if (!string.Equals(CurrentView, page, StringComparison.OrdinalIgnoreCase))
		{
			_viewHistory.Push(CurrentView);
		}
		CurrentView = page;
		IsDrawerOpen = false;
		ShowSettings = page == "settings";
		switch (page)
		{
		case "favorites":
			if (!IsInitializing && FavoriteTracks.Count == 0)
			{
				LoadFavoriteTracks();
			}
			break;
		case "recent":
			if (!IsInitializing && RecentPlayTracks.Count == 0)
			{
				LoadRecentPlayTracks();
			}
			break;
		case "recent_add":
			if (!IsInitializing && RecentAddTracks.Count == 0)
			{
				LoadRecentAddTracks();
			}
			break;
		case "most_played":
			if (!IsInitializing && MostPlayedTracks.Count == 0)
			{
				LoadMostPlayedTracks();
			}
			break;
		case "albums":
			if (Albums.Count == 0)
			{
				LoadAlbums();
			}
			break;
		case "artists":
			if (Artists.Count == 0)
			{
				LoadArtists();
			}
			break;
		case "genres":
			if (Genres.Count == 0)
			{
				LoadGenres();
			}
			break;
		case "folders":
			if (Folders.Count == 0)
			{
				LoadFolders();
			}
			break;
		case "songs":
			if (!IsInitializing && Tracks.Count == 0)
			{
				LoadLibrary();
			}
			break;
		case "playlists":
			if (!IsInitializing && Playlists.Count == 0)
			{
				LoadPlaylists();
			}
			break;
		case "theme":
			LoadWallpapers();
			break;
		}
		OnPropertyChanged("CanNavigateBack");
	}
	private void ShowEqualizerPanel()
	{
		IsDrawerOpen = false;
		ShowEqualizer = !ShowEqualizer;
	}
	private void ShowSoundEffectsPanel()
	{
		IsDrawerOpen = false;
		ShowSoundEffects = !ShowSoundEffects;
	}
	private void ShowSpeedPitchPanel()
	{
		ShowSpeedPitch = !ShowSpeedPitch;
	}
	private void ShowLyricsPanel()
	{
		ShowLyrics = !ShowLyrics;
	}
	private void ShowSettingsPanel()
	{
		Navigate("settings");
	}
	private void ShowQueuePanel()
	{
		ShowQueue = !ShowQueue;
	}
	private void SwitchArtistDetailTab(string tab)
	{
		ArtistDetailActiveTab = tab;
	}
	private void PlayArtist()
	{
		if (ArtistDetailTracks.Count != 0)
		{
			PlayTrackCommand.Execute(ArtistDetailTracks[0]);
		}
	}
	private void ShuffleArtist()
	{
		PlayShuffled(ArtistDetailTracks);
	}
	private void AddArtistToQueue()
	{
		EnqueueTracks(ArtistDetailTracks);
	}
	private void PlayAlbum()
	{
		if (AlbumDetailTracks.Count != 0)
		{
			PlayTrackCommand.Execute(AlbumDetailTracks[0]);
		}
	}
	private void ShuffleAlbum()
	{
		PlayShuffled(AlbumDetailTracks);
	}
	private void AddAlbumToQueue()
	{
		EnqueueTracks(AlbumDetailTracks);
	}
	private void PlayPlaylistDetail()
	{
		if (PlaylistDetailTracks.Count != 0)
		{
			PlayTrackCommand.Execute(PlaylistDetailTracks[0]);
		}
	}
	private void ShufflePlaylistDetail()
	{
		PlayShuffled(PlaylistDetailTracks);
	}

	private void PlayGenreDetail()
	{
		if (GenreDetailTracks.Count != 0)
		{
			PlayTrackCommand.Execute(GenreDetailTracks[0]);
		}
	}

	private void ShuffleGenreDetail()
	{
		PlayShuffled(GenreDetailTracks);
	}

	private void AddPlaylistDetailToQueue()
	{
		EnqueueTracks(PlaylistDetailTracks);
	}
	private void RemoveTrackFromPlaylist(MusicTrack? track)
	{
		if (track == null || CurrentPlaylistId <= 0) return;
		_db.RemoveTrackFromPlaylist(CurrentPlaylistId, track.Id);
		PlaylistDetailTracks.Remove(track);
		PlaylistDetailTrackCount = PlaylistDetailTracks.Count;
		OnPropertyChanged("PlaylistDetailTrackCount");
		LoadPlaylists();
		ShowStatusBar($"Removed '{track.Title}' from playlist");
	}
	private async Task AddManagedFolderAsync()
	{
		OpenFolderDialog openFolderDialog = new OpenFolderDialog
		{
			Title = "Select Music Folder"
		};
		if (!openFolderDialog.ShowDialog().GetValueOrDefault())
		{
			return;
		}
		string folderPath = openFolderDialog.FolderName;
		string text = Path.GetFileName(folderPath);
		if (string.IsNullOrEmpty(text))
		{
			text = folderPath;
		}
		if (_db.GetManagedFolder(folderPath) != null)
		{
			MessageBox.Show("This folder is already added.", "Folder Exists", MessageBoxButton.OK, MessageBoxImage.Asterisk);
			return;
		}
		ManagedMusicFolder folder = new ManagedMusicFolder
		{
			Path = folderPath,
			Name = text,
			Enabled = true,
			AddedDate = DateTimeOffset.UtcNow.ToUnixTimeSeconds(),
			LastScanDate = 0L
		};
		_db.AddManagedFolder(folder);
		StatusBarMessage = $"Scanning folder: {text}...";
		IsStatusBarVisible = true;
		IsStatusBarHiddenLinkVisible = false;
		try
		{
			await _scanner.ScanFolderAsync(folderPath);
			ManagedMusicFolder managedFolder = _db.GetManagedFolder(folderPath);
			if (managedFolder != null)
			{
				_db.UpdateManagedFolderLastScan(managedFolder.Id);
			}
		}
		catch (Exception ex)
		{
			_logger.LogError(ex, "Failed to scan folder: {Folder}", folderPath);
			StatusBarMessage = "Scan failed";
			MessageBox.Show("Failed to scan folder:\n" + ex.Message, "Scan Error", MessageBoxButton.OK, MessageBoxImage.Hand);
		}
		LoadFolders();
		LoadLibraryFull();
		StatusBarMessage = "Done";
		var doneTimer = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromMilliseconds(1500) };
		doneTimer.Tick += (_, _) =>
		{
			doneTimer.Stop();
			IsStatusBarVisible = false;
			IsStatusBarHiddenLinkVisible = false;
		};
		doneTimer.Start();
		UpdateHomeCardCounts();
	}
	private async Task RemoveManagedFolder(FolderItem? folder)
	{
		FolderItem folder2 = folder;
		if (folder2 == null || !folder2.IsManaged || MessageBox.Show("Remove \"" + folder2.Name + "\" from library?\n\nFiles will NOT be deleted from disk.", "Remove Folder", MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes)
		{
			return;
		}
		try
		{
			await Task.Run(delegate
			{
				_db.RemoveManagedFolder(folder2.Id);
				_db.CleanOrphanTracks();
			});
		}
		catch (Exception exception)
		{
			_logger.LogError(exception, "Failed to remove managed folder: {Folder}", folder2.Name);
		}
		// always clean any queued tracks that belonged to the removed folder
		try
		{
			var q = _playback.Engine.Queue.ToList();
			for (int i = q.Count - 1; i >= 0; i--)
			{
				if (!string.IsNullOrEmpty(q[i].FilePath) && q[i].FilePath.StartsWith(folder2.Path, StringComparison.OrdinalIgnoreCase))
					_playback.Engine.RemoveFromQueue(i);
			}
			RefreshQueue();
			SaveQueueState();
		}
		catch { }
		// if the current track was in the deleted folder, or the library is now empty, clear the player completely
		bool isCurrentInDeletedFolder = CurrentTrack != null && !string.IsNullOrEmpty(CurrentTrack.FilePath) && CurrentTrack.FilePath.StartsWith(folder2.Path, StringComparison.OrdinalIgnoreCase);
		int remainingCount = 0;
		try { remainingCount = _db.GetManagedFolderTrackCount(); } catch { }
		if (isCurrentInDeletedFolder || remainingCount == 0)
		{
			try { _playback.Engine.Stop(); } catch { }
			try { _playback.Engine.ClearQueue(); } catch { }
			CurrentTrack = null;
			IsPlaying = false;
			Position = TimeSpan.Zero;
			Duration = TimeSpan.Zero;
		}
		LoadFolders();
		LoadLibraryFull();
		UpdateHomeCardCounts();
	}
	private void NavigateToPlaylists()
	{
		Navigate("playlists");
	}
	private void ToggleSearch()
	{
		IsSearching = !IsSearching;
	}
	private void ToggleMute()
	{
		if (Volume > 0.5f)
		{
			_previousVolumeBeforeMute = Volume;
			Volume = 0;
		}
		else
		{
			Volume = _previousVolumeBeforeMute > 0.5f ? _previousVolumeBeforeMute : 80f;
		}
	}
	private void Search()
	{
		ObservableCollection<MusicTrack> currentViewSource = GetCurrentViewSource();
		if (string.IsNullOrWhiteSpace(SearchQuery))
		{
			SearchResults.Clear();
			OnPropertyChanged("DisplayedTracks");
			OnPropertyChanged("HasNoMusic");
			return;
		}
		string q = SearchQuery.Trim();
		List<MusicTrack> items = currentViewSource.Where((MusicTrack t) => (!string.IsNullOrEmpty(t.Title) && t.Title.Contains(q, StringComparison.OrdinalIgnoreCase)) || (!string.IsNullOrEmpty(t.Artist) && t.Artist.Contains(q, StringComparison.OrdinalIgnoreCase)) || (!string.IsNullOrEmpty(t.Album) && t.Album.Contains(q, StringComparison.OrdinalIgnoreCase))).ToList();
		SearchResults.ReplaceAll(items);
		OnPropertyChanged("DisplayedTracks");
		OnPropertyChanged("HasNoMusic");
	}
	private void PlayPlaylist(PlaylistViewModel? playlist)
	{
		if (playlist != null)
		{
			List<MusicTrack> playlistTracks = _db.GetPlaylistTracks(playlist.Id);
			if (playlistTracks.Count > 0)
			{
				_playback.PlayList(playlistTracks);
				CurrentTrack = playlistTracks[0];
				IsPlaying = true;
			}
		}
	}
	private void CreatePlaylist(string? name)
	{
		if (!string.IsNullOrWhiteSpace(name))
		{
			_db.InsertPlaylist(new Playlist
			{
				Name = name,
				DateCreated = DateTime.Now.ToBinary(),
				DateModified = DateTime.Now.ToBinary()
			});
			LoadPlaylists();
		}
	}
	private void AddToQueue(MusicTrack? track)
	{
		if (track != null)
		{
			_playback.Engine.AddToQueue(track);
			RefreshQueue();
			SaveQueueState();
		}
	}
	private void EnqueueTracks(IEnumerable<MusicTrack> tracks)
	{
		bool added = false;
		foreach (MusicTrack t in tracks)
		{
			if (_playback.Engine.Queue.Any((MusicTrack q) => q.Id == t.Id))
			{
				continue;
			}
			_playback.Engine.AddToQueue(t);
			added = true;
		}
		if (added)
		{
			RefreshQueue();
			SaveQueueState();
		}
	}
	private void PlayShuffled(IReadOnlyCollection<MusicTrack> tracks)
	{
		if (tracks.Count == 0) return;
		List<MusicTrack> items = tracks.OrderBy((MusicTrack _) => Guid.NewGuid()).ToList();
		Queue.ReplaceAll(items);
		_playback.Engine.SetQueue(items, 0);
		_playback.Engine.LoadAndPlayAsync(items[0]);
		CurrentTrack = items[0];
		IsPlaying = true;
		LoadLyrics(items[0]);
		SaveQueueState();
	}
	private void RemoveFromQueue(MusicTrack? track)
	{
		MusicTrack track2 = track;
		if (track2 == null) return;
		bool isPlaying = CurrentTrack != null && CurrentTrack.Id == track2.Id;
		int num = _playback.Engine.Queue.ToList().FindIndex((MusicTrack t) => t.Id == track2.Id);
		if (num < 0) return;
		_playback.Engine.RemoveFromQueue(num);
		RefreshQueue();
		SaveQueueState();
		if (!isPlaying) return;
		if (_playback.Engine.Queue.Count > 0)
		{
			int nextIdx = Math.Max(0, Math.Min(_playback.Engine.CurrentIndex, _playback.Engine.Queue.Count - 1));
			MusicTrack next = _playback.Engine.Queue[nextIdx];
			_playback.Engine.LoadAndPlayAsync(next);
			CurrentTrack = next;
			IsPlaying = true;
			LoadLyrics(next);
			RefreshQueue();
		}
		else
		{
			_playback.Engine.Stop();
			CurrentTrack = null;
			IsPlaying = false;
		}
	}

	// Moves a queue row (drag & drop / move buttons) keeping the engine (single
	// source of truth) and the UI collection in lock-step. When saveState is
	// false (live drag), no DB write happens — callers save once on drop.
	public void MoveQueueItem(int oldIndex, int newIndex, bool saveState = true)
	{
		if (oldIndex == newIndex) return;
		if (oldIndex < 0 || newIndex < 0) return;
		if (oldIndex >= Queue.Count || newIndex >= Queue.Count) return;
		if (_playback.Engine.MoveQueueItem(oldIndex, newIndex))
		{
			Queue.Move(oldIndex, newIndex);
			if (saveState) SaveQueueState();
		}
	}

	public void FinalizeQueueDrag()
	{
		SaveQueueState();
	}
	private void ClearQueue()
	{
		_playback.Engine.ClearQueue();
		RefreshQueue();
		SaveQueueState();
	}
	private void ClearUpcomingQueue()
	{
		if (CurrentTrack != null)
		{
			var current = CurrentTrack;
			_playback.Engine.SetQueue(new List<MusicTrack> { current }, 0);
			RefreshQueue();
			SaveQueueState();
		}
		else
		{
			ClearQueue();
		}
	}
	private void MoveQueueUp(MusicTrack? track)
	{
		if (track == null) return;
		int num = Queue.IndexOf(track);
		if (num > 0) MoveQueueItem(num, num - 1);
	}
	private void MoveQueueDown(MusicTrack? track)
	{
		if (track == null) return;
		int num = Queue.IndexOf(track);
		if (num >= 0 && num < Queue.Count - 1) MoveQueueItem(num, num + 1);
	}
	private void PlayQueueTrack(MusicTrack? track)
	{
		if (track != null)
		{
			int num = Queue.IndexOf(track);
			if (num >= 0)
			{
				_playback.Engine.SetQueue(Queue.ToList(), num);
				_playback.Engine.LoadAndPlayAsync(track);
				CurrentTrack = track;
				IsPlaying = true;
				LoadLyrics(track);
				SaveQueueState();
			}
		}
	}
	private void SaveQueueAsPlaylist()
	{
		if (Queue.Count == 0)
		{
			return;
		}
		NewPlaylistDialog newPlaylistDialog = new NewPlaylistDialog($"Queue {DateTime.Now:MM-dd HH:mm}", "Save Queue as Playlist", "Save")
		{
			Owner = Application.Current.MainWindow
		};
		if (newPlaylistDialog.ShowDialog().GetValueOrDefault())
		{
			Playlist pl = new Playlist
			{
				Name = newPlaylistDialog.PlaylistName,
				DateCreated = DateTime.Now.ToBinary(),
				DateModified = DateTime.Now.ToBinary(),
				SortOrder = Playlists.Count
			};
			_db.InsertPlaylist(pl);
			int playlistId = _db.GetAllPlaylists().FirstOrDefault((Playlist p) => p.Name == pl.Name)?.Id ?? pl.Id;
			for (int i = 0; i < Queue.Count; i++)
			{
				_db.AddTrackToPlaylist(playlistId, Queue[i].Id, i);
			}
			LoadPlaylists();
			MessageBox.Show($"Saved {Queue.Count} tracks to '{newPlaylistDialog.PlaylistName}'", "Queue");
		}
	}

	public void SyncEngineQueue() { SyncEngineQueueInternal(); }

	private void SyncEngineQueueInternal()
	{
		MusicTrack current = _playback.Engine.CurrentTrack;
		List<MusicTrack> tracks = Queue.ToList();
		int index = (current != null) ? tracks.FindIndex((MusicTrack t) => t.Id == current.Id) : _playback.Engine.CurrentIndex;
		if (index < 0) index = _playback.Engine.CurrentIndex;
		_playback.Engine.SetQueue(tracks, index);
		SaveQueueState();
	}

	private void SaveQueueState()
	{
		List<QueueItem> items = Queue.Select((MusicTrack t, int i) => new QueueItem
		{
			TrackId = t.Id,
			Position = i
		}).ToList();
		_db.SaveQueue(items);
	}

	private void RestoreQueueOnStartup()
	{
		try
		{
			List<MusicTrack> saved = _db.GetQueue();
			if (saved == null || saved.Count == 0) return;
			_playback.Engine.SetQueue(saved, -1);
			Queue.ReplaceAll(saved);
			LogHelper.LogInfo($"[Queue] Restored {saved.Count} tracks from database");
		}
		catch { }
	}

	private void RefreshQueue()
	{
		IReadOnlyList<MusicTrack> queue = _playback.Engine.Queue;
		if (Queue.Count != queue.Count || !SameTrackOrder(Queue, queue))
		{
			Queue.ReplaceAll(queue);
		}
		UpdateQueueSummary();
		ApplyQueueFilter();
	}

	private static bool SameTrackOrder(IReadOnlyList<MusicTrack> a, IReadOnlyList<MusicTrack> b)
	{
		if (a.Count != b.Count)
		{
			return false;
		}
		for (int i = 0; i < a.Count; i++)
		{
			MusicTrack musicTrack = a[i];
			MusicTrack musicTrack2 = b[i];
			if (musicTrack != musicTrack2 && musicTrack.Id != musicTrack2.Id)
			{
				return false;
			}
		}
		return true;
	}

	public double GetWindowSetting(string key, double defaultValue)
	{
		if (!double.TryParse(_db.GetSetting(key, string.Empty), out var result))
		{
			return defaultValue;
		}
		return result;
	}

	public bool GetWindowSettingBool(string key, bool defaultValue)
	{
		return _db.GetSettingBool(key, defaultValue);
	}

	public void SetWindowSetting(string key, double value)
	{
		_db.SetSetting(key, value.ToString("R"));
	}

	public void SetWindowSetting(string key, bool value)
	{
		_db.SetSetting(key, value.ToString());
	}

	private void LoadSettings()
	{
		_settings.LoadAll();
		_settings.SettingsChanged += OnSettingsChanged;
		LibrarySortOrder = _settings.LibraryOrder;
		IsLibrarySortDescending = _db.GetSettingBool("pref_library_sort_descending");
		Volume = _settings.Volume * 100f;
		Balance = _settings.Balance;
		Speed = _settings.Speed;
		Pitch = _settings.Pitch;
		ShuffleEnabled = _settings.Shuffle;
		PlayMode = (PlayMode)_settings.RepeatMode;
		EqEnabled = _settings.EqualizerEnabled;
		BassBoostEnabled = _settings.BassBoostEnabled;
		BassBoostStrength = _settings.BassBoostStrength;
		VirtualizerEnabled = _settings.VirtualizerEnabled;
		VirtualizerStrength = _settings.VirtualizerStrength;
		_isApplyingEqPreset = true;
		try
		{
			string presetName = _db.GetSetting("pref_eq_preset", "Normal");
			var preset = EqPresets.FirstOrDefault((EqPreset p) => p.Name == presetName) ?? EqPresets.FirstOrDefault((EqPreset p) => p.Name == "Normal") ?? EqPresets[0];
			_currentEqPreset = preset;
			OnPropertyChanged(nameof(CurrentEqPreset));

			float[] initialGains = new float[10];
			if (preset.Name == "Custom")
			{
				for (int i = 0; i < 10; i++)
				{
					var s = _db.GetSetting($"pref_eq_band_{i}", "0");
					if (float.TryParse(s, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var v))
					{
						initialGains[i] = Math.Clamp(v, -15f, 15f);
					}
				}
			}
			else
			{
				Array.Copy(preset.BandGains, initialGains, 10);
			}

			ApplyBandsInternal(initialGains);
		}
		finally
		{
			_isApplyingEqPreset = false;
		}
		CurrentSoundEffect = _settings.SoundEffect;
		ReverbPreset = (ReverbPreset)_db.GetSettingInt("pref_reverb_preset");
		ReverbEnabled = _db.GetSettingBool("pref_reverb_enabled");
		LoudnessEnabled = _db.GetSettingBool("pref_loudness_enabled");
		LoudnessGain = _db.GetSettingFloat("pref_loudness_gain", 6f);
		_playback.Engine.Volume = Volume / 100f;
		_playback.Engine.Balance = Balance;
		_playback.Engine.Speed = Speed;
		_playback.Engine.Pitch = Pitch;
		_playback.Engine.ShuffleEnabled = ShuffleEnabled;
		_playback.Engine.PlayMode = PlayMode;
		_playback.Engine.EqualizerEnabled = EqEnabled;
		_playback.Engine.BassBoostEnabled = BassBoostEnabled;
		_playback.Engine.BassBoostStrength = BassBoostStrength;
		_playback.Engine.VirtualizerEnabled = VirtualizerEnabled;
		_playback.Engine.VirtualizerStrength = VirtualizerStrength;
		_playback.Engine.ReverbPreset = ReverbPreset;
		_playback.Engine.ReverbEnabled = ReverbEnabled;
		_playback.Engine.LoudnessEnabled = LoudnessEnabled;
		_playback.Engine.LoudnessGain = LoudnessGain;
		_playback.Engine.ActiveSoundEffect = (SoundEffectType)CurrentSoundEffect;
		if (_settings.OutputDevice >= 0)
		{
			_playback.Engine.DeviceNumber = _settings.OutputDevice;
		}
		string savedWp = _db.GetSetting("app_wallpaper", "");
		if (!string.IsNullOrEmpty(savedWp) && (savedWp.StartsWith("pack:") || System.IO.File.Exists(savedWp)))
		{
			BackgroundImagePath = savedWp;
		}
		string savedBlur = _db.GetSetting("app_wallpaper_blur", "0");
		if (double.TryParse(savedBlur, System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out double bVal))
		{
			WallpaperBlur = Math.Clamp(bVal, 0.0, 50.0);
		}
		string savedAlpha = _db.GetSetting("app_wallpaper_alpha", "51");
		if (double.TryParse(savedAlpha, System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out double aVal))
		{
			WallpaperAlpha = Math.Clamp(aVal, 0.0, 255.0);
		}
		ApplySettingEffects();
	}

	private void OnSettingsChanged(string propertyName)
	{
		if (propertyName == null)
		{
			return;
		}
		switch (propertyName.Length)
		{
		case 17:
			switch (propertyName[4])
			{
			default:
				return;
			case 'H':
				if (!(propertyName == "ShowHiddenFolders"))
				{
					return;
				}
				break;
			case 'r':
				if (!(propertyName == "IgnoreShortTracks"))
				{
					return;
				}
				break;
			case 'S':
				if (propertyName == "ShowShuffleButton")
				{
					OnPropertyChanged("IsShuffleButtonVisible");
				}
				return;
			case 's':
				if (propertyName == "CrossfadeDuration")
				{
					_playback.Engine.CrossfadeDuration = _settings.CrossfadeDuration;
				}
				return;
			case 'D':
				if (propertyName == "ShowDesktopLyrics")
				{
					OnPropertyChanged("IsDesktopLyricsVisible");
				}
				return;
			}
			goto IL_01fe;
		case 15:
			switch (propertyName[0])
			{
			default:
				return;
			case 'S':
				break;
			case 'G':
				if (propertyName == "GaplessPlayback")
				{
					_playback.Engine.GaplessPlayback = _settings.GaplessPlayback;
				}
				return;
			}
			if (!(propertyName == "ShowDirectories"))
			{
				break;
			}
			goto IL_01fe;
		case 13:
			switch (propertyName[0])
			{
			default:
				return;
			case 'E':
				break;
			case 'C':
				if (propertyName == "ClickAddQueue")
				{
					OnPropertyChanged("ClickAddQueueEnabled");
				}
				return;
			}
			if (!(propertyName == "ExcludeBySize"))
			{
				break;
			}
			goto IL_01fe;
		case 19:
			switch (propertyName[0])
			{
			case 'S':
				if (propertyName == "ShowForwardBackward")
				{
					OnPropertyChanged("IsForwardBackwardVisible");
				}
				break;
			case 'T':
				if (propertyName == "TimeForwardBackward")
				{
					OnPropertyChanged("SeekDuration");
				}
				break;
			case 'P':
				if (propertyName == "PlaylistAddPosition")
				{
					OnPropertyChanged("PlaylistAddPositionMode");
				}
				break;
			case 'Q':
			case 'R':
				break;
			}
			break;
		case 10:
			switch (propertyName[0])
			{
			case 'V':
				if (propertyName == "VolumeFade")
				{
					_playback.Engine.VolumeFade = _settings.VolumeFade;
				}
				break;
			case 'S':
				if (propertyName == "ShowLyrics")
				{
					OnPropertyChanged("IsLyricsVisible");
				}
				break;
			case 'R':
				if (propertyName == "ReplaySong")
				{
					_playback.Engine.ReplaySong = _settings.ReplaySong;
				}
				break;
			case 'T':
			case 'U':
				break;
			}
			break;
		case 18:
			if (propertyName == "PlaylistTrackLimit")
			{
				OnPropertyChanged("PlaylistTrackLimitDisplay");
			}
			break;
		case 11:
		case 12:
		case 14:
		case 16:
			break;
			IL_01fe:
			LoadLibrary();
			break;
		}
	}

	private void ApplySettingEffects()
	{
		_playback.Engine.GaplessPlayback = _settings.GaplessPlayback;
		_playback.Engine.CrossfadeDuration = _settings.CrossfadeDuration;
		_playback.Engine.VolumeFade = _settings.VolumeFade;
		_playback.Engine.ReplaySong = _settings.ReplaySong;
	}

	public async void LoadLibrary()
	{
		try
		{
			List<MusicTrack> tracks;
			(int TrackCount, int FolderCount, int FavoriteCount, int RecentPlayCount, int RecentAddCount, int MostPlayedCount) counts;
			(tracks, counts) = await Task.Run(() => (_db.GetManagedFolderTracksSorted((LibrarySortOrder == "date_added") ? "date_added" : ((LibrarySortOrder == "duration") ? "duration" : ((LibrarySortOrder == "size") ? "size" : ((LibrarySortOrder == "year") ? "year" : "title"))), (LibrarySortOrder == "date_added") ? (!IsLibrarySortDescending) : IsLibrarySortDescending).Take(5000).ToList(), _db.GetAllCounts()));
			await DispatchAsync(delegate
			{
				Tracks.ReplaceAll(tracks);
				if (HomeCards.Count >= 6)
				{
					HomeCards[0].Count = counts.TrackCount.ToString();
					HomeCards[1].Count = counts.FolderCount.ToString();
					HomeCards[2].Count = counts.FavoriteCount.ToString();
					HomeCards[3].Count = counts.RecentPlayCount.ToString();
					HomeCards[4].Count = counts.RecentAddCount.ToString();
					HomeCards[5].Count = counts.MostPlayedCount.ToString();
				}
			});
			await _albumArt.WarmUpArtCacheAsync(tracks);
			await DispatchAsync(delegate
			{
				OnPropertyChanged("DisplayedTracks");
			});
		}
		catch
		{
		}
	}

	public void LoadLibraryFull()
	{
		LoadLibrary();
		LoadAlbums();
		_preloadedArtistResult = null;
		LoadArtists();
		LoadGenres();
		LoadFolders();
		LoadPlaylists();
	}

	public async void UpdateHomeCardCounts()
	{
		try
		{
			if (HomeCards.Count < 6)
			{
				_logger.LogWarning("UpdateHomeCardCounts: HomeCards.Count={Count}, skipping", HomeCards.Count);
				return;
			}
			(int TrackCount, int FolderCount, int FavoriteCount, int RecentPlayCount, int RecentAddCount, int MostPlayedCount) counts = await Task.Run(() => _db.GetAllCounts());
			_logger.LogInformation("UpdateHomeCardCounts: Tracks={TrackCount}, Folders={FolderCount}, Favorites={FavoriteCount}, RecentPlay={RecentPlayCount}, RecentAdd={RecentAddCount}, MostPlayed={MostPlayedCount}", counts.TrackCount, counts.FolderCount, counts.FavoriteCount, counts.RecentPlayCount, counts.RecentAddCount, counts.MostPlayedCount);
			await DispatchAsync(delegate
			{
				if (HomeCards.Count >= 6)
				{
					HomeCards[0].Count = counts.TrackCount.ToString();
					HomeCards[1].Count = counts.FolderCount.ToString();
					HomeCards[2].Count = counts.FavoriteCount.ToString();
					HomeCards[3].Count = counts.RecentPlayCount.ToString();
					HomeCards[4].Count = counts.RecentAddCount.ToString();
					HomeCards[5].Count = counts.MostPlayedCount.ToString();
				}
			});
		}
		catch (Exception ex)
		{
			_logger.LogError(ex, "UpdateHomeCardCounts failed");
		}
	}

	private async void LoadAlbums()
	{
		try
		{
			List<MusicTrack> items = await Task.Run(() => (from g in (from t in _db.GetAllTracks()
					where !string.IsNullOrWhiteSpace(t.Album)
					select t).GroupBy<MusicTrack, string>((MusicTrack t) => t.Album.Trim(), StringComparer.OrdinalIgnoreCase)
				select (DatabaseService.PickCanonicalTrack(g) ?? g.First()) into t
				orderby t.Album
				select t).ToList());
		// Sort BEFORE inserting: replacing the collection twice (insert, then
		// re-sort with a Reset) made the WrapPanel realize every tile twice.
		List<MusicTrack> sortedAlbums = items.Count >= 2 ? SortItems(items).ToList() : items;
		// Decode album art OFF the UI thread first — otherwise each fill chunk
		// decodes disk JPEGs inline (AlbumArtConverter), which lands in the
		// tab-switch window the user is already scrolling through and shows up
		// as dropped frames. After the warmup these are dictionary hits.
		await Task.Run(delegate
		{
			foreach (MusicTrack t in sortedAlbums)
			{
				AlbumArtLoader.Instance?.LoadMediumAlbumArt(t);
				AlbumArtLoader.Instance?.LoadAlbumArt(t);
			}
		});
		await ReplaceAllChunkedAsync(Albums, sortedAlbums);
		PerfTabData("albums", Albums.Count);
	}
	catch
	{
	}
}

private async void LoadArtists()
	{
		try
		{
			(List<MusicTrack>, List<KeyValuePair<string, string>>) tuple = _preloadedArtistResult ?? (await BuildArtistItemsAsync());
			(List<MusicTrack> Items, List<KeyValuePair<string, string>> CachedImages) result = tuple;
			_preloadedArtistResult = null;
			await DispatchAsync(delegate
			{
				foreach (KeyValuePair<string, string> item in result.CachedImages)
				{
					ArtistImages[item.Key] = item.Value;
				}
				OnPropertyChanged("ArtistImages");
			});
			// Pre-decode on a background thread any photo paths that are not yet
			// in ArtistImageCache (fetched mid-session results; startup-cached
			// ones were preloaded at launch) — the tile converter otherwise
			// decodes inline inside fill chunks on the UI thread.
			var undecoded = ArtistImages
				.Where((KeyValuePair<string, string> kv) => !string.IsNullOrWhiteSpace(kv.Value) && !ArtistImageCache.ContainsKey(kv.Key))
				.Select((KeyValuePair<string, string> kv) => kv)
				.ToList();
			if (undecoded.Count > 0)
				await Task.Run(delegate { PreloadArtistImages(undecoded); });
			// Sort BEFORE inserting (single pass) and fill progressively so the
			// WrapPanel never realizes all artist tiles inside one frame.
			List<MusicTrack> sortedArtists = result.Items.Count >= 2
				? SortItems(result.Items, isArtistCollection: true).ToList()
				: result.Items;
			var swFill = System.Diagnostics.Stopwatch.StartNew();
			await ReplaceAllChunkedAsync(Artists, sortedArtists);
			long fillMs = swFill.ElapsedMilliseconds;
			PerfTabData("artists", Artists.Count);
			var swKick = System.Diagnostics.Stopwatch.StartNew();
			_ = FetchMissingArtistImagesAsync(result.Items);
			if (Environment.GetEnvironmentVariable("MUSICPLAYER_PERFTEST") == "1")
				LogHelper.LogInfo($"TAB-ARTISTS fillMs={fillMs} fetchKickMs={swKick.ElapsedMilliseconds}");
		}
		catch
		{
		}
	}

	private Task<(List<MusicTrack> Items, List<KeyValuePair<string, string>> CachedImages)> BuildArtistItemsAsync()
	{
		return Task.Run(delegate
		{
			List<MusicTrack> list = (from t in (from t in _db.GetAllTracks()
					where !string.IsNullOrWhiteSpace(t.Artist)
					select t).GroupBy<MusicTrack, string>((MusicTrack t) => t.Artist.Trim(), StringComparer.OrdinalIgnoreCase).Select(delegate(IGrouping<string, MusicTrack> g)
				{
					MusicTrack musicTrack = (from t in g
						orderby t.Album, t.TrackNumber
						select t).First();
					musicTrack.Artist = g.Key;
					return musicTrack;
				})
				orderby t.Artist
				select t).ToList();
			List<KeyValuePair<string, string>> list2 = new List<KeyValuePair<string, string>>();
			foreach (MusicTrack item in list)
			{
				if (!string.IsNullOrWhiteSpace(item.Artist))
				{
					string cachedPath = _artistImages.GetCachedPath(item.Artist);
					if (cachedPath != null)
					{
						list2.Add(new KeyValuePair<string, string>(item.Artist, cachedPath));
					}
				}
			}
			return (Items: list, CachedImages: list2);
		});
	}

	private void PreloadArtistImages(List<KeyValuePair<string, string>> cachedImages)
	{
		try
		{
			foreach (KeyValuePair<string, string> cachedImage in cachedImages)
			{
				if (!string.IsNullOrWhiteSpace(cachedImage.Key) && !string.IsNullOrWhiteSpace(cachedImage.Value) && !ArtistImageCache.ContainsKey(cachedImage.Key))
				{
					try
					{
						BitmapImage bitmapImage = new BitmapImage();
						bitmapImage.BeginInit();
						bitmapImage.CacheOption = BitmapCacheOption.OnLoad;
						bitmapImage.DecodePixelWidth = 256;
						bitmapImage.UriSource = new Uri(cachedImage.Value, UriKind.Absolute);
						bitmapImage.EndInit();
						((Freezable)bitmapImage).Freeze();
						ArtistImageCache[cachedImage.Key] = bitmapImage;
					}
					catch
					{
					}
				}
			}
		}
		catch
		{
		}
	}

	private async Task FetchMissingArtistImagesAsync(List<MusicTrack> items)
	{
		try
		{
			// Filter on the calling (UI) thread — cheap dictionary/string reads.
			// The per-artist kick-off (file probes, regex, HTTP client setup) used
			// to run inline here: measured fetchKickMs=89 on the UI thread right
			// after a tab fill, which showed up as a ~118ms dropped frame.
			List<MusicTrack> missing = new List<MusicTrack>(items.Count);
			foreach (MusicTrack t in items)
			{
				if (string.IsNullOrWhiteSpace(t.Artist)) continue;
				if (ArtistImages.ContainsKey(t.Artist)) continue;
				if (IsUnknownArtist(t.Artist)) continue;
				if (t.Artist.Trim().Length <= 2) continue;
				missing.Add(t);
			}
			await Task.Run(async delegate
			{
				SemaphoreSlim sem = new SemaphoreSlim(6);
				await Task.WhenAll(missing.Select((Func<MusicTrack, Task>)async delegate(MusicTrack track)
				{
					await sem.WaitAsync();
					try
					{
						string path = await _artistImages.GetArtistImageAsync(track.Artist);
						if (path != null)
						{
							await DispatchAsync(delegate
							{
								if (!ArtistImages.ContainsKey(track.Artist))
									ArtistImages[track.Artist] = path;
							});
						}
					}
					catch
					{
					}
					finally
					{
						sem.Release();
					}
				}));
			});
		}
		catch
		{
		}
	}

	private static bool IsUnknownArtist(string? artist)
	{
		if (string.IsNullOrWhiteSpace(artist))
		{
			return true;
		}
		switch (artist.Trim().ToLowerInvariant())
		{
		case "unknown":
		case "various":
		case "<unknown>":
		case "unknown artist":
		case "various artists":
		case "none":
		case "n/a":
		case ".misc":
		case "instrumental":
		case "soundtrack":
			return true;
		default:
			return false;
		}
	}

	private async void LoadGenres()
	{
		try
		{
			List<MusicTrack> items = await Task.Run(() => (from g in (from t in _db.GetAllTracks()
					where !string.IsNullOrWhiteSpace(t.Genre)
					select t).GroupBy<MusicTrack, string>((MusicTrack t) => t.Genre.Trim(), StringComparer.OrdinalIgnoreCase)
				select (from t in g
					orderby t.Artist, t.Album
					select t).First() into t
				orderby t.Genre
				select t).ToList());
		await DispatchAsync(delegate
		{
			Genres.ReplaceAll(items);
			SortCollection(Genres);
		});
		PerfTabData("genres", Genres.Count);
	}
	catch
	{
	}
}

private async void LoadFolders()
	{
		try
		{
			List<FolderItem> folderItems = await Task.Run(delegate
			{
				List<ManagedMusicFolder> managedFolders = _db.GetManagedFolders();
				Dictionary<string, int> folderTrackCounts = _db.GetFolderTrackCounts();
				List<FolderItem> list = new List<FolderItem>();
				foreach (ManagedMusicFolder item in managedFolders)
				{
					int valueOrDefault = folderTrackCounts.GetValueOrDefault(item.Path, 0);
					list.Add(new FolderItem
					{
						Name = item.Name,
						Path = item.Path,
						TrackCount = valueOrDefault,
						Id = item.Id,
						IsManaged = true
					});
				}
				return list;
			});
		await DispatchAsync(delegate
		{
			Folders.ReplaceAll(folderItems);
			SortFolders();
		});
		PerfTabData("folders", Folders.Count);
	}
	catch
	{
	}
}

	private async void LoadFolderDetail(string folderPath)
	{
		string folderPath2 = folderPath;
		try
		{
			List<MusicTrack> tracks = await Task.Run(() => _db.GetFolderTracks(folderPath2).ToList());
			string folderName = Path.GetFileName(folderPath2);
			if (string.IsNullOrEmpty(folderName))
			{
				folderName = folderPath2;
			}
			await DispatchAsync(delegate
			{
				CurrentFolderName = folderName;
				FolderDetailTracks.ReplaceAll(tracks);
			});
		}
		catch
		{
		}
	}

	private async void LoadArtistDetail(string artistName)
	{
		string artistName2 = artistName;
		try
		{
			CurrentArtistName = artistName2;
			List<MusicTrack> tracks = await Task.Run(() => _db.GetArtistDetailTracks(artistName2));
			List<MusicTrack> albums = (from t in tracks
				where !string.IsNullOrEmpty(t.Album)
				group t by t.Album into g
				select g.First()).ToList();
			// Resolve canonical covers off the UI thread BEFORE publishing: the
			// card bindings evaluate synchronously at realization and must never
			// hit the database (a full-table scan per card = dropped frames).
			await Task.Run(() =>
			{
				List<MusicTrack> all = _db.GetAllTracks();
				AlbumCoverTracks.Clear();
				foreach (string rawKey in albums.Select(a => a.Album).Distinct(StringComparer.OrdinalIgnoreCase))
				{
					string key = (rawKey ?? string.Empty).Trim();
					if (string.IsNullOrEmpty(key)) continue;
					MusicTrack? cover = DatabaseService.PickCanonicalTrack(
						all.Where(t => string.Equals(t.Album?.Trim(), key, StringComparison.OrdinalIgnoreCase)));
					if (cover != null) AlbumCoverTracks[key] = cover;
				}
			});
			await DispatchAsync(delegate
			{
				ArtistDetailActiveTab = "albums";
				ArtistDetailTracks.ReplaceAll(tracks);
				ArtistAlbums.ReplaceAll(albums);
				ArtistAlbumCount = albums.Count;
				ArtistTrackCount = tracks.Count;
				LogHelper.LogInfo($"LoadArtistDetail: '{artistName2}' tracks={tracks.Count} albums={albums.Count}");
				ArtistCollage1 = ((albums.Count > 0) ? albums[0].AlbumArtPath : string.Empty);
				ArtistCollage2 = ((albums.Count > 1) ? albums[1].AlbumArtPath : string.Empty);
				ArtistCollage3 = ((albums.Count > 2) ? albums[2].AlbumArtPath : string.Empty);
				ArtistCollage4 = ((albums.Count > 3) ? albums[3].AlbumArtPath : string.Empty);
				OnPropertyChanged("ArtistCollage1");
				OnPropertyChanged("ArtistCollage2");
				OnPropertyChanged("ArtistCollage3");
				OnPropertyChanged("ArtistCollage4");
			});
			if (ArtistImages.ContainsKey(artistName2))
			{
				return;
			}
			string cached = await Task.Run(() => _artistImages.GetCachedPath(artistName2));
			if (cached != null)
			{
				await DispatchAsync(delegate
				{
					ArtistImages[artistName2] = cached;
					OnPropertyChanged("ArtistImages");
				});
				return;
			}
			string fetched = await _artistImages.GetArtistImageAsync(artistName2);
			if (fetched != null)
			{
				await DispatchAsync(delegate
				{
					ArtistImages[artistName2] = fetched;
					OnPropertyChanged("ArtistImages");
				});
			}
		}
		catch
		{
		}
	}

	private void LoadGenreDetail(string genre)
	{
		CurrentGenreName = genre;
		List<MusicTrack> tracks = _db.GetGenreTracks(genre);
		for (int i = 0; i < tracks.Count; i++)
			tracks[i].TrackNumber = i + 1;
		GenreDetailTracks.ReplaceAll(tracks);
	}

	private async void LoadAlbumDetail(string albumName, string artistName, MusicTrack? artSource = null)
	{
		string albumName2 = albumName;
		string artistName2 = artistName;
		try
		{
			// Publish the title synchronously so the detail page shows the right
			// header on the very first frame of the hero morph — the DB query below
			// is async and would otherwise let the previous album's title linger.
			if (!string.Equals(CurrentAlbumName, albumName2, StringComparison.Ordinal))
			{
				CurrentAlbumName = albumName2;
				OnPropertyChanged("CurrentAlbumName");
			}
			// Clear the rest of the stale album-detail state synchronously too:
			// non-hero entries (artist-page / list rows) reveal the page instantly,
			// and the async DB query lands ~3 frames later — long enough to flash
			// the PREVIOUS album's artist, cover and tracks (seen when two albums
			// share a title, e.g. two "Unknown Album"s by different artists).
			// The clicked card's own track (same album) is published as provisional
			// art when available; otherwise the art area falls back to the neutral
			// placeholder — never a wrong cover.
			bool sameTarget = string.Equals(CurrentAlbumName, albumName2, StringComparison.Ordinal)
				&& !string.IsNullOrEmpty(artistName2)
				&& string.Equals(CurrentAlbumArtist, artistName2, StringComparison.Ordinal)
				&& AlbumDetailTracks.Count > 0;
			if (!sameTarget)
			{
				string artistSync = artistName2 ?? string.Empty;
				if (!string.Equals(CurrentAlbumArtist, artistSync, StringComparison.Ordinal))
				{
					CurrentAlbumArtist = artistSync;
					OnPropertyChanged("CurrentAlbumArtist");
				}
				if (!string.IsNullOrEmpty(CurrentAlbumYear))
				{
					CurrentAlbumYear = string.Empty;
					OnPropertyChanged("CurrentAlbumYear");
				}
				if (!ReferenceEquals(CurrentAlbumArtTrack, artSource))
				{
					CurrentAlbumArtTrack = artSource;
					OnPropertyChanged("CurrentAlbumArtTrack");
				}
				if (AlbumDetailTracks.Count > 0)
				{
					AlbumDetailTracks.ReplaceAll(new List<MusicTrack>());
					AlbumDetailTrackCount = 0;
					OnPropertyChanged("AlbumDetailTrackCount");
				}
			}
			(List<MusicTrack>, string) tuple = await Task.Run(() => _db.GetAlbumDetail(albumName2));
			List<MusicTrack> tracks = tuple.Item1;
			string artist = tuple.Item2;
			int? year = tracks.FirstOrDefault((MusicTrack t) => t.Year > 0)?.Year;
			MusicTrack artTrack = tracks.FirstOrDefault();
			await DispatchAsync(delegate
			{
				CurrentAlbumName = albumName2;
				CurrentAlbumArtist = artistName2 ?? artist;
				CurrentAlbumYear = ((year.HasValue && year.Value > 0) ? year.Value.ToString() : "Unknown Year");
				CurrentAlbumArtTrack = artTrack;
				AlbumDetailTrackCount = tracks.Count;
				for (int i = 0; i < tracks.Count; i++)
					tracks[i].TrackNumber = i + 1;
				AlbumDetailTracks.ReplaceAll(tracks);
				OnPropertyChanged("CurrentAlbumName");
				OnPropertyChanged("CurrentAlbumArtist");
				OnPropertyChanged("CurrentAlbumYear");
				OnPropertyChanged("CurrentAlbumArtTrack");
				OnPropertyChanged("AlbumDetailTrackCount");
			});
		}
		catch
		{
		}
	}

	private async void LoadPlaylistDetail(int playlistId, string playlistName)
	{
		try
		{
			List<MusicTrack> tracks = await Task.Run(() => _db.GetPlaylistTracks(playlistId).ToList());
			await DispatchAsync(delegate
			{
				CurrentPlaylistId = playlistId;
				CurrentPlaylistName = playlistName;
				PlaylistDetailTrackCount = tracks.Count;
				for (int i = 0; i < tracks.Count; i++)
					tracks[i].TrackNumber = i + 1;
				PlaylistDetailTracks.ReplaceAll(tracks);
				OnPropertyChanged("CurrentPlaylistName");
				OnPropertyChanged("PlaylistDetailTrackCount");
			});
		}
		catch
		{
		}
	}

	public void NavigateToPlaylist(int playlistId, string playlistName)
	{
		_viewHistory.Push(CurrentView);
		CurrentView = "playlist_detail";
		IsDrawerOpen = false;
		LoadPlaylistDetail(playlistId, playlistName);
		OnPropertyChanged("CanNavigateBack");
	}

	private async void LoadPlaylists()
	{
		try
		{
			List<PlaylistViewModel> items = await Task.Run(delegate
			{
				List<Playlist> allPlaylists = _db.GetAllPlaylists();
				Dictionary<int, int> allPlaylistTrackCounts = _db.GetAllPlaylistTrackCounts();
				List<PlaylistViewModel> list = new List<PlaylistViewModel>();
				foreach (Playlist item in allPlaylists)
				{
					list.Add(new PlaylistViewModel(item)
					{
						TrackCount = allPlaylistTrackCounts.GetValueOrDefault(item.Id, 0)
					});
				}
				return list;
			});
			await DispatchAsync(delegate
			{
				Playlists.ReplaceAll(items);
			});
		}
		catch
		{
		}
	}

	private async void LoadFavoriteTracks()
	{
		try
		{
			List<MusicTrack> tracks = await Task.Run(() => _db.GetFavoriteTracks());
			await DispatchAsync(delegate
			{
				FavoriteTracks.ReplaceAll(tracks);
			});
		}
		catch
		{
		}
	}

	private async void LoadRecentPlayTracks()
	{
		try
		{
			List<MusicTrack> tracks = await Task.Run(() => _db.GetRecentPlay());
			await DispatchAsync(delegate
			{
				RecentPlayTracks.ReplaceAll(tracks);
			});
		}
		catch
		{
		}
	}

	private async void LoadRecentAddTracks()
	{
		try
		{
			List<MusicTrack> tracks = await Task.Run(() => _db.GetRecentAdd());
			await DispatchAsync(delegate
			{
				RecentAddTracks.ReplaceAll(tracks);
			});
		}
		catch
		{
		}
	}

	private async void LoadMostPlayedTracks()
	{
		try
		{
			List<MusicTrack> tracks = await Task.Run(() => _db.GetMostPlay());
			await DispatchAsync(delegate
			{
				MostPlayedTracks.ReplaceAll(tracks);
			});
		}
		catch
		{
		}
	}

	private void LoadLyrics(MusicTrack? track)
	{
		DispatcherTimer? lyricAdvanceTimer = _lyricAdvanceTimer;
		if (lyricAdvanceTimer != null)
		{
			lyricAdvanceTimer.Stop();
		}
		_lyricAdvanceTimer = null;
		Lyrics.Clear();
		CurrentLyricText = string.Empty;
		CurrentLyricIndex = -1;
		LyricsSourcePath = string.Empty;
		if (track == null || string.IsNullOrEmpty(track.FilePath))
		{
			return;
		}
		LyricParser lyricParser = LyricParser.FindLyricForTrack(track.FilePath);
		if (lyricParser == null)
		{
			return;
		}
		foreach (LyricLine line in lyricParser.Lines)
		{
			Lyrics.Add(line);
		}
		LyricsSourcePath = Path.ChangeExtension(track.FilePath, ".lrc");
	}

	private void UpdateLyricProgress(TimeSpan pos)
	{
		if (Lyrics.Count == 0)
		{
			return;
		}
		int num = CurrentLyricIndex;
		for (int num2 = Lyrics.Count - 1; num2 >= 0; num2--)
		{
			if (pos >= Lyrics[num2].Time)
			{
				num = num2;
				break;
			}
		}
		if (num >= 0 && (CurrentLyricIndex != num || !Lyrics[num].IsCurrent))
		{
			if (CurrentLyricIndex >= 0 && CurrentLyricIndex < Lyrics.Count && CurrentLyricIndex != num)
			{
				Lyrics[CurrentLyricIndex].IsCurrent = false;
			}
			CurrentLyricIndex = num;
			CurrentLyricText = Lyrics[num].Text;
			Lyrics[num].IsCurrent = true;
		}
		ScheduleNextLyricTransition(pos);
	}

	private void ScheduleNextLyricTransition(TimeSpan pos)
	{
		_lyricAdvanceTimer?.Stop();
		if (!IsPlaying || Lyrics.Count == 0 || CurrentLyricIndex < 0 || CurrentLyricIndex >= Lyrics.Count)
		{
			return;
		}
		int num = CurrentLyricIndex + 1;
		if (num >= Lyrics.Count)
		{
			return;
		}
		double num2 = (Lyrics[num].Time - pos).TotalMilliseconds;
		if (num2 <= 0.0)
		{
			num2 = 50.0;
		}
		float valueOrDefault = (_playback?.Engine?.Speed).GetValueOrDefault(1f);
		if (valueOrDefault > 0f)
		{
			num2 /= (double)valueOrDefault;
		}
		if (_lyricAdvanceTimer == null)
		{
			_lyricAdvanceTimer = new DispatcherTimer();
			_lyricAdvanceTimer.Tick += delegate
			{
				_lyricAdvanceTimer?.Stop();
				UpdateLyricProgress(_playback?.Engine?.Position ?? TimeSpan.Zero);
			};
		}
		_lyricAdvanceTimer.Interval = TimeSpan.FromMilliseconds(Math.Max(1.0, num2));
		_lyricAdvanceTimer.Start();
	}
	private void SeekToLyric(LyricLine? line)
	{
		if (line != null)
		{
			_playback.Seek(line.Time);
		}
	}

	public void RefreshLyricHighlight()
	{
		UpdateLyricProgress(_playback?.Engine?.Position ?? TimeSpan.Zero);
	}

	private List<MusicTrack> GetContextTracks()
	{
		return CurrentView switch
		{
			"favorites" => FavoriteTracks.ToList(), 
			"recent" => RecentPlayTracks.ToList(), 
			"recent_add" => RecentAddTracks.ToList(), 
			"most_played" => MostPlayedTracks.ToList(), 
			"folder_detail" => FolderDetailTracks.ToList(), 
			"artist_detail" => ArtistDetailTracks.ToList(), 
			"album_detail" => AlbumDetailTracks.ToList(), 
			"genre_detail" => GenreDetailTracks.ToList(), 
			"albums" => Albums.ToList(), 
			"artists" => Artists.ToList(), 
			"genres" => Genres.ToList(), 
			"search" => SearchResults.ToList(), 
			_ => Tracks.ToList(), 
		};
	}

	private void ApplyLibrarySort()
	{
		if (Tracks.Count > 1)
		{
			SortCollection(Tracks);
		}
		if (FavoriteTracks.Count > 1)
		{
			SortCollection(FavoriteTracks);
		}
		if (RecentPlayTracks.Count > 1)
		{
			SortCollection(RecentPlayTracks);
		}
		if (RecentAddTracks.Count > 1)
		{
			SortCollection(RecentAddTracks);
		}
		if (MostPlayedTracks.Count > 1)
		{
			SortCollection(MostPlayedTracks);
		}
		if (Albums.Count > 1)
		{
			SortCollection(Albums);
		}
		if (Artists.Count > 1)
		{
			SortCollection(Artists, isArtistCollection: true);
		}
		if (Genres.Count > 1)
		{
			SortCollection(Genres);
		}
	}

	private IEnumerable<MusicTrack> SortItems(IEnumerable<MusicTrack> tracks, bool isArtistCollection = false)
	{
		Func<MusicTrack, string> keySelector = LibrarySortOrder switch
		{
			"artist" => (MusicTrack track) => track.Artist ?? string.Empty, 
			"album" => (MusicTrack track) => track.Album ?? string.Empty, 
			"folder" => (MusicTrack track) => Path.GetDirectoryName(track.FilePath) ?? string.Empty, 
			_ when isArtistCollection => (MusicTrack track) => track.Artist ?? string.Empty, 
			_ => (MusicTrack track) => track.Title ?? string.Empty, 
		};
		IEnumerable<MusicTrack> enumerable = LibrarySortOrder switch
		{
			"date_added" => tracks.OrderByDescending((MusicTrack track) => track.DateAdded), 
			"date_modified" => tracks.OrderBy((MusicTrack track) => track.DateModified), 
			"duration" => tracks.OrderBy((MusicTrack track) => track.Duration), 
			"size" => tracks.OrderBy((MusicTrack track) => track.FileSize), 
			"year" => tracks.OrderBy((MusicTrack track) => track.Year), 
			"track_number" => tracks.OrderBy((MusicTrack track) => track.TrackNumber), 
			"title_az" when isArtistCollection => tracks.OrderBy<MusicTrack, string>((MusicTrack track) => track.Artist ?? string.Empty, StringComparer.CurrentCultureIgnoreCase), 
			"title_za" when isArtistCollection => tracks.OrderByDescending<MusicTrack, string>((MusicTrack track) => track.Artist ?? string.Empty, StringComparer.CurrentCultureIgnoreCase), 
			"title_az" => tracks.OrderBy<MusicTrack, string>((MusicTrack track) => track.Title ?? string.Empty, StringComparer.CurrentCultureIgnoreCase), 
			"title_za" => tracks.OrderByDescending<MusicTrack, string>((MusicTrack track) => track.Title ?? string.Empty, StringComparer.CurrentCultureIgnoreCase), 
			"random" => tracks.OrderBy((MusicTrack _) => Guid.NewGuid()), 
			_ when isArtistCollection => tracks.OrderBy<MusicTrack, string>(keySelector, StringComparer.CurrentCultureIgnoreCase), 
			_ => tracks.OrderBy<MusicTrack, string>(keySelector, StringComparer.CurrentCultureIgnoreCase), 
		};
		if (IsLibrarySortDescending)
		{
			enumerable = enumerable.Reverse();
		}
		return enumerable;
	}

	private void SortCollection(ObservableCollection<MusicTrack> tracks, bool isArtistCollection = false)
	{
		if (tracks.Count < 2)
		{
			return;
		}
		IEnumerable<MusicTrack> enumerable = SortItems(tracks, isArtistCollection);
		if (tracks is BulkObservableCollection<MusicTrack> bulkObservableCollection)
		{
			bulkObservableCollection.ReplaceAll(enumerable);
			return;
		}
		List<MusicTrack> list = enumerable.ToList();
		tracks.Clear();
		foreach (MusicTrack item in list)
		{
			tracks.Add(item);
		}
	}

	private void SortFolders()
	{
		if (Folders.Count < 2) return;
		IEnumerable<FolderItem> sorted = LibrarySortOrder switch
		{
			"title_az" => Folders.OrderBy(f => f.Name, StringComparer.CurrentCultureIgnoreCase),
			"title_za" => Folders.OrderByDescending(f => f.Name, StringComparer.CurrentCultureIgnoreCase),
			"size" => Folders.OrderByDescending(f => f.TrackCount),
			"random" => Folders.OrderBy(_ => Guid.NewGuid()),
			_ => Folders.OrderBy(f => f.Name, StringComparer.CurrentCultureIgnoreCase),
		};
		if (IsLibrarySortDescending && LibrarySortOrder != "random")
			sorted = sorted.Reverse();
		Folders.ReplaceAll(sorted);
	}

	private static void BulkReplace<T>(ObservableCollection<T> target, IList<T> items)
	{
		target.Clear();
		foreach (T item in items)
		{
			target.Add(item);
		}
	}
	private void OpenHiddenFolders()
	{
		LoadHiddenFolders();
		Navigate("hidden_folders");
	}

	private void LoadHiddenFolders()
	{
		HiddenFoldersList.Clear();
		foreach (HiddenFolder hiddenFolder in _db.GetHiddenFolders())
		{
			HiddenFoldersList.Add(hiddenFolder);
		}
	}
	private void AddHiddenFolderDialog()
	{
		OpenFolderDialog openFolderDialog = new OpenFolderDialog
		{
			Title = "Select folder to hide"
		};
		if (openFolderDialog.ShowDialog().GetValueOrDefault())
		{
			_db.AddHiddenFolder(openFolderDialog.FolderName);
			LoadHiddenFolders();
		}
	}
	private void RemoveHiddenFolder(HiddenFolder? h)
	{
		if (h != null)
		{
			_db.RemoveHiddenFolder(h.FolderPath);
			LoadHiddenFolders();
		}
	}
	private void OpenHiddenFiles()
	{
		LoadHiddenTracks();
		Navigate("hidden_files");
	}
	private void LoadHiddenTracks()
	{
		HiddenTracks.Clear();
		foreach (var t in _db.GetHiddenTracks())
			HiddenTracks.Add(t);
	}
	private void RefreshLibraryTabs()
	{
		try
		{
			var artists = _db.GetAllArtists();
			Artists.Clear();
			foreach (var a in artists) Artists.Add(new MusicTrack { Artist = a });
			OnPropertyChanged(nameof(DisplayedArtists));
		}
		catch { }
		try
		{
			var albums = _db.GetAllAlbums();
			Albums.Clear();
			foreach (var al in albums)
			{
				var rep = _db.GetAlbumTracks(al).FirstOrDefault();
				if (rep != null) Albums.Add(rep);
				else Albums.Add(new MusicTrack { Album = al, Artist = _db.GetAlbumArtist(al) });
			}
			OnPropertyChanged(nameof(DisplayedAlbums));
		}
		catch { }
		try
		{
			var genres = _db.GetAllGenres();
			Genres.Clear();
			foreach (var g in genres) Genres.Add(new MusicTrack { Genre = g });
			OnPropertyChanged(nameof(DisplayedGenres));
		}
		catch { }
		try
		{
			OnPropertyChanged(nameof(DisplayedTracks));
		}
		catch { }
	}
	private void HideTrack(MusicTrack? track)
	{
		if (track == null) return;
		bool isCurrent = CurrentTrack != null && (CurrentTrack.Id == track.Id || string.Equals(CurrentTrack.FilePath, track.FilePath, StringComparison.OrdinalIgnoreCase));
		track.IsHidden = true;
		_db.UpdateTrack(track);
		// remove from every visible collection that might contain the track
		void RemoveById(BulkObservableCollection<MusicTrack> col, int id)
		{
			var item = col.FirstOrDefault(t => t.Id == id);
			if (item != null) col.Remove(item);
		}
		RemoveById(Tracks, track.Id);
		RemoveById(ArtistDetailTracks, track.Id);
		RemoveById(AlbumDetailTracks, track.Id);
		RemoveById(GenreDetailTracks, track.Id);
		RemoveById(FolderDetailTracks, track.Id);
		RemoveById(FavoriteTracks, track.Id);
		RemoveById(RecentAddTracks, track.Id);
		RemoveById(RecentPlayTracks, track.Id);
		RemoveById(MostPlayedTracks, track.Id);
		RemoveById(SearchResults, track.Id);
		RemoveById(Queue, track.Id);
		RemoveById(PlaylistDetailTracks, track.Id);
		RemoveById(DuplicateTracks, track.Id);
		RemoveById(DeletedTracks, track.Id);
		RemoveById(FilteredArtists, track.Id);
		RemoveById(FilteredAlbums, track.Id);
		RemoveById(FilteredGenres, track.Id);
		// if not found in main Tracks, fallback to full reload to ensure DB-filtered state
		if (!Tracks.Any(t => t.Id == track.Id))
		{
			// ensure count and stats are refreshed
			OnPropertyChanged(nameof(Tracks));
		}
		else
		{
			// full reload if still present (reference mismatch)
			Tracks.Clear();
			foreach (var t in _db.GetManagedFolderTracksSorted(_librarySortOrder, _isLibrarySortDescending))
				Tracks.Add(t);
		}
		// remove from playback queue if present
		try
		{
			var qq = _playback.Engine.Queue.ToList();
			int qi = qq.FindIndex(t => t.Id == track.Id || string.Equals(t.FilePath, track.FilePath, StringComparison.OrdinalIgnoreCase));
			if (qi >= 0)
			{
				_playback.Engine.RemoveFromQueue(qi);
				RefreshQueue();
				SaveQueueState();
			}
		}
		catch { }
		// if the hidden track was currently shown/playing, stop it
		if (isCurrent)
		{
			try { _playback.Engine.Stop(); } catch { }
			CurrentTrack = null;
			IsPlaying = false;
			Position = TimeSpan.Zero;
			Duration = TimeSpan.Zero;
		}
		// if artist/album/genre detail page was showing this track, refresh its stats
		if (CurrentView == "artist_detail" && !string.IsNullOrEmpty(CurrentArtistName))
		{
			LoadArtistDetail(CurrentArtistName);
		}
		else if (CurrentView == "album_detail" && !string.IsNullOrEmpty(CurrentAlbumName))
		{
			try
			{
				var (t, a) = _db.GetAlbumDetail(CurrentAlbumName);
				AlbumDetailTracks.ReplaceAll(t);
			}
			catch { }
		}
		RefreshLibraryTabs();
		UpdateHomeCardCounts();
		ShowHiddenSuccessBar();
		LoadHiddenTracks();
	}
	private void UnhideTrack(MusicTrack? track)
	{
		if (track == null) return;
		track.IsHidden = false;
		_db.UpdateTrack(track);
		LoadHiddenTracks();
		// add back to main library and keep it sorted
		if (!Tracks.Any(t => t.Id == track.Id) && System.IO.File.Exists(track.FilePath))
		{
			Tracks.Add(track);
			SortCollection(Tracks);
		}
		RefreshLibraryTabs();
		UpdateHomeCardCounts();
		OnPropertyChanged(nameof(HiddenTracks));
		ShowStatusBar($"Restored '{track.Title}'", 2000);
	}
	private void AddHiddenFilesDialog()
	{
		var dlg = new Microsoft.Win32.OpenFileDialog
		{
			Title = "Select audio files to hide",
			Filter = "Audio files|*.mp3;*.flac;*.wav;*.m4a;*.aac;*.ogg;*.wma|All files|*.*",
			Multiselect = true
		};
		if (dlg.ShowDialog().GetValueOrDefault())
		{
			foreach (var path in dlg.FileNames)
			{
				var t = _db.GetTrackByPath(path);
				if (t != null && !t.IsHidden)
				{
					t.IsHidden = true;
					_db.UpdateTrack(t);
					Tracks.Remove(t);
				}
			}
			LoadHiddenTracks();
			ShowStatusBar($"{dlg.FileNames.Length} file(s) hidden", 2000);
		}
	}
	private void OpenScanSettings()
	{
		ScanFolderList.Clear();
		string[] array = _settings.ScanFolders.Split(';', StringSplitOptions.RemoveEmptyEntries);
		if (array.Length == 0)
		{
			array = new string[1] { Environment.GetFolderPath(Environment.SpecialFolder.MyMusic) };
		}
		string[] array2 = array;
		foreach (string item in array2)
		{
			ScanFolderList.Add(item);
		}
		Navigate("scan_settings");
	}
	private void AddScanFolderDialog()
	{
		OpenFolderDialog openFolderDialog = new OpenFolderDialog
		{
			Title = "Select scan folder"
		};
		if (openFolderDialog.ShowDialog().GetValueOrDefault())
		{
			ScanFolderList.Add(openFolderDialog.FolderName);
			_settings.ScanFolders = string.Join(";", ScanFolderList);
		}
	}
	private void RemoveScanFolder(string? f)
	{
		if (f != null)
		{
			ScanFolderList.Remove(f);
			_settings.ScanFolders = string.Join(";", ScanFolderList);
		}
	}
	private async Task OpenDuplicateFinderAsync()
	{
		IEnumerable<IGrouping<string, MusicTrack>> source = from t in await Task.Run(() => _db.GetAllTracks())
			group t by t.Title?.Trim().ToLowerInvariant() + "|" + t.Artist?.Trim().ToLowerInvariant() into g
			where g.Count() > 1
			select g;
		List<MusicTrack> dupes = source.SelectMany((IGrouping<string, MusicTrack> g) => g).ToList();
		await DispatchAsync(delegate
		{
			DuplicateTracks.ReplaceAll(dupes);
		});
		Navigate("duplicate_finder");
	}
	private void DeleteDuplicate(MusicTrack? t)
	{
		if (t != null)
		{
			_db.DeleteTrack(t.Id);
			DuplicateTracks.Remove(t);
			LoadLibraryFull();
		}
	}
	private async Task OpenDeletedMusicAsync()
	{
		List<MusicTrack> tracks = await Task.Run(() => (from tr in _db.GetAllTracks()
			where !System.IO.File.Exists(tr.FilePath)
			select tr).ToList());
		await DispatchAsync(delegate
		{
			DeletedTracks.ReplaceAll(tracks);
		});
		Navigate("deleted_music");
	}
	private void RemoveDeletedTrack(MusicTrack? t)
	{
		if (t != null)
		{
			_db.DeleteTrack(t.Id);
			DeletedTracks.Remove(t);
		}
	}
	private void ClearAllDeleted()
	{
		foreach (MusicTrack item in DeletedTracks.ToList())
		{
			_db.DeleteTrack(item.Id);
		}
		DeletedTracks.Clear();
		LoadLibraryFull();
	}
	private async Task OpenMusicSelectAsync()
	{
		List<MusicTrack> tracks = await Task.Run(() => _db.GetAllTracks().Take(500).ToList());
		await DispatchAsync(delegate
		{
			MusicSelectList.ReplaceAll(tracks);
		});
		Navigate("music_select");
	}
	private async Task OpenMusicSetEditAsync()
	{
		if (CurrentPlaylist == null)
		{
			Navigate("music_set_edit");
			return;
		}
		List<MusicTrack> tracks = await Task.Run(() => _db.GetPlaylistTracks(CurrentPlaylist.Id).ToList());
		await DispatchAsync(delegate
		{
			PlaylistEditTracks.ReplaceAll(tracks);
		});
		Navigate("music_set_edit");
	}
	private void OpenPlaylistEdit()
	{
		LoadPlaylists();
		Navigate("playlist_edit");
	}
	private void MovePlaylistUp(PlaylistViewModel? p)
	{
		if (p == null)
		{
			return;
		}
		int num = Playlists.IndexOf(p);
		if (num <= 0)
		{
			return;
		}
		Playlists.Move(num, num - 1);
		for (int i = 0; i < Playlists.Count; i++)
		{
			Playlists[i].Model.SortOrder = i;
		}
		foreach (PlaylistViewModel playlist in Playlists)
		{
			_db.UpdatePlaylist(playlist.Model);
		}
	}
	private void MovePlaylistDown(PlaylistViewModel? p)
	{
		if (p == null)
		{
			return;
		}
		int num = Playlists.IndexOf(p);
		if (num < 0 || num >= Playlists.Count - 1)
		{
			return;
		}
		Playlists.Move(num, num + 1);
		for (int i = 0; i < Playlists.Count; i++)
		{
			Playlists[i].Model.SortOrder = i;
		}
		foreach (PlaylistViewModel playlist in Playlists)
		{
			_db.UpdatePlaylist(playlist.Model);
		}
	}
	private void OpenPlaylistSelect()
	{
		if (ShowFullPlayer)
		{
			ShowFullPlayer = false;
		}
		PlaylistSelectList.ReplaceAll(Playlists);
		Navigate("playlist_select");
	}
	private void SelectPlaylistForAdd(PlaylistViewModel? p)
	{
		if (p == null) return;

		if (PendingTracksToAdd != null && PendingTracksToAdd.Count > 0)
		{
			int count = _db.GetPlaylistTracks(p.Id).Count;
			foreach (var track in PendingTracksToAdd)
			{
				_db.AddTrackToPlaylist(p.Id, track.Id, count++);
			}
			int added = PendingTracksToAdd.Count;
			LoadPlaylists();
			ShowStatusBar($"Added {added} tracks to '{p.Name}'");
			PendingTracksToAdd = null;
			GoBack();
		}
		else
		{
			var track = BottomSheetTrack ?? CurrentTrack;
			if (track != null)
			{
				int count = _db.GetPlaylistTracks(p.Id).Count;
				_db.AddTrackToPlaylist(p.Id, track.Id, count);
				LoadPlaylists();
				ShowStatusBar($"Added '{track.Title}' to '{p.Name}'");
				GoBack();
			}
		}
	}
	private void DeletePlaylist(PlaylistViewModel? p)
	{
		if (p != null && MessageBox.Show("Delete playlist '" + p.Name + "'?", "Confirm", MessageBoxButton.YesNo, MessageBoxImage.Exclamation) == MessageBoxResult.Yes)
		{
			_db.DeletePlaylist(p.Id);
			LoadPlaylists();
		}
	}
	private void RenamePlaylist((PlaylistViewModel vm, string newName) item)
	{
		if (item.vm != null && !string.IsNullOrWhiteSpace(item.newName))
		{
			item.vm.Name = item.newName.Trim();
			item.vm.Model.DateModified = DateTime.Now.ToBinary();
			_db.UpdatePlaylist(item.vm.Model);
			LoadPlaylists();
		}
	}
	private void OpenEditTags(MusicTrack? t)
	{
		MusicTrack musicTrack = t ?? CurrentTrack;
		if (musicTrack != null)
		{
			EditingTrack = musicTrack;
			EditTitle = musicTrack.Title;
			EditArtist = musicTrack.Artist;
			EditAlbum = musicTrack.Album;
			Navigate("edit_tags");
		}
	}
	private void SaveEditTags()
	{
		if (EditingTrack != null)
		{
			EditingTrack.Title = EditTitle;
			EditingTrack.Artist = EditArtist;
			EditingTrack.Album = EditAlbum;
			_db.UpdateTrack(EditingTrack);
			try
			{
				TagLib.File file = TagLib.File.Create(EditingTrack.FilePath);
				file.Tag.Title = EditTitle;
				file.Tag.Performers = new string[1] { EditArtist };
				file.Tag.Album = EditAlbum;
				file.Save();
			}
			catch
			{
			}
			LoadLibrary();
			GoBack();
		}
	}
	private void OpenBatchEdit()
	{
		Navigate("batch_edit");
	}
	private void BatchSetArtist(string? a)
	{
		if (string.IsNullOrWhiteSpace(a))
		{
			return;
		}
		foreach (MusicTrack item in ((SearchResults.Count > 0) ? SearchResults : Tracks).Take(100).ToList())
		{
			item.Artist = a;
			_db.UpdateTrack(item);
			try
			{
				TagLib.File file = TagLib.File.Create(item.FilePath);
				file.Tag.Performers = new string[1] { a };
				file.Save();
			}
			catch
			{
			}
		}
		LoadLibrary();
	}
	private void OpenAlbumGetter()
	{
		AlbumSearchQuery = CurrentTrack?.Album ?? "";
		AlbumArtStatus = "Enter album name and search";
		Navigate("album_getter");
	}
	private void SearchAlbumArt()
	{
		AlbumArtStatus = (string.IsNullOrWhiteSpace(AlbumSearchQuery) ? "Enter keyword" : $"Searching online for '{AlbumSearchQuery}' - open browser: https://www.google.com/search?q={Uri.EscapeDataString(AlbumSearchQuery)}+album+art");
	}
	private void OpenAlbumSave()
	{
		AlbumArtStatus = "Album art will be saved to track file and cache";
		Navigate("album_save");
	}
	private void SaveAlbumArt()
	{
		if (CurrentTrack == null)
		{
			return;
		}
		try
		{
			OpenFileDialog openFileDialog = new OpenFileDialog
			{
				Filter = "Images|*.jpg;*.png;*.webp"
			};
			if (openFileDialog.ShowDialog().GetValueOrDefault())
			{
				TagLib.File file = TagLib.File.Create(CurrentTrack.FilePath);
				Tag tag = file.Tag;
				IPicture[] pictures = new Picture[1]
				{
					new Picture(openFileDialog.FileName)
				};
				tag.Pictures = pictures;
				file.Save();
				AlbumArtStatus = "Saved: " + openFileDialog.FileName;
			}
		}
		catch (Exception ex)
		{
			AlbumArtStatus = "Failed: " + ex.Message;
		}
	}
	private void OpenAudioEditor()
	{
		Navigate("audio_editor");
	}
	private void OpenLrcBrowser()
	{
		string[] array = _settings.ScanFolders.Split(';', StringSplitOptions.RemoveEmptyEntries);
		if (array.Length == 0)
		{
			array = new string[1] { Environment.GetFolderPath(Environment.SpecialFolder.MyMusic) };
		}
		_lrcFolderData.Clear();
		_lrcAllFiles.Clear();
		List<string> list = new List<string> { "ALL" };
		string[] array2 = array;
		foreach (string text in array2)
		{
			if (!Directory.Exists(text))
			{
				continue;
			}
			List<string> list2 = Directory.GetFiles(text, "*.lrc", SearchOption.AllDirectories).Take(200).ToList();
			if (list2.Count > 0)
			{
				string text2 = Path.GetFileName(text);
				if (string.IsNullOrEmpty(text2))
				{
					text2 = text;
				}
				_lrcFolderData.Add((text2, list2.ToArray()));
				list.Add(text2);
				_lrcAllFiles.AddRange(list2);
			}
		}
		LrcFolderNames.ReplaceAll(list);
		SelectedLrcFolder = "ALL";
		LrcLocalFiles.ReplaceAll(_lrcAllFiles);
		Navigate("lrc_browser");
	}
	private void OpenLrcEdit()
	{
		LrcEditShowValidation = false;
		_lrcEditAutoCreated = false;
		_lrcEditPath = string.Empty;
		if (CurrentTrack == null || string.IsNullOrEmpty(CurrentTrack.FilePath))
		{
			return;
		}
		_lrcEditPath = Path.ChangeExtension(CurrentTrack.FilePath, ".lrc");
		if (System.IO.File.Exists(_lrcEditPath))
		{
			LrcEditText = System.IO.File.ReadAllText(_lrcEditPath);
			_lrcEditAutoCreated = false;
		}
		else
		{
			LrcEditText = string.Empty;
			_lrcEditAutoCreated = true;
			try
			{
				System.IO.File.WriteAllText(_lrcEditPath, string.Empty);
			}
			catch
			{
			}
		}
		Navigate("lrc_editor");
	}
	private void SaveLrcEdit()
	{
		if ((from l in LrcEditText.Split('\n', StringSplitOptions.RemoveEmptyEntries)
			where l.Trim().Length > 0
			select l).ToList().Count == 0)
		{
			LrcEditShowValidation = true;
			return;
		}
		LrcEditShowValidation = false;
		try
		{
			if (!string.IsNullOrEmpty(_lrcEditPath))
			{
				System.IO.File.WriteAllText(_lrcEditPath, LrcEditText);
			}
		}
		catch
		{
		}
		LoadLyrics(CurrentTrack);
		GoBackCommand.Execute(null);
	}

	internal void CleanupLrcEditIfEmpty()
	{
		if (_lrcEditAutoCreated && !string.IsNullOrEmpty(_lrcEditPath) && (from l in LrcEditText.Split('\n', StringSplitOptions.RemoveEmptyEntries)
			where l.Trim().Length > 0
			select l).ToList().Count == 0)
		{
			try
			{
				if (System.IO.File.Exists(_lrcEditPath))
				{
					System.IO.File.Delete(_lrcEditPath);
				}
			}
			catch
			{
			}
		}
		_lrcEditAutoCreated = false;
		LrcEditText = string.Empty;
		LrcEditShowValidation = false;
	}
	private void OpenSleepTimer()
	{
		Navigate("sleep_timer");
	}
	private void SetSleepTimer(string? mins)
	{
		if (mins == null) return;
		// normalize: "close" / "0" => cancel
		if (mins == "0" || mins.Equals("close", StringComparison.OrdinalIgnoreCase))
		{
			CancelSleepTimer();
			return;
		}
		int result;
		if (mins.Equals("manual", StringComparison.OrdinalIgnoreCase))
		{
			_isManualSleepTimer = true;
			result = SleepTimerManualMinutes;
		}
		else if (!int.TryParse(mins, out result) || result <= 0)
		{
			return;
		}
		else
		{
			_isManualSleepTimer = false;
		}
		if (result < 1) result = 1;
		if (result > 999) result = 999;
		StartSleepTimer(result);
	}
	private void StartSleepTimer(int minutes)
	{
		_sleepTimerCts?.Cancel();
		_sleepTimerCts = new CancellationTokenSource();
		var token = _sleepTimerCts.Token;
		SleepTimerSelectedMinutes = minutes;
		OnPropertyChanged(nameof(IsManualSleepTimerActive));
		OnPropertyChanged(nameof(SleepTimerPresetMinutes));
		_sleepTimerEndTime = DateTime.Now.AddMinutes(minutes);
		StartSleepTimerTicker();
		_ = RunSleepTimerAsync(minutes, token);
		SleepTimerStatus = $"Sleep timer: {minutes:00}:00 remaining";
		ShowStatusBar($"Sleep timer set for {minutes} min", 2500);
	}
	private async Task RunSleepTimerAsync(int minutes, CancellationToken token)
	{
		try
		{
			await Task.Delay(TimeSpan.FromMinutes(minutes), token);
			await HandleSleepTimerElapsedAsync(token);
		}
		catch (OperationCanceledException)
		{
			// Timer was cancelled or replaced
		}
		catch (Exception ex)
		{
			System.Diagnostics.Debug.WriteLine($"SleepTimer error: {ex.Message}");
		}
	}
	private void CancelSleepTimer()
	{
		_sleepTimerCts?.Cancel();
		_sleepTimerCts = null;
		_sleepTimerTicker?.Stop();
		_sleepTimerEndTime = null;
		_isManualSleepTimer = false;
		SleepTimerSelectedMinutes = 0;
		SleepTimerStatus = string.Empty;
		OnPropertyChanged(nameof(IsManualSleepTimerActive));
		OnPropertyChanged(nameof(SleepTimerPresetMinutes));
		ShowStatusBar("Sleep timer cancelled", 2000);
	}
	private void StartSleepTimerTicker()
	{
		_sleepTimerTicker?.Stop();
		_sleepTimerTicker = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
		_sleepTimerTicker.Tick += (s, e) =>
		{
			if (_sleepTimerEndTime == null)
			{
				SleepTimerStatus = string.Empty;
				_sleepTimerTicker?.Stop();
				return;
			}
			var rem = _sleepTimerEndTime.Value - DateTime.Now;
			if (rem <= TimeSpan.Zero)
			{
				SleepTimerStatus = "Finishing…";
				return;
			}
			int totalM = (int)rem.TotalMinutes;
			int sec = rem.Seconds;
			SleepTimerStatus = $"Sleep timer: {totalM:D2}:{sec:D2} remaining";
		};
		_sleepTimerTicker.Start();
	}
	private async Task HandleSleepTimerElapsedAsync(CancellationToken token)
	{
		if (token.IsCancellationRequested) return;
		// If "Play to the end regardless of timer" is on and a track is playing, wait for it to finish
		if (SleepTimerPlayToEnd && _playback.Engine.IsPlaying)
		{
			try
			{
				var rem = _playback.Engine.Duration - _playback.Engine.Position;
				if (rem > TimeSpan.Zero && rem.TotalMinutes < 120)
				{
					await Task.Delay(rem, token);
				}
			}
			catch (OperationCanceledException) { return; }
			catch { }
		}
		if (token.IsCancellationRequested) return;
		Dispatch(() =>
		{
			try { _playback.Engine.Pause(); } catch { }
			_sleepTimerTicker?.Stop();
			_sleepTimerEndTime = null;
			_isManualSleepTimer = false;
			SleepTimerSelectedMinutes = 0;
			SleepTimerStatus = "Sleep timer finished";
			OnPropertyChanged(nameof(IsManualSleepTimerActive));
			OnPropertyChanged(nameof(SleepTimerPresetMinutes));
			try { App.AllowSleep(); } catch { }
			// After the sleep timer end -> Exit Player (as shown in phone screenshot)
			ShowStatusBar("Sleep timer finished — exiting", 2000);
		});

		try
		{
			await Task.Delay(900, token);
		}
		catch (OperationCanceledException)
		{
			return;
		}

		if (token.IsCancellationRequested) return;
		Dispatch(() =>
		{
			try { System.Windows.Application.Current.Shutdown(); } catch { }
		});
	}
	private void OpenTrackBottomSheet(MusicTrack? track)
	{
		if (track != null)
		{
			BottomSheetTrack = track;
			ShowTrackBottomSheet = true;
		}
	}
	private void CloseTrackBottomSheet()
	{
		ShowTrackBottomSheet = false;
	}
	private void OpenTrackInfo(MusicTrack? track)
	{
		MusicTrack musicTrack = track ?? BottomSheetTrack ?? CurrentTrack;
		if (musicTrack != null)
		{
			BottomSheetTrack = musicTrack;
			ShowTrackInfo = true;
		}
	}
	private void CloseTrackInfo()
	{
		ShowTrackInfo = false;
	}
	private void ToggleSortMenu()
	{
		ShowOverflowMenu = false;
		ShowSortMenu = !ShowSortMenu;
	}
	private void ToggleOverflowMenu()
	{
		ShowOverflowMenu = !ShowOverflowMenu;
	}
	private void SelectSortOption(string? sortKey)
	{
		if (!string.IsNullOrEmpty(sortKey))
		{
			if (sortKey == "reverse")
			{
				IsLibrarySortDescending = !IsLibrarySortDescending;
				_db.SetSetting("pref_library_sort_descending", IsLibrarySortDescending.ToString());
			}
			else
			{
				LibrarySortOrder = sortKey;
				_settings.LibraryOrder = sortKey;
			}
			ApplyLibrarySort();
			ShowSortMenu = false;
			ShowOverflowMenu = false;
		}
	}
	private async Task RefreshMetadataAsync()
	{
		ShowOverflowMenu = false;
		if (_scanner.IsScanning)
		{
			ShowStatusBar("Scan already in progress...", 2000);
			return;
		}

		ShowStatusBarLoading("Refreshing metadata, please wait…");

		try
		{
			var (added, updated, removed) = await _scanner.RefreshAndSyncLibraryAsync();

			await DispatchAsync(delegate
			{
				LoadLibraryFull();
				UpdateHomeCardCounts();
			});

			if (added > 0 && removed > 0)
			{
				ShowStatusBar($"Metadata refreshed: {added} new added, {removed} removed", 3500);
			}
			else if (added > 0)
			{
				ShowStatusBar($"Metadata refreshed: {added} new track(s) added", 3500);
			}
			else if (updated > 0)
			{
				ShowStatusBar($"Metadata refreshed: {updated} track(s) updated", 3500);
			}
			else if (removed > 0)
			{
				ShowStatusBar($"Metadata refreshed: {removed} missing track(s) removed", 3500);
			}
			else
			{
				ShowStatusBar("Metadata refreshed: Library is up to date", 3000);
			}
		}
		catch (Exception ex)
		{
			_logger.LogError(ex, "Failed to refresh metadata");
			ShowStatusBar("Failed to refresh metadata", 3000);
		}
	}
	private void ToggleSongsSection()
	{
		IsSongsSectionCollapsed = !IsSongsSectionCollapsed;
	}
	private void ToggleTrackFavorite(MusicTrack? track)
	{
		MusicTrack musicTrack = track ?? BottomSheetTrack ?? CurrentTrack;
		if (musicTrack != null)
		{
			_db.ToggleFavorite(musicTrack.Id);
			musicTrack.IsFavorite = !musicTrack.IsFavorite;
			if (CurrentTrack != null && CurrentTrack.Id == musicTrack.Id)
			{
				CurrentTrack.IsFavorite = musicTrack.IsFavorite;
				IsFavorite = musicTrack.IsFavorite;
			}
			UpdateHomeCardCounts();
			if (CurrentView == "favorites")
			{
				LoadFavoriteTracks();
			}
		}
	}
	private void PlayNext(MusicTrack? track)
	{
		MusicTrack musicTrack = track ?? BottomSheetTrack ?? MoreOptionsContextTrack;
		if (musicTrack != null)
		{
			_playback.Engine.InsertNext(musicTrack);
			RefreshQueue();
			SaveQueueState();
			UpdateQueueSummary();
			ShowTrackBottomSheet = false;
			ShowStatusBar($"Next up: {musicTrack.Title}", 2500);
		}
	}
	private void Enqueue(MusicTrack? track)
	{
		MusicTrack musicTrack = track ?? BottomSheetTrack ?? MoreOptionsContextTrack;
		if (musicTrack != null)
		{
			_playback.Engine.AddToQueue(musicTrack);
			RefreshQueue();
			SaveQueueState();
			UpdateQueueSummary();
			ShowTrackBottomSheet = false;
			ShowStatusBar($"Added to queue: {musicTrack.Title}", 2500);
		}
	}
	private void SetRingtone(MusicTrack? track)
	{
		MusicTrack musicTrack = track ?? BottomSheetTrack;
		if (musicTrack == null)
		{
			return;
		}
		ShowTrackBottomSheet = false;
		MessageBox.Show("Ringtone: " + musicTrack.Title + " (Windows: opening file in Explorer)", "Ringtone", MessageBoxButton.OK, MessageBoxImage.Asterisk);
		try
		{
			Process.Start("explorer.exe", "/select, \"" + musicTrack.FilePath + "\"");
		}
		catch
		{
		}
	}
	private void TrimAudio(MusicTrack? track)
	{
		MusicTrack musicTrack = track ?? BottomSheetTrack;
		if (musicTrack != null)
		{
			ShowTrackBottomSheet = false;
			EditingTrack = musicTrack;
			OpenAudioEditor();
		}
	}
	private void ShareTrack(MusicTrack? track)
	{
		MusicTrack musicTrack = track ?? BottomSheetTrack;
		if (musicTrack == null)
		{
			return;
		}
		ShowTrackBottomSheet = false;
		try
		{
			Process.Start("explorer.exe", "/select, \"" + musicTrack.FilePath + "\"");
		}
		catch
		{
		}
	}
	private void DeleteTrackFromLibrary(MusicTrack? track)
	{
		MusicTrack musicTrack = track ?? BottomSheetTrack;
		if (musicTrack != null)
		{
			ShowTrackBottomSheet = false;
			if (MessageBox.Show("Delete '" + musicTrack.Title + "' from library?", "Confirm", MessageBoxButton.YesNo, MessageBoxImage.Exclamation) == MessageBoxResult.Yes)
			{
				_db.DeleteTrack(musicTrack.Id);
				LoadLibrary();
			}
		}
	}
	private async Task OpenLyricListAsync()
	{
		List<string> files = await Task.Run(() => (from tr in _db.GetAllTracks()
			select Path.ChangeExtension(tr.FilePath, ".lrc")).Where(System.IO.File.Exists).Distinct().Take(200)
			.ToList());
		await DispatchAsync(delegate
		{
			LrcLocalFiles.ReplaceAll(files);
		});
		Navigate("lyric_list");
	}
	private void BindLyricFile(string? lrcPath)
	{
		if (CurrentTrack != null && lrcPath != null && System.IO.File.Exists(lrcPath))
		{
			_db.SaveLyricBinding(new LyricBinding
			{
				TrackPath = CurrentTrack.FilePath,
				LyricPath = lrcPath
			});
			LoadLyrics(CurrentTrack);
			MessageBox.Show("Lyric bound: " + lrcPath, "Lyrics");
		}
	}
	private void OpenStatusBarLyrics()
	{
		Navigate("status_bar_lyrics");
	}
	private void OpenThemeGallery()
	{
		// Warm the current wallpaper's editor-size decode in the background: the
		// editor's first paint needs it, and this avoids a cold synchronous decode
		// inside the entry window. Fire-and-forget, cached, bounded (one file).
		string wp = BackgroundImagePath;
		Task.Run(() =>
		{
			try
			{
				if (!string.IsNullOrWhiteSpace(wp))
					Converters.WallpaperThumbnailConverter.GetThumbnail(wp, 700);
			}
			catch { }
		});
		Navigate("theme");
	}
	private void ApplyTheme(string? t)
	{
		if (t != null)
		{
			SelectedTheme = t;
			_db.SetSetting("app_theme", t);
			MessageBox.Show("Theme '" + t + "' applied (persisted for next launch)", "Theme", MessageBoxButton.OK, MessageBoxImage.Asterisk);
		}
	}
	private void OpenThemeEditor()
	{
		ThemeEditWallpaperPath = string.IsNullOrEmpty(BackgroundImagePath)
			? "pack://application:,,,/Assets/Backgrounds/bg_001.png"
			: BackgroundImagePath;
		ThemeEditBlur = WallpaperBlur;
		ThemeEditAlpha = WallpaperAlpha;
		Navigate("theme_edit");
	}

	private void ChooseThemePicture()
	{
		try
		{
			var dlg = new Microsoft.Win32.OpenFileDialog
			{
				Title = "Select Wallpaper Picture",
				Filter = "Image Files (*.jpg;*.jpeg;*.png;*.webp;*.bmp)|*.jpg;*.jpeg;*.png;*.webp;*.bmp|All Files (*.*)|*.*",
				CheckFileExists = true
			};
			if (dlg.ShowDialog() == true && !string.IsNullOrWhiteSpace(dlg.FileName))
			{
				ThemeEditWallpaperPath = dlg.FileName;
			}
		}
		catch (Exception ex)
		{
			System.Diagnostics.Debug.WriteLine($"[ThemeEditor] Failed to select image: {ex.Message}");
		}
	}

	private void ResetThemeEdit()
	{
		ThemeEditBlur = 0.0;
		ThemeEditAlpha = 51.0;
	}

	private void SaveThemeEdit()
	{
		if (!string.IsNullOrEmpty(ThemeEditWallpaperPath))
		{
			BackgroundImagePath = ThemeEditWallpaperPath;
			_db.SetSetting("app_wallpaper", BackgroundImagePath);
		}
		WallpaperBlur = ThemeEditBlur;
		WallpaperAlpha = ThemeEditAlpha;
		_db.SetSetting("app_wallpaper_blur", WallpaperBlur.ToString(System.Globalization.CultureInfo.InvariantCulture));
		_db.SetSetting("app_wallpaper_alpha", WallpaperAlpha.ToString(System.Globalization.CultureInfo.InvariantCulture));
		ExitThemeEditorToTheme();
	}

	private void CancelThemeEdit()
	{
		ExitThemeEditorToTheme();
	}

	/// <summary>
	/// Exits the Theme Editor and returns to the Themes page without pushing theme_edit
	/// onto the navigation history. Clears any stale theme_edit entries from the stack
	/// so the Back button does not loop back into the editor.
	/// </summary>
	private void ExitThemeEditorToTheme()
	{
		// Remove all consecutive theme_edit entries left by OpenThemeEditor push
		var temp = new System.Collections.Generic.List<string>();
		while (_viewHistory.Count > 0)
		{
			var top = _viewHistory.Pop();
			if (!string.Equals(top, "theme_edit", StringComparison.OrdinalIgnoreCase))
			{
				temp.Add(top);
				break;
			}
			// discard theme_edit from history
		}
		for (int i = temp.Count - 1; i >= 0; i--)
		{
			_viewHistory.Push(temp[i]);
		}
		CurrentView = "theme";
		IsDrawerOpen = false;
		ShowSettings = false;
		LoadWallpapers();
		OnPropertyChanged("CanNavigateBack");
	}
	private bool _isLoadingWallpapers;
	/// <returns>true when wallpapers are loaded (or someone else is loading them);
	/// false when aborted because the user interacted — the caller should retry later.</returns>
	private async Task<bool> EnsureWallpapersLoadedAsync(bool forceReload = false, bool abortIfUserActive = false)
	{
		if (!forceReload && Wallpapers.Count > 0)
		{
			FilterWallpapers();
			return true;
		}

		if (_isLoadingWallpapers) return true;
		_isLoadingWallpapers = true;

		try
		{
			string saved = _db.GetSetting("app_wallpaper", "");
			string baseDir = AppDomain.CurrentDomain.BaseDirectory;
			var result = await Task.Run(() =>
			{
				var cats = new List<string>();
				var items = new List<WallpaperItem>();
				var dir = new System.IO.DirectoryInfo(System.IO.Path.Combine(baseDir, "Wallpaper"));
				LogHelper.LogInfo($"[Wallpaper] scan dir='{(dir.Exists ? dir.FullName : "(missing)")}' exists={dir.Exists}");
				if (!dir.Exists) return (cats, items, aborted: false);
				foreach (var sub in dir.GetDirectories())
				{
					if (sub.Name == "Thumbnails") continue;
					cats.Add(sub.Name);
					var thumbDir = new System.IO.DirectoryInfo(System.IO.Path.Combine(baseDir, "Wallpaper", "Thumbnails", sub.Name));
					foreach (var file in sub.GetFiles())
					{
						// Thumb decode is the heavy part (full-res file → 240px).
						// When running from the background gate, hand control back
						// within one file if the user returns — the caller retries
						// after the next quiet window (return-lag guard).
						if (abortIfUserActive && MusicPlayer.Views.SmoothScrollHelper.IsInteractingNow())
						{
							LogHelper.LogInfo($"[Wallpaper] abort (user interacting) after {items.Count} items");
							return (cats, items, aborted: true);
						}
						string ext = file.Extension.ToLowerInvariant();
						if (ext is ".jpg" or ".jpeg" or ".png" or ".webp" or ".bmp")
						{
							string thumbFile = System.IO.Path.Combine(thumbDir.FullName, file.Name);
							if (!System.IO.File.Exists(thumbFile))
							{
								string altExt = ext == ".png" ? ".jpg" : ".png";
								thumbFile = System.IO.Path.Combine(thumbDir.FullName, System.IO.Path.GetFileNameWithoutExtension(file.Name) + altExt);
							}
							string finalThumb = System.IO.File.Exists(thumbFile) ? thumbFile : file.FullName;

							var thumbSource = Converters.WallpaperThumbnailConverter.GetThumbnail(finalThumb, 240);

							items.Add(new WallpaperItem
							{
								FilePath = file.FullName,
								ThumbnailPath = finalThumb,
								ThumbnailSource = thumbSource,
								FileName = System.IO.Path.GetFileNameWithoutExtension(file.Name),
								Category = sub.Name,
								IsSelected = !string.IsNullOrEmpty(saved) && string.Equals(file.FullName, saved, StringComparison.OrdinalIgnoreCase)
							});
						}
					}
				}
				LogHelper.LogInfo($"[Wallpaper] found {items.Count} images in {cats.Count} categories: [{string.Join(", ", cats)}]");
				return (cats, items, aborted: false);
			});

			if (result.aborted) return false;

			await DispatchAsync(() =>
			{
				WallpaperCategories.Clear();
				WallpaperCategories.Add("All");
				WallpaperCategories.AddRange(result.cats);
				Wallpapers.ReplaceAll(result.items);
				if (!string.IsNullOrEmpty(saved) && System.IO.File.Exists(saved))
					BackgroundImagePath = saved;
				FilterWallpapers();
			});
			return true;
		}
		catch { return true; }
		finally
		{
			_isLoadingWallpapers = false;
		}
	}

	private void LoadWallpapers()
	{
		if (Wallpapers.Count > 0)
		{
			FilterWallpapers();
			return;
		}
		_ = EnsureWallpapersLoadedAsync();
	}
	private void FilterWallpapers()
	{
		string cat = _selectedWallpaperCategory;
		var list = new List<WallpaperItem>();
		foreach (var wp in Wallpapers)
		{
			if (cat == "All" || string.Equals(wp.Category, cat, StringComparison.OrdinalIgnoreCase))
				list.Add(wp);
		}
		FilteredWallpapers.ReplaceAll(list);
	}
	private readonly object _settingsSaveLock = new();
	private Task _settingsSaveChain = Task.CompletedTask;
	private long _settingsSaveSeq;

	private void SelectWallpaper(WallpaperItem? item)
	{
		if (item == null) return;
		foreach (var wp in Wallpapers)
		{
			bool should = ReferenceEquals(wp, item);
			if (wp.IsSelected != should) wp.IsSelected = should;
		}
		BackgroundImagePath = item.FilePath;
		SaveWallpaperSettingOrdered(item.FilePath);
	}

	// Ordered, non-blocking settings persistence: chained FIFO on pool threads so
	// rapid selections can never persist out of order (stale-after-newer), with a
	// sequence guard as backstop. Same read-modify-write as before, just off-UI.
	private void SaveWallpaperSettingOrdered(string filePath)
	{
		long seq = System.Threading.Interlocked.Increment(ref _settingsSaveSeq);
		Task prev;
		lock (_settingsSaveLock) { prev = _settingsSaveChain; _settingsSaveChain = prev.ContinueWith(_ => SaveWallpaperSettingNow(filePath, seq), TaskScheduler.Default); }
	}

	private void SaveWallpaperSettingNow(string filePath, long seq)
	{
		try
		{
			if (seq != System.Threading.Volatile.Read(ref _settingsSaveSeq)) return;
			_db.SetSetting("app_wallpaper", filePath);
		}
		catch { }
	}
	private void SelectWallpaperCategory(string? category)
	{
		if (!string.IsNullOrEmpty(category))
			SelectedWallpaperCategory = category;
	}
	private void OpenGenreTracks(string? genre)
	{
		if (genre == null)
		{
			return;
		}
		List<MusicTrack> genreTracks = _db.GetGenreTracks(genre);
		Tracks.Clear();
		foreach (MusicTrack item in genreTracks)
		{
			Tracks.Add(item);
		}
		CurrentView = "songs";
	}
	private void OpenFolderTracks(string? filePath)
	{
		if (filePath == null)
		{
			return;
		}
		string directoryName = Path.GetDirectoryName(filePath);
		if (directoryName == null)
		{
			return;
		}
		List<MusicTrack> folderTracks = _db.GetFolderTracks(directoryName);
		Tracks.Clear();
		foreach (MusicTrack item in folderTracks)
		{
			Tracks.Add(item);
		}
		CurrentView = "songs";
	}
	private void SetOutputDevice(object? deviceParam)
	{
		if (deviceParam is int num)
		{
			_playback.Engine.DeviceNumber = num;
			_settings.OutputDevice = num;
		}
	}

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.ObservablePropertyGenerator", "8.4.0.0")]
	private void OnCurrentViewChanged(string value)
	{
		bool preserveSearch = value is "songs" or "artists" or "albums" or "genres" or "folders" or "artist_detail" or "album_detail";
		if (!preserveSearch)
		{
			IsSearching = false;
			if (!string.IsNullOrEmpty(SearchQuery))
			{
				SearchQuery = string.Empty;
				SearchResults.Clear();
				FilteredArtists.Clear();
				FilteredAlbums.Clear();
				FilteredGenres.Clear();
				OnPropertyChanged("HasNoSearchResults");
			}
		}
		OnPropertyChanged("IsTrackListView");
		OnPropertyChanged("IsSearchableView");
		OnPropertyChanged("CurrentTrackListTitle");
		OnPropertyChanged("DisplayedTracks");
		OnPropertyChanged("CanNavigateBack");
		OnPropertyChanged("HasNoMusic");
	}

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.ObservablePropertyGenerator", "8.4.0.0")]
	private void OnSearchQueryChanged(string value)
	{
		string value2 = value;
		_searchCts?.Cancel();
		_searchCts = new CancellationTokenSource();
		CancellationToken token = _searchCts.Token;
		ObservableCollection<MusicTrack> source = GetCurrentViewSource();
		Task.Run(async delegate
		{
			try
			{
				await Task.Delay(200, token);
				if (!token.IsCancellationRequested)
				{
					List<MusicTrack> results;
					if (string.IsNullOrWhiteSpace(value2))
					{
						results = new List<MusicTrack>();
					}
					else
					{
						string q = value2.Trim();
						results = source.Where((MusicTrack t) => (!string.IsNullOrEmpty(t.Title) && t.Title.Contains(q, StringComparison.OrdinalIgnoreCase)) || (!string.IsNullOrEmpty(t.Artist) && t.Artist.Contains(q, StringComparison.OrdinalIgnoreCase)) || (!string.IsNullOrEmpty(t.Album) && t.Album.Contains(q, StringComparison.OrdinalIgnoreCase))).ToList();
					}
					string qTrim = value2?.Trim() ?? "";
					bool flag = !string.IsNullOrWhiteSpace(qTrim);
					List<MusicTrack> filteredArtists = (flag ? Artists.Where((MusicTrack t) => !string.IsNullOrEmpty(t.Artist) && t.Artist.Contains(qTrim, StringComparison.OrdinalIgnoreCase)).ToList() : new List<MusicTrack>());
					List<MusicTrack> filteredAlbums = (flag ? Albums.Where((MusicTrack t) => !string.IsNullOrEmpty(t.Album) && t.Album.Contains(qTrim, StringComparison.OrdinalIgnoreCase)).ToList() : new List<MusicTrack>());
					List<MusicTrack> filteredGenres = (flag ? Genres.Where((MusicTrack t) => !string.IsNullOrEmpty(t.Genre) && t.Genre.Contains(qTrim, StringComparison.OrdinalIgnoreCase)).ToList() : new List<MusicTrack>());
					Dispatch(delegate
					{
						SearchResults.ReplaceAll(results);
						FilteredArtists.ReplaceAll(filteredArtists);
						FilteredAlbums.ReplaceAll(filteredAlbums);
						FilteredGenres.ReplaceAll(filteredGenres);
						OnPropertyChanged("DisplayedTracks");
						OnPropertyChanged("DisplayedArtists");
						OnPropertyChanged("DisplayedAlbums");
						OnPropertyChanged("DisplayedGenres");
						OnPropertyChanged("HasNoSearchResults");
						OnPropertyChanged("HasNoMusic");
						OnPropertyChanged("IsSearchActive");
					});
				}
			}
			catch (TaskCanceledException)
			{
			}
			catch (Exception exception)
			{
				_logger.LogError(exception, "Search failed for '{Query}'", value2);
			}
		}, token);
	}

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.ObservablePropertyGenerator", "8.4.0.0")]
	private void OnIsSearchingChanged(bool value)
	{
		OnPropertyChanged("DisplayedTracks");
		OnPropertyChanged("DisplayedArtists");
		OnPropertyChanged("DisplayedAlbums");
		OnPropertyChanged("DisplayedGenres");
		OnPropertyChanged("HasNoSearchResults");
		OnPropertyChanged("HasNoMusic");
		OnPropertyChanged("IsSearchActive");
		if (!value && !string.IsNullOrEmpty(SearchQuery))
		{
			SearchQuery = string.Empty;
			SearchResults.Clear();
			FilteredArtists.Clear();
			FilteredAlbums.Clear();
			FilteredGenres.Clear();
		}
	}

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.ObservablePropertyGenerator", "8.4.0.0")]
	private void OnCurrentTrackChanged(MusicTrack? value)
	{
		OnPropertyChanged("CurrentPlayingTrackId");
	}

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.ObservablePropertyGenerator", "8.4.0.0")]
	private void OnVolumeChanged(float value)
	{
		float v = value > 1f ? value / 100f : value;
		_playback.Engine.Volume = v;
		_settings.Volume = v;
	}

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.ObservablePropertyGenerator", "8.4.0.0")]
	private void OnBalanceChanged(float value)
	{
		_playback.Engine.Balance = value;
		_settings.Balance = value;
	}

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.ObservablePropertyGenerator", "8.4.0.0")]
	private void OnSpeedChanged(float value)
	{
		_playback.Engine.Speed = value;
		_settings.Speed = value;
		OnPropertyChanged("CurrentSpeedDisplay");
		OnPropertyChanged("SpeedLog");
		_isSyncingSpeedText = true;
		OnPropertyChanged("SpeedText");
		_isSyncingSpeedText = false;
	}

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.ObservablePropertyGenerator", "8.4.0.0")]
	private void OnPitchChanged(float value)
	{
		_playback.Engine.Pitch = value;
		_settings.Pitch = value;
		OnPropertyChanged("CurrentPitchDisplay");
		OnPropertyChanged("PitchDisplay");
		OnPropertyChanged("PitchLog");
		_isSyncingPitchText = true;
		OnPropertyChanged("PitchText");
		_isSyncingPitchText = false;
	}

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.ObservablePropertyGenerator", "8.4.0.0")]
	private void OnCurrentlyPlayingPageChanged(CurrentlyPlayingPage value)
	{
		OnPropertyChanged("IsAlbumArtPage");
		OnPropertyChanged("IsLyricsPage");
		OnPropertyChanged("IsVisualizerPage");
	}

	private bool _isApplyingEqPreset;
	private readonly object _eqSaveLock = new();
	private readonly float[] _pendingBandSaves = new float[10];
	private readonly bool[] _bandSavePending = new bool[10];
	private Timer? _eqSaveTimer;

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.ObservablePropertyGenerator", "8.4.0.0")]
	private void OnCurrentEqPresetChanged(EqPreset value)
	{
		if (value == null || _isApplyingEqPreset) return;

		_isApplyingEqPreset = true;
		try
		{
			if (value.Name == "Custom")
			{
				float[] gains = new float[10];
				for (int i = 0; i < 10; i++)
				{
					var s = _db.GetSetting($"pref_eq_band_{i}", "0");
					float.TryParse(s, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out gains[i]);
					gains[i] = Math.Clamp(gains[i], -15f, 15f);
				}
				ApplyBandsInternal(gains);
			}
			else
			{
				ApplyBandsInternal(value.BandGains);
			}
			_db.SetSetting("pref_eq_preset", value.Name);
		}
		finally
		{
			_isApplyingEqPreset = false;
		}
	}

	private void ApplyBandsInternal(float[] gains)
	{
		if (gains == null || gains.Length < 10) return;
		Band0 = gains[0];
		Band1 = gains[1];
		Band2 = gains[2];
		Band3 = gains[3];
		Band4 = gains[4];
		Band5 = gains[5];
		Band6 = gains[6];
		Band7 = gains[7];
		Band8 = gains[8];
		Band9 = gains[9];
		_playback.Engine.SetAllBands(gains);
	}

	private void HandleBandChange(int band, float value)
	{
		_playback.Engine.SetBandGain(band, value);
		if (_isApplyingEqPreset) return;

		lock (_eqSaveLock)
		{
			_pendingBandSaves[band] = value;
			_bandSavePending[band] = true;
			_eqSaveTimer?.Dispose();
			_eqSaveTimer = new Timer(_ => FlushPendingBandSaves(), null, 250, Timeout.Infinite);
		}

		if (CurrentEqPreset?.Name != "Custom")
		{
			var custom = EqPresets.FirstOrDefault(p => p.Name == "Custom");
			if (custom != null)
			{
				_currentEqPreset = custom;
				OnPropertyChanged(nameof(CurrentEqPreset));
				_db.SetSetting("pref_eq_preset", "Custom");
			}
		}
	}

	private void FlushPendingBandSaves()
	{
		lock (_eqSaveLock)
		{
			for (int i = 0; i < 10; i++)
			{
				if (_bandSavePending[i])
				{
					_db.SetSetting($"pref_eq_band_{i}", _pendingBandSaves[i].ToString(System.Globalization.CultureInfo.InvariantCulture));
					_bandSavePending[i] = false;
				}
			}
		}
	}

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.ObservablePropertyGenerator", "8.4.0.0")]
	private void OnEqEnabledChanged(bool value)
	{
		_playback.Engine.EqualizerEnabled = value;
		_settings.EqualizerEnabled = value;
	}

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.ObservablePropertyGenerator", "8.4.0.0")]
	private void OnBand0Changed(float value) => HandleBandChange(0, value);

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.ObservablePropertyGenerator", "8.4.0.0")]
	private void OnBand1Changed(float value) => HandleBandChange(1, value);

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.ObservablePropertyGenerator", "8.4.0.0")]
	private void OnBand2Changed(float value) => HandleBandChange(2, value);

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.ObservablePropertyGenerator", "8.4.0.0")]
	private void OnBand3Changed(float value) => HandleBandChange(3, value);

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.ObservablePropertyGenerator", "8.4.0.0")]
	private void OnBand4Changed(float value) => HandleBandChange(4, value);

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.ObservablePropertyGenerator", "8.4.0.0")]
	private void OnBand5Changed(float value) => HandleBandChange(5, value);

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.ObservablePropertyGenerator", "8.4.0.0")]
	private void OnBand6Changed(float value) => HandleBandChange(6, value);

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.ObservablePropertyGenerator", "8.4.0.0")]
	private void OnBand7Changed(float value) => HandleBandChange(7, value);

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.ObservablePropertyGenerator", "8.4.0.0")]
	private void OnBand8Changed(float value) => HandleBandChange(8, value);

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.ObservablePropertyGenerator", "8.4.0.0")]
	private void OnBand9Changed(float value) => HandleBandChange(9, value);

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.ObservablePropertyGenerator", "8.4.0.0")]
	private void OnBassBoostStrengthChanged(float value)
	{
		_playback.Engine.BassBoostStrength = value;
		_settings.BassBoostStrength = value;
	}

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.ObservablePropertyGenerator", "8.4.0.0")]
	private void OnBassBoostEnabledChanged(bool value)
	{
		_playback.Engine.BassBoostEnabled = value;
		_settings.BassBoostEnabled = value;
	}

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.ObservablePropertyGenerator", "8.4.0.0")]
	private void OnVirtualizerStrengthChanged(float value)
	{
		_playback.Engine.VirtualizerStrength = value;
		_settings.VirtualizerStrength = value;
	}

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.ObservablePropertyGenerator", "8.4.0.0")]
	private void OnVirtualizerEnabledChanged(bool value)
	{
		_playback.Engine.VirtualizerEnabled = value;
		_settings.VirtualizerEnabled = value;
	}

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.ObservablePropertyGenerator", "8.4.0.0")]
	private void OnReverbPresetChanged(ReverbPreset value)
	{
		_playback.Engine.ReverbPreset = value;
		_db.SetSetting("pref_reverb_preset", ((int)value).ToString());
	}

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.ObservablePropertyGenerator", "8.4.0.0")]
	private void OnReverbEnabledChanged(bool value)
	{
		_playback.Engine.ReverbEnabled = value;
		_db.SetSetting("pref_reverb_enabled", value.ToString());
	}

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.ObservablePropertyGenerator", "8.4.0.0")]
	private void OnLoudnessEnabledChanged(bool value)
	{
		_playback.Engine.LoudnessEnabled = value;
		_db.SetSetting("pref_loudness_enabled", value.ToString());
	}

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.ObservablePropertyGenerator", "8.4.0.0")]
	private void OnLoudnessGainChanged(float value)
	{
		_playback.Engine.LoudnessGain = value;
		_db.SetSetting("pref_loudness_gain", value.ToString());
	}

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.ObservablePropertyGenerator", "8.4.0.0")]
	private void OnCurrentSoundEffectChanged(int value)
	{
		_playback.Engine.ActiveSoundEffect = (SoundEffectType)value;
		_settings.SoundEffect = value;
		if (value == 4)
		{
			if (!IsMagicSoundOn)
			{
				_isMagicSoundOn = true;
				OnPropertyChanged("IsMagicSoundOn");
			}
		}
		else if (IsMagicSoundOn)
		{
			_isMagicSoundOn = false;
			OnPropertyChanged("IsMagicSoundOn");
		}
	}

	public void ResetEqualizer()
	{
		_isApplyingEqPreset = true;
		try
		{
			float[] flat = new float[10];
			ApplyBandsInternal(flat);
			_playback.Engine.ResetEqualizer();

			for (int i = 0; i < 10; i++)
			{
				_db.SetSetting($"pref_eq_band_{i}", "0");
			}

			var normal = EqPresets.FirstOrDefault(p => p.Name == "Normal") ?? EqPresets.FirstOrDefault(p => p.Name == "Flat");
			if (normal != null)
			{
				_currentEqPreset = normal;
				OnPropertyChanged(nameof(CurrentEqPreset));
				_db.SetSetting("pref_eq_preset", normal.Name);
			}
		}
		finally
		{
			_isApplyingEqPreset = false;
		}
	}

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.ObservablePropertyGenerator", "8.4.0.0")]
	private void OnIsMagicSoundOnChanged(bool value)
	{
		if (value)
		{
			if (CurrentSoundEffect != 4)
			{
				CurrentSoundEffect = 4;
			}
		}
		else if (CurrentSoundEffect == 4)
		{
			CurrentSoundEffect = -1;
		}
	}

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.ObservablePropertyGenerator", "8.4.0.0")]
	private void OnSelectedLrcFolderChanged(string value)
	{
		string value2 = value;
		if (value2 == "ALL")
		{
			LrcLocalFiles.ReplaceAll(_lrcAllFiles);
			return;
		}
		(string, string[]) tuple = _lrcFolderData.FirstOrDefault<(string, string[])>(((string folder, string[] files) f) => f.folder == value2);
		LrcLocalFiles.ReplaceAll(tuple.Item2);
	}
}
