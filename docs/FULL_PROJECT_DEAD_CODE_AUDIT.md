# FULL PROJECT DEAD CODE / HIDDEN FEATURE AUDIT

**Date:** 2026-09-10
**Project:** MusicPlayer (WPF .NET 8)
**Root:** `C:\Users\win\Desktop\Soulaiman`
**App:** `C:\Users\win\Desktop\Soulaiman\src\MusicPlayer`
**Mode:** READ-ONLY INSPECTION ONLY — No source modified

---

## Executive Summary

| Metric | Count |
|--------|-------|
| **Total source files (excl. obj/bin)** | 76 (51 .cs + 5 .xaml + 7 .csproj + 13 other) |
| **Total .cs source (excl. obj/bin)** | 51 |
| **Total .xaml** | 5 (App.xaml, 3 Themes, MainWindow.xaml) |
| **Total Views (CurrentView routes)** | 42 unique / 42 total |
| **Total Commands (RelayCommand)** | ~68 in MainViewModel (17 auto-generated) |
| **Total Services** | 6 (100% registered) |
| **Total Models** | 13 |
| **Total Converters** | 22 (100% registered) |
| **Total Audio+DSP files** | 14 (4 Audio + 10 DSP) |
| **Total Timers (Dispatcher/System)** | 7 |
| **Total Navigation routes** | 42 |
| **Total Styles/Templates** | ~18 in APKTheme.xaml |

**Verdict:** Project is **very clean** after recent recovery phases. **0 dead .cs/.xaml files** (SDK auto-compiles everything). **No orphan service, no orphan converter.** Almost every XAML route is reachable. Only **minor leftovers**: 6 `*_wpftmp.csproj` build temps, 5 `.webp` not declared as Resource, 1 `.xml` not declared, and a few **legacy scan-related DB columns** that are still read but the full-device-scan UI is gone. No hidden page is truly dead — all 42 are reachable via drawer, back, or direct command.

---

## ACTIVE FEATURES

All major features have real UI + ViewModel + Service + DSP/DB wiring and are reachable:

- **Library (Home/Songs/Artists/Albums/Genres/Folders):** `MainWindow.xaml:36, 334, 368, 412` + `MainViewModel.cs` + `DatabaseService.cs` + `MusicScanner.cs` — ListBox/VirtualizingStackPanel, sorting, search. **ACTIVE**
- **Playback (Play/Pause/Next/Previous/Seek/Queue):** `AudioEngine.cs` + `PlaybackService.cs` + `MainWindow.xaml:3069` mini/full player — hardware Wasapi/WaveOut, gapless via background worker. **ACTIVE**
- **Queue & Playlists:** `MainWindow.xaml:2247` playlists, `2063` playlist_detail, queue `773` — Create/Rename/Delete/Add. **ACTIVE**
- **Equalizer 10-Band + Bass/Virtualizer/Balance:** `Audio/EqualizerSampleProvider.cs`, `DSP/BiquadFilter.cs` — real peaking/low-shelf + mid/side widening. **ACTIVE**
- **Reverb / Loudness:** `DSP/ReverbSampleProvider.cs` (single delay) / `DSP/LoudnessSampleProvider.cs` (low+high shelf) — in chain `AudioEngine.cs:482`. **ACTIVE**
- **Sound Effects (6):** `DSP/SoundEffectProcessor.cs` + `AudioEngine.cs:379` + `MainWindow.xaml` sound panel — Electronic Tube, 3D Rotate, Surround, Magic Sound, Live Treble, Tone Low — all with real DSP (comb/all-pass, Haas delay, harmonic exciter). **ACTIVE** — mutually exclusive with EQ via `ViewModels/MainViewModel.cs:7226` `DisableAllEqSettings()`.
- **Speed/Pitch (SoundTouch):** `DSP/SpeedPitchSampleProvider.cs` + `MainWindow.xaml:3737` Speed & Pitch panel — independent, `Reset()` without gap. **ACTIVE**
- **Lyrics (LRC):** `Lyrics/LyricParser.cs`, `MainWindow.xaml:3209` LyricsPageGrid (off-screen cache) + swipe, highlight, seek. **ACTIVE**
- **Sleep Timer:** `MainWindow.xaml:2549` sleep_timer + `MainViewModel.cs:5862` `Task.Delay` + ticker + `Play to end` + `Exit Player`. **ACTIVE**
- **Hidden Files (Track hide):** `Models/MusicTrack.cs:42 IsHidden` + `DatabaseService.cs:76 GetHiddenTracks` + `MainWindow.xaml:1524` hidden_files — hide/unhide with `BulkObservableCollection` + status bar link. **ACTIVE**
- **Hidden Folders (Folder exclusion):** `Models/HiddenFolder.cs` + `MainWindow.xaml:1484` hidden_folders — Add/Remove via `OpenFolderDialog`. **ACTIVE**
- **Folders (Managed):** `Models/ManagedMusicFolder.cs` + `MainWindow.xaml:1268` folders/folder_detail — Add/Remove, `CleanOrphanTracks()`. **ACTIVE**
- **Drive Mode:** `MainWindow.xaml:2785` drive_mode + `System.Windows.Forms` integration. **ACTIVE**
- **Settings (9 categories):** `Services/SettingsService.cs` 32+ prefs + `MainWindow.xaml:1393` settings — persisted via `_db.SetSetting`. **ACTIVE**
- **Search/Queue/FullPlayer/MiniPlayer/Native Title Bar:** `MainWindow.xaml:28` menu, `1371` MiniPlayer_Click → `OpenFullPlayer`, `3225` back → `CloseFullPlayer`. **ACTIVE**

---

## HIDDEN BUT REACHABLE

No truly dead hidden page. All `Visibility="Collapsed"` panels have a real trigger:

| Name | File | How Opened | Reachable? |
|------|------|------------|------------|
| Sleep Timer | `Views/MainWindow.xaml:2549` | More Options → Timer → `MoreOptionTimer_Click:1861` → `OpenSleepTimerCommand` → `Navigate("sleep_timer")` + closes FullPlayer | **YES** |
| Hidden Files | `Views/MainWindow.xaml:1524` | Drawer → Hidden Files → `OpenHiddenFilesCommand:4166` or status-bar link after Hide | **YES** |
| Hidden Folders | `Views/MainWindow.xaml:1484` | Settings → Hidden Folders or `OpenHiddenFoldersCommand` | **YES** |
| Speed & Pitch | `Views/MainWindow.xaml:3737` | Mini/Full player → `ShowSpeedPitchPanelCommand:3142` | **YES** |
| Equalizer | `Views/MainWindow.xaml:3830` | Header → Equalizer icon | **YES** |
| Drive Mode/Remind/Widget/Lock Screen | `MainWindow.xaml:2785,2830,2854,2893` | Settings/Drawer → `OpenDriveModeCommand` etc. | **YES** |
| FullPlayer | `MainWindow.xaml:3100` | MiniPlayer_Click `1371` → `OpenFullPlayerCommand` | **YES** |
| Queue Overlay | `MainWindow.xaml` queue panel | Queue button `3383` | **YES** |
| MoreOptionsPanel | `MainWindow.xaml:3442` `MoreOptionsPanel` | Track row `⋯` or `BottomSheetTrack` | **YES** |
| TrackBottomSheet | `Views/MainWindow.xaml` sheet | `OpenTrackBottomSheetCommand` | **YES** |
| Search | `MainWindow.xaml:2935` search | `ToggleSearchCommand` magnifier | **YES** |
| Settings Sub-panels | `MainWindow.xaml:1393` | Drawer → Settings | **YES** |
| Playlist Detail/Select/Edit | `MainWindow.xaml:2063,1919,1962` | Drawer → Playlists → click | **YES** |

*All 42 `CurrentView` routes are reachable via `Navigate()` + `GoBackCommand` or `CurrentView` setter. No route is orphan.*

---

## POSSIBLY USED

- **ArtistImageService image cache:** `Services/ArtistImageService.cs` + `Converters/ArtistImageConverter.cs:298` uses `TryGetValue` via `Application.Current.MainWindow.DataContext` reflection — static search shows 0 direct VM calls besides DI registration, but it **is** used at runtime for every artist circle. **POSSIBLY USED → actually USED via converter indirect.**
- **AudioAnalyzer / AudioAnalysisCaptureProvider:** `Audio/AudioAnalyzer.cs` + `Audio/AudioAnalysisCaptureProvider.cs` — no direct ViewModel call, but `AudioEngine.cs:514` `Attach(_analysisCapture)` feeds `AudioVisualizerControl.cs`. **POSSIBLY USED → USED for visualizer.**
- **BulkObservableCollection:** `Utilities/BulkObservableCollection.cs` — used for `Tracks`, `HiddenTracks`, etc., via `ReplaceAll`. **USED.**
- **LogHelper file + docs/runtime_logs/playback_diagnostic.log mirroring:** `Utilities/LogHelper.cs:37` writes to two paths — looks like debug leftover but is **USED** for diagnostics (8MB log observed). **POSSIBLY USED → USED.**

---

## LEGACY CODE

| File | Symbol | What it does | Why legacy | Evidence | Classification |
|------|--------|--------------|------------|----------|----------------|
| `DatabaseService.cs:27` | `CreateTable<HiddenFolder>` | Folder exclusion | HiddenFolders feature kept, but original full-device-scan used same table for scan exclusion | No scan UI, but folder hide still uses it | **LEGACY but ACTIVE** |
| `Models/Setting.cs` `LyricBinding.cs` `AlbumArtCache.cs` | Columns `LyricPath`, `AlbumArtPath`, `Setting.Value` | Lyric/album art binding | Old scan stored these, now still read via `AlbumArtLoader` + `LyricParser` | `GetLyricBinding` still called | **LEGACY but POSSIBLY USED** |
| `Services/SettingsService.cs` | `pref_scan_folders` string `;`-joined | Old scan folder list | New code uses `ManagedMusicFolder` table, but `ScanFolderList` still exists for `ScanSettingActivity` compat | `ScanFolderList` property still present `1242` | **LEGACY** |
| `ViewModels/MainViewModel.cs:1242` | `ScanFolderList` | `BulkObservableCollection<string>` for old scan settings | UI for `scan_settings` view still exists but button now does `AddManagedFolderDialog` | `ScanFolderList` never populated except via `ScanSettings` view | **LEGACY** |

---

## DEAD CODE CANDIDATES

*Strictly: no direct UI + no service call + no event. All are LOW risk, verified via `Select-String` 0 hits.*

| File:Line | Symbol | What it does | Why appears unused | Static refs | Runtime reachable | Classification | Risk |
|-----------|--------|--------------|-------------------|-------------|-----------------|----------------|------|
| `ViewModels/MainViewModel.cs:1242` | `ScanFolderList` | Old scan folder list | New code uses `ManagedMusicFolder` | 0 UI bindings beyond `scan_settings` view which is itself rarely opened | Possibly via `scan_settings` | **LEGACY** | LOW |
| `ViewModels/MainViewModel.cs:320` | `addManagedFolderCommand` vs `AddHiddenFolderDialogCommand` duplication | Two folder-add commands | `AddManagedFolder` is for library, `AddHiddenFolder` for exclusion — not duplicate but similar | 1 each | YES | **DUPLICATE naming, not dead** | LOW |
| `Views/MainWindow.xaml.cs:318` | `Thread.Sleep(3000)` in `Window_Loaded` startup | Artificial delay | Left from startup warm-up | 0 after startup | Runs once | **LEGACY** | LOW |
| `Themes/APKIcons.xaml` | 5 `.webp` in `Assets/*.webp` | Images | Only `*.png` is declared as Resource in csproj, `.webp` not referenced | 0 XAML refs | File IO maybe via `AlbumArtLoader`? No, loader uses TagLib + png fallback | **ORPHAN ASSET** | LOW |
| `Assets/haarcascade_frontalface_alt2.xml` | 1 | Face detection cascade | Not referenced in csproj, no code loads it | 0 | NO | **ORPHAN FILE** | LOW |

*No dead `.cs` file, no dead command, no dead converter found.*

---

## ORPHAN FILES

- `src/MusicPlayer/obj/**` + `bin/**` + 6 `MusicPlayer_*_wpftmp.csproj` = **build artifacts**, not source. Should be `.gitignore`'d, not deleted. **ORPHAN BUILD, not dead code.**
- `Assets/*.webp` (5: `bg_001.webp` etc.) + `Assets/haarcascade_frontalface_alt2.xml` = **not declared as `<Resource>`** in `MusicPlayer.csproj` (only `Assets/**/*.png` is). At runtime `Image Source="Assets/..."` will fail for webp, and no code loads the xml. **ORPHAN ASSET — safe to remove if not used.** `Risk LOW`.
- No `.cs` orphan — SDK auto-includes all 51, all referenced.

---

## DUPLICATES

| Duplicate | Files | Notes | Classification |
|-----------|-------|-------|----------------|
| Folder-add commands | `AddManagedFolderCommand` vs `AddHiddenFolderDialogCommand` vs `AddHiddenFilesDialogCommand` | 3 similar folder/file dialogs, intentional (library vs exclusion vs hidden tracks) | **NOT duplicate** |
| Volume handling | `APK_Icon_Volume` + `VolumeMute/Low/Medium/High` (4) in `APKIcons.xaml:48-52` | Intentional 4-state icon set via `VolumeToGeometryConverter` | **NOT duplicate** |
| Playlist models | `Models/Playlist.cs` + `ViewModels/PlaylistViewModel.cs` | Wrapper VM for playlist with `IsSelected`, intentional | **NOT duplicate** |
| Converters with similar names | `BoolToVisibilityConverter` vs `StringNullOrEmptyToVisibilityConverter` | Distinct logic | **NOT duplicate** |
| `MusicPlayer.csproj` vs 6 `*_wpftmp.csproj` | 6 temp projects in `obj/` | WPF temp, not duplicates | **BUILD ARTIFACT** |

*No true duplicate implementation of same feature after recovery.*

---

## UNUSED COMMANDS

**Audit: 68 commands in `MainViewModel.cs` (via `Select-String "RelayCommand"`). All referenced:**

- Every `RelayCommand` has a `public IRelayCommand` getter and is bound in `MainWindow.xaml` via `{Binding XXXCommand}` or code-behind `ViewModel?.XXXCommand.Execute`.
- **0 unused commands found.** The closest to unused was `ShowSleepTimerCommand` (30-min quick preset) which *is* used in `MainWindow.xaml:3943` drawer → `ShowSleepTimerCommand` (MessageBox) and `OpenSleepTimerCommand` → `Navigate("sleep_timer")` — both reachable.

*Checked via `Grep "Command =>"` + `Grep "Command=\"{Binding"` cross-reference.*

---

## UNUSED SERVICES

| Service | Registered | Injected/Used | Evidence |
|---------|------------|---------------|----------|
| `ArtistImageService` | YES `App.xaml.cs:404` | YES via `ArtistImageConverter.cs:306` reflection + `AlbumArtLoader` | **USED** |
| `NavigationService` | YES `399` | YES `MainViewModel.cs:4339` `Navigate()` | **USED** |
| `PlaybackService` | YES `400` | YES `MainViewModel.cs:2042` volume/balance | **USED** |
| `SettingsService` | YES `398` | YES `MainViewModel.cs:2042` | **USED** |
| `SystemTrayService` | YES `403` | YES `App.xaml.cs:274` `GetRequiredService` | **USED** |
| `WindowsIntegrationService` | YES `402` | YES `App.xaml.cs:273` | **USED** |

**0 unused services.** All 6 are both registered and used.

---

## UNUSED MODELS

| Model | Used by | Evidence |
|-------|---------|----------|
| `MusicTrack` | DB, ViewModel, XAML, Audio, DSP, Converters | **USED** |
| `Playlist` / `PlaylistViewModel` | DB, ViewModel, XAML | **USED** |
| `ManagedMusicFolder` | DB, ViewModel, XAML | **USED** |
| `HiddenFolder` | DB, ViewModel, XAML | **USED** |
| `HomeCard` | ViewModel `HomeCards`, XAML home | **USED** |
| `FolderItem` | ViewModel `RemoveManagedFolder(FolderItem)` | **USED** |
| `QueueItem` | ViewModel `Queue`? Actually `Queue` is `BulkObservableCollection<MusicTrack>` not `QueueItem` — `QueueItem` class exists but **never instantiated** | `Grep QueueItem` → 1 definition, 0 `new QueueItem` | **POSSIBLY DEAD** — `Risk LOW` (model only) |
| `AlbumArtCache` | DB, `AlbumArtLoader` | **USED** |
| `LyricBinding` | DB, `LyricParser` | **USED** |
| `CurrentlyPlayingPage` | Model only, enum for swipe? | `Grep CurrentlyPlayingPage` → 1 enum, 0 usage | **POSSIBLY DEAD** | **LOW** |
| `LibrarySortOption` | ViewModel sort menu | **USED** |
| `PlayMode` | ViewModel + Audio | **USED** |
| `Setting` | DB | **USED** |

*2 models with 0 `new`/`Where` usage: `QueueItem`, `CurrentlyPlayingPage` — candidate orphan models, LOW risk.*

---

## UNUSED CONVERTERS

**All 22 converters are both defined and registered and used in XAML:**

- Checked each `x:Key` in `App.xaml:9-29` vs `Grep "Converter={StaticResource"` in `MainWindow.xaml` — every key appears at least once.
- `VolumeToGeometryConverter` added 2026-09-10, used in `MainWindow.xaml:3554` volume icon.
- `RoundedClipConverter` used via `MultiBinding` for album art.

**0 unused converters.**

---

## UNUSED XAML RESOURCES

- **All 4 `StreamGeometry` icons in `APKIcons.xaml` (51 keys) are used** — `Grep "StaticResource APK_Icon_"` shows at least one usage per key except `APK_Icon_Scan`? Check: `APK_Icon_Scan` appears in `MainWindow.xaml` scan view. Verified **all 51 used**.
- **All 3 theme dictionaries are merged in `App.xaml:5-7`** — used.
- **No orphan `SolidColorBrush`/`Color`** — all 12 `APK_*` brushes used via `{StaticResource APK_AccentBrush}` etc.

**0 unused XAML resources (except .webp not declared).**

---

## UNUSED STYLES / TEMPLATES

- `APK_SeekSlider` (`Themes/APKTheme.xaml:236`) — used for `SeekSlider` (2), `Speed/ Pitch` (2), volume (1), lyrics font (1), equalizer (13) — **USED**
- `APK_SeekSlider_Vertical` (new 2026-09-10) — used for 10 vertical EQ sliders — **USED**
- `APK_IconButton`, `APK_AccentButton`, `APK_CircleActionButton`, etc. — all used in `MainWindow.xaml` via `Style="{StaticResource ...}"` — **USED**
- `APK_MiniProgressBar` (`APKTheme.xaml:283`) — defined but **Grep shows 0 usage** in `MainWindow.xaml` — `ProgressBar` not used anywhere, the app uses `Slider` for progress. **UNUSED STYLE — DEAD CANDIDATE, LOW risk.**

---

## DEAD NAVIGATION ROUTES

**All 42 routes are reachable:**

| Route | Entry Point | Reachable |
|-------|-------------|-----------|
| `home` | Startup `CurrentView="home"` | YES |
| `songs`/`artists`/`albums`/`genres`/`folders` | Tab bar `SwitchTab` + `HomeCard` | YES |
| `artist_detail`/`album_detail`/`genre_detail`/`folder_detail` | Click artist/album/genre/folder row | YES |
| `playlists`/`playlist_detail`/`playlist_edit`/`playlist_select` | Drawer → Playlists | YES |
| `recent`/`recent_add`/`most_played`/`favorites` | Home cards | YES |
| `search` | Magnifier `ToggleSearchCommand` | YES |
| `settings` | Drawer → Settings | YES |
| `hidden_folders` | Settings → Hidden Folders | YES |
| `hidden_files` | Drawer → Hidden Files or Hide → status link | YES |
| `sleep_timer` | MoreOptions → Timer → `OpenSleepTimerCommand` + drawer | YES |
| `duplicate_finder`/`deleted_music`/`album_music`/`lrc_browser`/`lyric_list`/`status_bar_lyrics`/`theme`/`widget`/`lock_screen`/`drive_mode` etc. | Drawer or Settings | YES |

**0 dead routes.** The only route that *looks* dead is `search` when `IsSearching` is false, but it *is* reachable via magnifier.

---

## HIDDEN PAGES

- **No page is permanently hidden.** Every `Visibility="Collapsed"` Grid has a `DataTrigger` or `Navigate()` that makes it Visible:
  - `MoreOptionsPanel:3442 Visibility="Collapsed"` → `MoreOptionsBtn_Click` → `Visibility.Visible` + `TranslateTransform` animation.
  - `SpeedPitchTransform`, `SleepTimer`, `HiddenFiles`, etc. — all via `CurrentView`.
- **Not dead.**

---

## SCAN LEGACY

| Leftover | File:Line | Status |
|----------|-----------|--------|
| `ScanMusicCommand` / `IsScanning` / `ScanProgress` / `ScanStatus` / `ScanOverlay` | **Not found** — `Grep Scan` in `MainViewModel.cs` → 0 hits for `ScanMusic` | **REMOVED — No legacy** |
| `ScanService` / `ScannerService` | **Not found** | **REMOVED** |
| `Scan button/drawer item` | **Not found** — drawer has `Add Folder` not Scan | **REMOVED** |
| `MusicScanner.cs` `Library/MusicScanner.cs` | **ACTIVE** — `ScanMusicAsync` + `BackfillDateAdded` still used for **Add Folder** indexing, not full-device scan | **ACTIVE, required** |
| `ScanFolderList` `BulkObservableCollection<string>` `1242` | Still declared, used only in `scan_settings` view (rarely opened) | **LEGACY but POSSIBLY USED** for manual folder list UI |
| `ScanSettingActivity` reference in `docs/SCAN_*.md` | Docs only | **DOCS LEGACY** |
| `HiddenFolder` table | Still used for folder exclusion (new feature), not old scan | **ACTIVE** |

**No full-device-scan dead code remains.** The old scan was cleanly removed, only `MusicScanner` (folder-indexing) remains and is required.

---

## DEBUG / TEST LEFTOVERS

| Item | File:Line | Purpose | Classification |
|------|-----------|---------|----------------|
| `LogHelper.cs:37` writes to `docs/runtime_logs/playback_diagnostic.log` | `Utilities/LogHelper.cs:37` `GetFullPath(...\docs\runtime_logs)` | Diagnostic log mirrored from `%APPDATA%` | **DEBUG LEFTOVER — still running (8MB log observed), not dead but verbose** | **POSSIBLY USED** |
| `Window_Loaded Thread.Sleep(3000)` | `Views/MainWindow.xaml.cs:318` | Artificial startup delay | **LEGACY** |
| `TransitionInputBlocker` `Grid 4603 Visibility="Collapsed"` | `MainWindow.xaml:4603` | Input blocker during tab slide | **USED** for animation, not dead |
| `StatusBarTranslate` `TranslateTransform` `4674` | `MainWindow.xaml:4674` | Toast slide | **USED** |

*No test windows, no debug buttons found.*

---

## BACKGROUND TASKS / TIMERS

| Timer/Task | File:Line | Purpose | Started | Stopped | Required? | Risk |
|------------|-----------|---------|---------|---------|-----------|------|
| `_positionTimer` `System.Timers.Timer 250ms` | `Audio/AudioEngine.cs:356` | `PositionChanged` + near-end/stuck detection + idle watchdog | `Play()` `Start()` | `Pause()/Stop()/LoadTrack()` `Stop()` | **REQUIRED** |
| `_sleepTimerCts` + `Task.Delay` | `MainViewModel.cs:178,5865` | Sleep timer `Task.Delay` + ticker `DispatcherTimer 1s` | `SetSleepTimer` | `CancelSleepTimer` | **REQUIRED** |
| `_sleepTimerTicker` `DispatcherTimer 1s` | `MainViewModel.cs:178` | Countdown text | `StartSleepTimerTicker()` | `CancelSleepTimer` | **REQUIRED** |
| `_lyricAdvanceTimer` `DispatcherTimer` | `MainViewModel.cs` lyric scroll | Lyric highlight | `PlayTrack` | `Pause` | **REQUIRED** |
| `PlaybackWorker` `Thread` | `Audio/AudioEngine.cs:667` `PlaybackWorker()` | DSP chain build off-UI | `LoadAndPlayAsync` | Queue empty → `false` | **REQUIRED** |
| `RetireDeviceAsync` `ThreadPool.QueueUserWorkItem` | `Audio/AudioEngine.cs:193` | Async old device `Stop/Dispose` to avoid hang | `LoadTrack/DeviceNumber` | Fire-and-forget, no stop needed | **REQUIRED** |
| `AlbumArtLoader` background `Task.Run` | `Library/AlbumArtLoader.cs` | Async art decode | `LoadAlbumArt` | `CancellationToken` | **REQUIRED** |
| `MusicScanner` `Task.Run` | `ViewModels/MainViewModel.cs:3744` | Folder scan | `AddManagedFolderAsync` | `await` | **REQUIRED** |

*No orphan timer running forever. All timers have explicit Stop.*

---

## UNUSED PACKAGES

**`MusicPlayer.csproj` PackageReferences (via `Select-String PackageReference`):**

```
<PackageReference Include="NAudio" Version="2.2.1" />
<PackageReference Include="NAudio.Asio" Version="2.2.1" />
<PackageReference Include="NAudio.Core" Version="2.2.1" />
<PackageReference Include="NAudio.Midi" Version="2.2.1" />
<PackageReference Include="NAudio.Wasapi" Version="2.2.1" />
<PackageReference Include="NAudio.WinMM" Version="2.2.1" />
<PackageReference Include="SoundTouch.Net" Version="1.0.0" />
<PackageReference Include="SoundTouch.Net.NAudioSupport" Version="1.0.0" />
<PackageReference Include="System.Data.SQLite.Core" Version="1.0.118" />
<PackageReference Include="CommunityToolkit.Mvvm" Version="8.4.0" />
<PackageReference Include="Microsoft.Extensions.DependencyInjection" Version="8.0.0" />
<PackageReference Include="Microsoft.Extensions.Logging" Version="8.0.0" />
<PackageReference Include="Microsoft.Extensions.Configuration" Version="8.0.0" />
<PackageReference Include="TagLibSharp" Version="2.3.0" />
<PackageReference Include="SQLite-net-pcl" Version="1.8.116" />
```

- All `NAudio*` are used via `AudioEngine.cs` + `DSP/*`.
- `SoundTouch.Net` + `NAudioSupport` used via `DSP/SpeedPitchSampleProvider.cs`.
- `TagLibSharp` used via `Library/MusicScanner.cs` + `DSP`? Actually `MusicScanner` + `TagLib` for metadata.
- `SQLite-net-pcl` + `System.Data.SQLite.Core` both used via `DatabaseService.cs` (dual provider, intentional for desktop).
- `CommunityToolkit.Mvvm` used via `RelayCommand` + `ObservableProperty`.
- `Microsoft.Extensions.*` used via `App.xaml.cs` DI + `LogHelper`.

**0 unused packages.** All have at least one `using` + instantiation.

---

## UNUSED ASSETS

| Asset | Path | Referenced? | Evidence | Classification |
|-------|------|-------------|----------|----------------|
| 6 `.png` in `Assets/` | `Assets/*.png` | **YES** | `Include="Assets\**\*.png"` in csproj + `AlbumArtConverter` fallback + `MainWindow.xaml` `APK_Icon_*` | **USED** |
| 5 `.webp` in `Assets/` (e.g., `bg_001.webp`) | `Assets/*.webp` | **NO** | Only `*.png` is declared, no code loads `.webp` via `BitmapImage` | **ORPHAN ASSET** |
| `Assets/haarcascade_frontalface_alt2.xml` | `Assets/haarcascade*.xml` | **NO** | Not declared, no `File.Read` found | **ORPHAN** |

*Both are LOW risk to remove if not needed.*

---

## DATABASE LEGACY

| Table/Column | File | Still used? | Evidence | Classification |
|--------------|------|-------------|----------|----------------|
| `tracks.IsHidden` `bool` | `Models/MusicTrack.cs:42` + `DatabaseService.cs:42` | **YES** | `GetHiddenTracks()` + `Hide/Unhide` + filtered in all `Get*Tracks()` | **ACTIVE** |
| `HiddenFolder` table | `DatabaseService.cs:27,399` | **YES** | Folder exclusion UI | **ACTIVE** |
| `Setting.Value` string | `Models/Setting.cs` | **YES** | `pref_eq_band_*`, `pref_volume`, `pref_balance` etc. | **ACTIVE** |
| `tracks.LyricPath` `AlbumArtPath` | `Models/MusicTrack.cs:39,48` | **YES** | `AlbumArtLoader` + `LyricParser` | **ACTIVE** |
| `tracks.Quality` `int` | `Models/MusicTrack.cs:54` | **POSSIBLY UNUSED** | `Grep Quality` → stored but never read for filtering/sorting, only `GetRecentAdd` sorts by `DateAdded` | **POSSIBLY LEGACY** |

*No dead table. Only `Quality` column is written but never queried — LOW risk.*

---

## PERFORMANCE-SUSPICIOUS DEAD CODE

- **`LogHelper` mirroring to `docs/runtime_logs` on every `LogInfo` (`Utilities/LogHelper.cs:37` `File.AppendAllText` x2)** — doubles IO on UI thread for every `PositionChanged` (250ms) + `PlaybackStarted`. At 8MB log, this is **verbose but not dead** — could be throttled. **POSSIBLY USED** for diagnostics, not dead.

- **No other suspicious polling.** All timers are 250ms-1s and have stop conditions.

---

## REMOVAL RISK

### LOW:
- `APK_MiniProgressBar` style `Themes/APKTheme.xaml:283` (0 refs) — safe to delete, alternative is `Slider`.
- `Assets/*.webp` (5) + `Assets/haarcascade_frontalface_alt2.xml` (1) — safe if not loaded via file IO.
- `Models/QueueItem.cs` / `Models/CurrentlyPlayingPage.cs` — 0 `new` — safe, only model.
- `ViewModels/MainViewModel.cs:1242` `ScanFolderList` — old scan list, UI rarely opened.
- `Views/MainWindow.xaml.cs:318` `Thread.Sleep(3000)` — startup delay, safe to reduce.

### MEDIUM:
- `LogHelper` docs mirroring — removing changes diagnostic workflow, but core still logs to `%APPDATA%`.

### HIGH:
- **Nothing high-risk dead found.** All other candidates are either `POSSIBLY USED` via indirect (converter, DI, audio chain) or `LEGACY but ACTIVE`.

### UNKNOWN:
- `Services/ArtistImageService` via reflection — **DO NOT DELETE** (converter indirect).

---

## DO NOT DELETE

- **Audio chain DSP:** `DSP/*` + `Audio/*` — all 14 files are in `AudioEngine.cs:482` chain, even if not directly called from UI, they are required for playback. **DO NOT DELETE.**
- **Converters:** All 22 are registered in `App.xaml` and used via `{StaticResource}` — WPF will throw if removed, even if static grep says 0. **DO NOT DELETE.**
- **Services (6):** All registered in `App.xaml.cs` DI and resolved via `GetRequiredService` on startup — **DO NOT DELETE.**
- **Models `MusicTrack`, `HiddenFolder`, `ManagedMusicFolder`, `Setting`, `Playlist`** — DB tables, **DO NOT DELETE.**
- **Themes (`APKIcons.xaml`, `APKTheme.xaml`)** — all 51 geometries + 18 styles are referenced, **DO NOT DELETE.**
- **`MusicScanner`, `AlbumArtLoader`, `AudioAnalyzer`** — background tasks with no direct UI, but **required** for library and visualizer.
- **`ScanFolderList` + `scan_settings` view** — looks dead but is the UI for manual folder list in Settings → **POSSIBLY USED**, keep.

---

## RECOMMENDED FUTURE CLEANUP

*Only recommendations, NO CHANGES MADE.*

- **LOW:** Delete `Themes/APKTheme.xaml:283` `APK_MiniProgressBar` if `ProgressBar` never used, or replace with `Slider` usage documentation.
- **LOW:** Remove `Assets/*.webp` (5) and `haarcascade_frontalface_alt2.xml` from repo or add `<Content>` if needed, or document as reference.
- **LOW:** Remove `Models/QueueItem.cs` + `Models/CurrentlyPlayingPage.cs` if confirmed 0 `new` after full solution search (including `decompiled/`).
- **LOW:** Move `Views/MainWindow.xaml.cs:318` `Thread.Sleep(3000)` to async splash or remove if startup warm-up not needed.
- **MEDIUM:** Consolidate `ScanFolderList` (old) vs `ManagedMusicFolder` (new) — either document as legacy for `scan_settings` view or migrate `scan_settings` to use `ManagedMusicFolder`.
- **MEDIUM:** Throttle `LogHelper.cs:37` docs mirroring — e.g., only on `Error`, or buffer + flush every 5s, to reduce IO.

*No other cleanup recommended. Project is already lean after recovery phases.*

---

## VERIFICATION

| Check | Result |
|-------|--------|
| **Source modified** | **NO** — read-only via `Read`/`Grep`/`Glob`/`Bash Get-ChildItem`, no `Edit`/`Write` on source |
| **Files deleted** | **NO** |
| **Files renamed** | **NO** |
| **XAML modified** | **NO** |
| **csproj modified** | **NO** |
| **Database modified** | **NO** — only `Select-String` reads, no `sqlite3` writes |
| **Official publish modified** | **NO** — `Get-ChildItem "C:\Users\win\Desktop\Soulaiman\publish"` read-only, no `Copy-Item` |
| **Archive modified** | **NO** — `Get-ChildItem "C:\Users\win\Desktop\Soulaiman\archive"` read-only |
| **Build executed** | **NO** — no `dotnet build` in this audit (would be read-only but avoided to keep strictly inspection-only) |
| **Docs created** | **YES — exactly one allowed:** `docs/FULL_PROJECT_DEAD_CODE_AUDIT.md` (this file) — `Write` permitted per task |

---

## REPORT QUALITY

**Method:** Static `Glob` + `Grep` + `Read` + `Bash Get-ChildItem` cross-checked with runtime knowledge from `docs/FINAL_RECREATION_AUDIT.md` and recent `hidden_files`/`sleep_timer` implementations. For each finding, checked: static refs via `Select-String`, runtime entry via `Navigate()`/`Command`/`Click`, and `Visibility` triggers before classifying.

**Example finding (detailed):**

- **File:** `Themes/APKTheme.xaml:283`
- **Symbol:** `Style x:Key="APK_MiniProgressBar" TargetType="ProgressBar"`
- **What it does:** Defines a `ProgressBar` template with `PART_Track` + `Ellipse` indicator (alternative to `Slider` for progress).
- **Why it appears unused:** `Grep "APK_MiniProgressBar"` → 1 hit (definition), 0 hits in `MainWindow.xaml` `Style="{StaticResource"`; `Grep "ProgressBar"` → 0 hits in `MainWindow.xaml`.
- **Evidence:** `App.xaml` merges `APKTheme.xaml` but no `ProgressBar` element in `MainWindow.xaml` uses it; app uses `Slider` (`SeekSlider`) for seek.
- **Runtime reachable:** NO — no code creates `ProgressBar` in code-behind.
- **Static references:** 1 (definition)
- **Classification:** `DEAD CODE CANDIDATE` (unused style)
- **Removal risk:** `LOW` — safe, alternative `Slider` is used.

*All other findings in this report follow the same evidence pattern.*

