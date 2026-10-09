# جدول التدقيق الشامل لجميع العناصر التفاعلية (Full Interaction Recreation Audit)

هذا المستند يوثق كل عنصر تفاعلي في التطبيق، ومطابقته المباشرة مع كود وموارد تطبيق الأندرويد الأصلي (`media.music.musicplayer`)، وكيفية تنفيذه وتكييفه في بيئة Windows، وحالته التشغيلية واختباره الفعلي.

---

## 1. شاشة البداية والتنقل الرئيسي (Main & Navigation)

| Screen | Element | Original Android ID / Resource | Original Action | Windows Element | Windows Command / Handler | Backend Dependency | Current Status | Test Status |
|---|---|---|---|---|---|---|---|---|
| **Top Bar** | Navigation Drawer Button | `R.id.btn_menu` / `ic_menu` | فتح القائمة الجانبية (DrawerLayout) | `Button` (AppBar) | `ToggleDrawerCommand` | `MainViewModel._isDrawerOpen` | ✅ Implemented | Passed |
| **Top Bar** | App Title Text | `R.id.tv_app_title` | عرض اسم التطبيق | `TextBlock` | Static text display | N/A | ✅ Implemented | Passed |
| **Top Bar** | Search Box & Clear | `R.id.et_search` / `ic_search_clear` | كتابة نص البحث مع Debounce وحذف النص | `TextBox` + `Button` | `SearchQuery` (TwoWay) + `ClearSearch_Click` | `DatabaseService.SearchTracks` | ✅ Implemented | Passed |
| **Top Bar** | Scan Button | `R.id.btn_scan` / `ic_scan` | بدء فحص مكتبة الموسيقى مع شريط تقدم | `Button` | `ScanMusicCommand` | `MusicScanner.ScanFoldersAsync` | ✅ Implemented | Passed |
| **Top Bar** | Shuffle All Button | `R.id.btn_shuffle_all` / `ic_shuffle` | خلط وتشغيل جميع أغاني المكتبة | `Button` | `ShuffleAllCommand` | `DatabaseService.GetAllTracks` + `AudioEngine` | ✅ Implemented | Passed |
| **Top Bar** | Minimize Window | N/A (Windows Specific) | تصغير النافذة إلى شريط المهام | `Button` | `Minimize_Click` | `WindowState.Minimized` | ✅ Implemented | Passed |
| **Top Bar** | Maximize / Restore | N/A (Windows Specific) | تكبير النافذة مع احترام WorkArea | `Button` | `Maximize_Click` + `WM_GETMINMAXINFO` | `WindowState.Maximized` | ✅ Implemented | Passed |
| **Top Bar** | Close App | N/A (Windows Specific) | إغلاق التطبيق وحفظ الحالة | `Button` | `Close_Click` | `Application.Current.Shutdown` | ✅ Implemented | Passed |

---

## 2. القائمة الجانبية (Navigation Drawer - `main_menu.xml`)

| Screen | Element | Original Android Resource | Original Action | Windows Element | Windows Command / Handler | Backend Dependency | Current Status | Test Status |
|---|---|---|---|---|---|---|---|---|
| **Drawer** | Home Item | `R.id.menu_home` | التنقل لصفحة Home | `Button` | `DrawerHome_Click` | `CurrentView = "home"` | ✅ Implemented | Passed |
| **Drawer** | All Songs Item | `R.id.menu_all_songs` | فتح قائمة الأغاني الكاملة | `Button` | `NavigateCommand("songs")` | `CurrentView = "songs"` | ✅ Implemented | Passed |
| **Drawer** | Albums Item | `R.id.menu_albums` | فتح قائمة الألبومات | `Button` | `NavigateCommand("albums")` | `DatabaseService.GetAllAlbums` | ✅ Implemented | Passed |
| **Drawer** | Artists Item | `R.id.menu_artists` | فتح قائمة الفنانين | `Button` | `NavigateCommand("artists")` | `DatabaseService.GetAllArtists` | ✅ Implemented | Passed |
| **Drawer** | Genres Item | `R.id.menu_genres` | فتح قائمة الأنواع الموسيقية | `Button` | `NavigateCommand("genres")` | `DatabaseService.GetAllGenres` | ✅ Implemented | Passed |
| **Drawer** | Folders Item | `R.id.menu_folders` | فتح متصفح المجلدات | `Button` | `NavigateCommand("folders")` | `DatabaseService.GetAllFolders` | ✅ Implemented | Passed |
| **Drawer** | Favorites Item | `R.id.menu_favorites` | فتح قائمة الأغاني المفضلة | `Button` | `NavigateCommand("favorites")` | `DatabaseService.GetFavoriteTracks` | ✅ Implemented | Passed |
| **Drawer** | Equalizer Item | `R.id.menu_equalizer` | فتح نافذة المعادل الصوتي | `Button` | `ShowEqualizerPanelCommand` | `ShowEqualizer = true` | ✅ Implemented | Passed |
| **Drawer** | Sound Effects Item | `R.id.menu_effect` | فتح نافذة المؤثرات الصوتية | `Button` | `ShowSoundEffectsPanelCommand` | `ShowSoundEffects = true` | ✅ Implemented | Passed |
| **Drawer** | Sleep Timer Item | `R.id.menu_sleep` | فتح مؤقت النوم | `Button` | `ShowSleepTimerCommand` | `SleepTimerService` | ✅ Implemented | Passed |
| **Drawer** | Scan Music Item | `R.id.menu_scan` | تشغيل فحص المجلدات | `Button` | `ScanMusicCommand` | `MusicScanner` | ✅ Implemented | Passed |
| **Drawer** | Settings Item | `R.id.menu_settings` | فتح شاشة الإعدادات التفضيلية | `Button` | `NavigateCommand("settings")` | `CurrentView = "settings"` | ✅ Implemented | Passed |
| **Drawer** | Exit Item | `R.id.menu_exit` | إنهاء التطبيق | `Button` | `Close_Click` | `Window.Close()` | ✅ Implemented | Passed |

---

## 3. بطاقات الصفحة الرئيسية (Home Cards & Playlists)

| Screen | Element | Original Android Resource | Original Action | Windows Element | Windows Command / Handler | Backend Dependency | Current Status | Test Status |
|---|---|---|---|---|---|---|---|---|
| **Home** | Card: All Songs | `R.id.card_songs` | فتح قائمة الأغاني مع إظهار العدد | `Border` | `AllSongsCard_Click` | `Tracks.Count` | ✅ Implemented | Passed |
| **Home** | Card: Folders | `R.id.card_folders` | فتح المجلدات مع إظهار العدد | `Border` | `FoldersCard_Click` | `Folders.Count` | ✅ Implemented | Passed |
| **Home** | Card: Favorites | `R.id.card_favorites` | فتح المفضلة مع إظهار العدد | `Border` | `FavoritesCard_Click` | `FavoriteTracks.Count` | ✅ Implemented | Passed |
| **Home** | Card: Albums | `R.id.card_albums` | فتح الألبومات مع إظهار العدد | `Border` | `AlbumsCard_Click` | `Albums.Count` | ✅ Implemented | Passed |
| **Home** | Card: Artists | `R.id.card_artists` | فتح الفنانين مع إظهار العدد | `Border` | `ArtistsCard_Click` | `Artists.Count` | ✅ Implemented | Passed |
| **Home** | Card: Genres | `R.id.card_genres` | فتح الأنواع مع إظهار العدد | `Border` | `GenresCard_Click` | `Genres.Count` | ✅ Implemented | Passed |
| **Home** | New Playlist Button | `R.id.btn_new_playlist` | فتح حوار إنشاء قائمة تشغيل جديدة | `Button` | `NewPlaylist_Click` | `NewPlaylistDialog` + `DatabaseService.InsertPlaylist` | ✅ Implemented | Passed |
| **Home** | Playlist Tile Click | `R.id.item_playlist` | تشغيل قائمة التشغيل من البداية | `Border` | `PlaylistItem_Click` | `DatabaseService.GetPlaylistTracks` + `PlaybackService.PlayList` | ✅ Implemented | Passed |
| **Home** | Playlist Context Menu | `R.menu.menu_playlist_item` | فتح قائمة الخيارات (Play, Rename, Delete) | `ContextMenu` | `PlayPlaylist`, `RenamePlaylist`, `DeletePlaylist` | `DatabaseService` | ✅ Implemented | Passed |

---

## 4. شريط المشغل المصغر (Mini Player - `bottom_controller.xml`)

| Screen | Element | Original Android Resource | Original Action | Windows Element | Windows Command / Handler | Backend Dependency | Current Status | Test Status |
|---|---|---|---|---|---|---|---|---|
| **Mini Player** | Card / Track Info | `R.id.layout_mini_player` | الضغط يفتح شاشة المشغل الكاملة | `Grid` | `MiniPlayer_Click` -> `OpenFullPlayerCommand` | `ShowFullPlayer = true` | ✅ Implemented | Passed |
| **Mini Player** | Album Artwork | `R.id.iv_mini_cover` | عرض صورة غلاف المسار الحالي | `Image` | `AlbumArtConverter` | `AlbumArtLoader.LoadAlbumArt` | ✅ Implemented | Passed |
| **Mini Player** | Track Title & Artist | `R.id.tv_mini_title`, `tv_mini_artist` | عرض نصوص المسار الحالي | `TextBlock` | `CurrentTrack.Title`, `CurrentTrack.Artist` | `AudioEngine.CurrentTrack` | ✅ Implemented | Passed |
| **Mini Player** | Progress Line | `R.id.pb_mini_progress` | شريط رفيع علوي يوضح نسبة التقدم | `ProgressBar` | `PlaybackProgress` | `Position / Duration` | ✅ Implemented | Passed |
| **Mini Player** | Shuffle Button | `R.id.btn_mini_shuffle` | تبديل وضع الخلط العشوائي | `Button` | `ToggleShuffleCommand` | `AudioEngine.ShuffleEnabled` | ✅ Implemented | Passed |
| **Mini Player** | Previous Button | `R.id.btn_mini_prev` | الانتقال للمقطع السابق | `Button` | `PreviousCommand` | `AudioEngine.Previous()` | ✅ Implemented | Passed |
| **Mini Player** | Play / Pause Button | `R.id.btn_mini_play_pause` | تشغيل / إيقاف مؤقت | `Button` | `PlayPauseCommand` | `AudioEngine.Play()` / `Pause()` | ✅ Implemented | Passed |
| **Mini Player** | Next Button | `R.id.btn_mini_next` | الانتقال للمقطع التالي | `Button` | `NextCommand` | `AudioEngine.Next()` | ✅ Implemented | Passed |
| **Mini Player** | Repeat Button | `R.id.btn_mini_repeat` | تبديل أوضاع التكرار (Order/All/One/Shuffle) | `Button` | `ToggleRepeatCommand` | `AudioEngine.PlayMode` | ✅ Implemented | Passed |
| **Mini Player** | Speed / Pitch Chip | N/A (Windows Quick Access) | فتح حوار السرعة والنبرة | `Button` | `ShowSpeedPitchPanelCommand` | `ShowSpeedPitch = true` | ✅ Implemented | Passed |
| **Mini Player** | Equalizer Button | `R.id.btn_mini_eq` | فتح نافذة المعادل الصوتي | `Button` | `ShowEqualizerPanelCommand` | `ShowEqualizer = true` | ✅ Implemented | Passed |
| **Mini Player** | Sound Effects Button | `R.id.btn_mini_effect` | فتح نافذة المؤثرات الصوتية | `Button` | `ShowSoundEffectsPanelCommand` | `ShowSoundEffects = true` | ✅ Implemented | Passed |
| **Mini Player** | Favorite Toggle | `R.id.btn_mini_favorite` | إضافة/إزالة من المفضلة | `Button` | `ToggleFavoriteCommand` | `DatabaseService.ToggleFavorite` | ✅ Implemented | Passed |
| **Mini Player** | Queue Button | `R.id.btn_mini_queue` | فتح قائمة الانتظار الجانبية | `Button` | `ShowQueuePanelCommand` | `ShowQueue = true` | ✅ Implemented | Passed |

---

## 5. شاشة المشغل الكاملة (Full Player - `activity_musicplay.xml`)

| Screen | Element | Original Android Resource | Original Action | Windows Element | Windows Command / Handler | Backend Dependency | Current Status | Test Status |
|---|---|---|---|---|---|---|---|---|
| **Full Player** | Back / Minimize | `R.id.btn_play_back` | إغلاق المشغل الكامل والعودة للواجهة | `Button` | `CloseFullPlayerCommand` | `ShowFullPlayer = false` | ✅ Implemented | Passed |
| **Full Player** | Title & Artist | `R.id.tv_play_title`, `tv_play_artist` | عرض بيانات المقطع بحجم عريض | `TextBlock` | `CurrentTrack.Title`, `CurrentTrack.Artist` | `AudioEngine.CurrentTrack` | ✅ Implemented | Passed |
| **Full Player** | Large Album Art | `R.id.iv_play_cover` | عرض صورة الغلاف بزوايا مستديرة وظل | `Image` | `AlbumArtConverter` | `AlbumArtLoader` | ✅ Implemented | Passed |
| **Full Player** | Blurred Background | `R.id.iv_play_bg` | خلفية مموهة ومظللة بالألبوم | `Image` + `Border` | `AlbumArtConverter` + Scrim | `AlbumArtLoader` | ✅ Implemented | Passed |
| **Full Player** | Synced Lyrics View | `R.id.lyric_view` | عرض كلمات LRC متزامنة مع التظليل | `ScrollViewer` + `ItemsControl` | `Lyrics` Collection + Progress tracking | `LyricParser` | ✅ Implemented | Passed |
| **Full Player** | Lyric Line Click | `R.id.lyric_item` | القفز الفوري لزمن السطر (Click to Seek) | `TextBlock` (Click) | `LyricLine_Click` -> `SeekToLyricCommand` | `AudioEngine.Seek()` | ✅ Implemented | Passed |
| **Full Player** | Seek Slider | `R.id.sb_play_progress` | شريط السحب والتمرير الزمني | `Slider` | `PlaybackProgress` (TwoWay) | `AudioEngine.Seek()` | ✅ Implemented | Passed |
| **Full Player** | Time Displays | `R.id.tv_play_current_time`, `tv_play_total_time` | عرض الوقت المنقضي والوقت الكلي | `TextBlock` | `Position`, `Duration` (DurationConverter) | `AudioEngine.Position` | ✅ Implemented | Passed |
| **Full Player** | Shuffle Toggle | `R.id.btn_play_shuffle` | تبديل الخلط العشوائي | `Button` | `ToggleShuffleCommand` | `AudioEngine.ShuffleEnabled` | ✅ Implemented | Passed |
| **Full Player** | Rewind 10s | `R.id.btn_play_backward_15` | تأخير 10 ثوان | `Button` | `SeekRelativeCommand(-10)` | `AudioEngine.SeekRelative(-10)` | ✅ Implemented | Passed |
| **Full Player** | Previous Button | `R.id.btn_play_prev` | المسار السابق | `Button` | `PreviousCommand` | `AudioEngine.Previous()` | ✅ Implemented | Passed |
| **Full Player** | Large Play/Pause | `R.id.btn_play_play_pause` | زر دائري كبير 60dp للتشغيل والإيقاف | `Button` | `PlayPauseCommand` | `AudioEngine.Play()/Pause()` | ✅ Implemented | Passed |
| **Full Player** | Next Button | `R.id.btn_play_next` | المسار التالي | `Button` | `NextCommand` | `AudioEngine.Next()` | ✅ Implemented | Passed |
| **Full Player** | Forward 10s | `R.id.btn_play_forward_15` | تقديم 10 ثوان | `Button` | `SeekRelativeCommand(10)` | `AudioEngine.SeekRelative(10)` | ✅ Implemented | Passed |
| **Full Player** | Repeat Mode Toggle | `R.id.btn_play_repeat` | تكرار الكل / تكرار فردي / تشغيل مرتب | `Button` | `ToggleRepeatCommand` | `AudioEngine.PlayMode` | ✅ Implemented | Passed |
| **Full Player** | Speed / Pitch Shortcut | `R.id.btn_play_speed` | فتح نافذة التحكم بالسرعة والنبرة | `Button` | `ShowSpeedPitchPanelCommand` | `ShowSpeedPitch = true` | ✅ Implemented | Passed |
| **Full Player** | Equalizer Shortcut | `R.id.btn_play_equalizer` | فتح معادل الصوت | `Button` | `ShowEqualizerPanelCommand` | `ShowEqualizer = true` | ✅ Implemented | Passed |
| **Full Player** | Sound Effects Shortcut | `R.id.btn_play_effect` | فتح نافذة المؤثرات الصوتية | `Button` | `ShowSoundEffectsPanelCommand` | `ShowSoundEffects = true` | ✅ Implemented | Passed |
| **Full Player** | Favorite Button | `R.id.btn_play_favorite` | تفضيل المسار الحالي | `Button` | `ToggleFavoriteCommand` | `DatabaseService.ToggleFavorite` | ✅ Implemented | Passed |
| **Full Player** | Queue Button | `R.id.btn_play_queue` | فتح قائمة الانتظار | `Button` | `ShowQueuePanelCommand` | `ShowQueue = true` | ✅ Implemented | Passed |

---

## 6. نافذة السرعة والنبرة (Speed & Pitch Control)

| Screen | Element | Original Android Key / Logic | Original Action | Windows Element | Windows Command / Handler | Backend Dependency | Current Status | Test Status |
|---|---|---|---|---|---|---|---|---|
| **Speed/Pitch** | Speed Slider | `preference_play_speed` | تغيير سرعة التشغيل (0.25x - 2.0x) في الوقت الفعلي | `Slider` | `Speed` (TwoWay binding) | `SpeedPitchSampleProvider.Speed` | ✅ Implemented | Passed (Audible real-time) |
| **Speed/Pitch** | Speed Preset Buttons | `0.5x, 0.75x, 1.0x, 1.25x, 1.5x, 2.0x` | ضبط سريع لقيم السرعة | `Button`s | `SetSpeedPresetCommand` | `MainViewModel.Speed` | ✅ Implemented | Passed |
| **Speed/Pitch** | Pitch Slider | `preference_play_pitch` | تغيير نبرة الصوت (-12 إلى +12 نصف نغمة) | `Slider` | `Pitch` (TwoWay binding) | `SpeedPitchSampleProvider.Pitch` | ✅ Implemented | Passed (Audible real-time) |
| **Speed/Pitch** | Reset Button | `Reset` | استعادة السرعة والنبرة الافتراضية (1.0x / 0) | `Button` | `ResetSpeedPitchCommand` | `Speed = 1.0f; Pitch = 0f;` | ✅ Implemented | Passed |
| **Speed/Pitch** | Close Button | `btn_close` | إغلاق النافذة المنبثقة | `Button` | `CloseModal_Click` | `ShowSpeedPitch = false` | ✅ Implemented | Passed |

---

## 7. معادل الصوت (10-Band Equalizer & Enhancers)

| Screen | Element | Original Android Class / FX | Original Action | Windows Element | Windows Command / Handler | Backend Dependency | Current Status | Test Status |
|---|---|---|---|---|---|---|---|---|
| **Equalizer** | EQ Enable Switch | `pref_enable_eq` | تشغيل/تعطيل المعادل الصوتي | `CheckBox` | `EqEnabled` (TwoWay) | `EqualizerSampleProvider.IsEnabled` | ✅ Implemented | Passed |
| **Equalizer** | 23 Presets Selector | `EqPreset.Presets` | تطبيق نمط معادل جاهز فوراً | `ComboBox` | `CurrentEqPreset` | `AudioEngine.SetAllBands` | ✅ Implemented | Passed |
| **Equalizer** | 10 Band Sliders | 31Hz, 62Hz, 125Hz, 250Hz, 500Hz, 1kHz, 2kHz, 4kHz, 8kHz, 16kHz | تحريك كل نطاق من -15dB إلى +15dB | 10 `Slider`s | `Band0` to `Band9` (TwoWay) | `EqualizerSampleProvider.SetBandGain` | ✅ Implemented | Passed (Audible real-time) |
| **Equalizer** | Bass Boost Toggle & Slider | `pref_enable_bass_boost`, `pref_bass_boost` | تعزيز الترددات الخفيضة (Low-shelf 80Hz) | `CheckBox` + `Slider` | `BassBoostEnabled`, `BassBoostStrength` | `EqualizerSampleProvider.BassBoost` | ✅ Implemented | Passed (Audible real-time) |
| **Equalizer** | Virtualizer Toggle & Slider | `pref_enable_virtualizer`, `pref_virtualizer` | توسيع المجال المكاني ستيريو (Mid/Side) | `CheckBox` + `Slider` | `VirtualizerEnabled`, `VirtualizerStrength` | `EqualizerSampleProvider.Virtualizer` | ✅ Implemented | Passed (Audible real-time) |
| **Equalizer** | Balance Slider | `pref_balance` | ضبط توازن القناة اليسرى واليمنى | `Slider` | `Balance` (TwoWay) | `EqualizerSampleProvider.Balance` | ✅ Implemented | Passed (Audible real-time) |
| **Equalizer** | Close Button | `btn_close` | إغلاق نافذة المعادل | `Button` | `CloseModal_Click` | `ShowEqualizer = false` | ✅ Implemented | Passed |

---

## 8. المؤثرات الصوتية الستة (Sound Effects - `ActivityEffectGroup`)

| Screen | Element | Original Effect Class | Original Action | Windows Element | Windows Command / Handler | Backend Dependency | Current Status | Test Status |
|---|---|---|---|---|---|---|---|---|
| **Effects** | Electronic Tube | `f5.c` (Tube saturation) | تفعيل طابع تشبع الصمام المفرغ الدافئ | `Border` Tile | `ToggleSoundEffectCommand("0")` | `SoundEffectProcessor.ElectronicTube` | ✅ Implemented | Passed (Audible warmth) |
| **Effects** | 3D Rotate | `f5.b` (LFO stereo rotation) | تدوير مستمر للمجال الصوتي بين السماعات | `Border` Tile | `ToggleSoundEffectCommand("1")` | `SoundEffectProcessor.3DRotate` | ✅ Implemented | Passed (Audible panning) |
| **Effects** | Tone Low | `f5.h` (Low tone shaping) | تشكيل عميق لنغمة الباس وتخميد التريبل | `Border` Tile | `ToggleSoundEffectCommand("2")` | `SoundEffectProcessor.ToneLow` | ✅ Implemented | Passed (Audible low shaping) |
| **Effects** | Surround Sound | `f5.g` (Spatial Haas effect) | توسيع محيطي للأصوات المسرحية | `Border` Tile | `ToggleSoundEffectCommand("3")` | `SoundEffectProcessor.SurroundSound` | ✅ Implemented | Passed (Audible spatial) |
| **Effects** | Magic Sound | `f5.e` (Harmonic sparkle) | بريق وتألق ناصع للترددات العالية (Exciter) | `Border` Tile | `ToggleSoundEffectCommand("4")` | `SoundEffectProcessor.MagicSound` | ✅ Implemented | Passed (Audible sparkle) |
| **Effects** | Live Treble | `f5.d` (Dynamic treble clarity) | تعزيز ديناميكي لنقاء وحدة الصوت المباشر | `Border` Tile | `ToggleSoundEffectCommand("5")` | `SoundEffectProcessor.LiveTreble` | ✅ Implemented | Passed (Audible clarity) |
| **Effects** | Close Button | `btn_close` | إغلاق نافذة المؤثرات | `Button` | `CloseModal_Click` | `ShowSoundEffects = false` | ✅ Implemented | Passed |

---

## 9. قائمة الانتظار (Play Queue - `ActivityMusicQueue`)

| Screen | Element | Original Android Resource | Original Action | Windows Element | Windows Command / Handler | Backend Dependency | Current Status | Test Status |
|---|---|---|---|---|---|---|---|---|
| **Queue** | Queue Item Click | `R.id.item_queue` | تشغيل المقطع المختار من قائمة الانتظار | `Border` | `TrackRow_Click` | `PlaybackService.PlayTrack` | ✅ Implemented | Passed |
| **Queue** | Remove Item | `R.id.btn_remove_queue` | إزالة المسار المحدد من قائمة الانتظار | `Button` | `RemoveFromQueueCommand` | `AudioEngine.RemoveFromQueue` | ✅ Implemented | Passed |
| **Queue** | Clear Queue | `R.id.btn_clear_queue` | مسح كافة المسارات من قائمة الانتظار | `Button` | `ClearQueueCommand` | `AudioEngine.ClearQueue` | ✅ Implemented | Passed |
| **Queue** | Close Queue | `R.id.btn_close_queue` | إغلاق لوحة قائمة الانتظار الجانبية | `Border` (Backdrop) | `DrawerBackdrop_Click` | `ShowQueue = false` | ✅ Implemented | Passed |

---

## 10. شاشة الإعدادات (Settings - `ActivitySetting`)

| Category | Setting Item | Original Preference Key | Default | Windows Control | Bound Property | Persistence | Status |
|---|---|---|---|---|---|---|---|
| **1. Normal** | Use 10-Band Equalizer | `use_ten_band` | true | CheckBox | `EqEnabled` | SQLite (`pref_enable_eq`) | ✅ Works & Persists |
| **1. Normal** | Show Hidden Folders | `show_hidden_folders` | true | CheckBox | `Settings.ShowHiddenFolders` | SQLite (`show_hidden_folders`) | ✅ Works & Persists |
| **1. Normal** | Show Audio Quality Logo | `show_quality_logo` | false | CheckBox | `Settings.ShowQualityLogo` | SQLite (`show_quality_logo`) | ✅ Works & Persists |
| **1. Normal** | Show Forward / Backward Buttons | `show_forward_backward` | false | CheckBox | `Settings.ShowForwardBackward` | SQLite (`show_forward_backward`) | ✅ Works & Persists |
| **2. Lyrics** | Desktop Floating Lyrics | `show_desktop_lyrics` | false | CheckBox | `Settings.ShowDesktopLyrics` | SQLite (`show_desktop_lyrics`) | ✅ Works & Persists |
| **2. Lyrics** | Bluetooth Lyric Broadcast | `bluetooth_lyric` | true | CheckBox | `Settings.BluetoothLyric` | SQLite (`bluetooth_lyric`) | ✅ Works & Persists |
| **3. Audio** | Volume Fade In / Out | `pref_volume_fade` | false | CheckBox | `Settings.VolumeFade` | SQLite (`pref_volume_fade`) | ✅ Works & Persists |
| **3. Audio** | Gapless Playback | `gapless_playback` | true | CheckBox | `Settings.GaplessPlayback` | SQLite (`gapless_playback`) | ✅ Works & Persists |
| **4. Playlist** | Auto-Add Clicked Songs to Queue | `pref_click_add_queue` | false | CheckBox | `Settings.ClickAddQueue` | SQLite (`pref_click_add_queue`) | ✅ Works & Persists |
| **5. Headset** | Play When Headset Connected | `pref_headset_in_play` | false | CheckBox | `Settings.HeadsetInPlay` | SQLite (`pref_headset_in_play`) | ✅ Works & Persists |
| **5. Headset** | Pause When Headset Unplugged | `pref_headset_out_stop` | true | CheckBox | `Settings.HeadsetOutStop` | SQLite (`pref_headset_out_stop`) | ✅ Works & Persists |
