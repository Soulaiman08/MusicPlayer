# WinUI 3 Feasibility & Migration Evaluation (`MusicPlayer`)

## Executive Summary
To evaluate whether migrating from WPF (D3D9Ex) to WinUI 3 / Windows App SDK (DirectComposition + D3D11/D3D12) eliminates live-resize (`WM_SIZING`) jitter without destabilizing the production app, we executed a non-destructive 3-phase spike:

1. **Extracted `src/MusicPlayer.Core`** (`net8.0-windows10.0.19041.0`, zero `System.Windows.*` dependencies) containing the audio engine, DSP pipeline, SQLite database layer, library scanner, lyrics engine, models, and platform-agnostic artwork resolution.
2. **Kept `src/MusicPlayer` (WPF)** 100% buildable (`0` errors) and regression-free (`267/271` tests passing, identical to pre-spike baseline).
3. **Built `src/MusicPlayer.WinUI.Prototype`** (unpackaged WinUI 3 / Windows App SDK `1.8.260921001`) referencing `MusicPlayer.Core`, featuring `AppWindow.TitleBar` (`ExtendsContentIntoTitleBar = true`) with transparent caption buttons (`-`, `□`, `×`), Mica/Acrylic backdrop switching, a left navigation rail, responsive `Your library` glass cards bound to real `DatabaseService` counts, and a working bottom player bar bound to `AudioEngine` and `PlaybackService`.

---

## 1. Shared (`MusicPlayer.Core`) vs. WPF-Specific Inventory

### Successfully Shared in `src/MusicPlayer.Core`
| Category | Components Moved | Notes |
|---|---|---|
| **Audio & DSP** | `AudioEngine`, `AudioAnalyzer`, `AudioAnalysisCaptureProvider`, `EqualizerSampleProvider`, `EqBand`, `EqPreset`, `SoundEffect`, `SoundEffectProcessor`, `BiquadFilter`, `AllPassFilter`, `CombFilter`, `LoudnessSampleProvider`, `ReverbSampleProvider`, `SpeedPitchSampleProvider` | Zero WPF dependencies; `NAudio` + `SoundTouch.Net` work identically in WPF and WinUI 3. |
| **Database & Scanner** | `DatabaseService`, `DatabaseRows`, `MusicScanner` | `sqlite-net-pcl` + `TagLibSharp`; thread-safe serialized access. |
| **Core Services** | `PlaybackService`, `SettingsService` | `INotifyPropertyChanged` + `DatabaseService` persistence. |
| **Lyrics** | `LyricLine`, `LyricParser`, `LyricsFileService` | Pure C# LRC parsing and file lookup. |
| **Models** | `MusicTrack`, `Playlist`, `QueueItem`, `FolderItem`, `ManagedMusicFolder`, `HiddenFolder`, `HomeCard`, `AlbumArtRecord`, `LyricBinding`, `Setting`, `PlayMode`, `LibrarySortOption`, `CurrentlyPlayingPage` | All non-WPF models shared cleanly. |
| **ViewModels (Base)** | `ViewModelBase`, `PlaylistViewModel` | Pure `ObservableObject` / POCO wrappers. |
| **Image Pipeline Core** | `ImageDiskCache`, `ImageLoadScheduler`, `ImageDecodeMetrics`, `ImageLoadingEnums`, `SingleFlightCoordinator`, `SourceIdentity`, `IAlbumArtPathResolver` / `AlbumArtPathResolver` | New `IAlbumArtPathResolver` returns `ArtworkData(CachedFilePath, RawBytes)` without `BitmapImage` / `BitmapSource`. |
| **Utilities** | `AppPaths`, `LogHelper`, `PathContainment`, `BackgroundTaskRegistry`, `BulkObservableCollection`, `TabLoadingStateTracker`, `UiOperationTracker` | Pure .NET utilities. |

### Remained WPF-Specific (`src/MusicPlayer`)
| Component | Why It Remained in WPF | Migration Requirement |
|---|---|---|
| `MainViewModel.cs` (~9,200 lines) | Coupled to `DispatcherTimer`, `Application.Current.Dispatcher`, `BitmapSource`, `WallpaperBlurEngine`, and `Application.Current.Shutdown()`. | Split into UI-agnostic state/commands in `Core` + `IDispatcherService` / `ITimerService` abstraction, or rewrite shell VM against `DispatcherQueue`. |
| `AlbumArtLoader`, `OriginalAlbumArtAsset`, `ImageDecoder`, `ImageMemoryCache`, `AlbumArtProvider`, `ArtistImageProvider`, `ImageLoadingCoordinator` | Coupled to `System.Windows.Media.Imaging.BitmapSource` / `BitmapImage` and WPF `Dispatcher`. | Replace `BitmapSource` with `Microsoft.UI.Xaml.Media.Imaging.BitmapImage` / `SoftwareBitmapSource` or Win2D `CanvasBitmap` backed by `IAlbumArtPathResolver`. |
| `WallpaperBlurEngine`, `WallpaperDecodeCache`, `WallpaperAlphaCompositor`, `BlurResultCache`, `PackResourceLoader`, `WallpaperCatalog`, `WallpaperItem` | Uses WPF `WriteableBitmap`, `RenderTargetBitmap`, `BlurEffect`, and `pack://application:,,,/` URIs. | Replace with WinUI `MicaBackdrop` / `DesktopAcrylicBackdrop` or Win2D `GaussianBlurEffect` + `ms-appx:///` assets. |
| `SystemTrayService`, `WindowsIntegrationService`, `FaceCropHelper` | Uses Win32 `WindowInteropHelper` / WPF `Rect` / `BitmapSource`. | Port `Hwnd` retrieval to `WinRT.Interop.WindowNative.GetWindowHandle(window)` and `AppWindow`. |
| `Views/*`, `Themes/*`, `Converters/*`, `Diagnostics/*` | WPF XAML (`MainWindow.xaml`, `HomeContent.xaml`, `TrackTable`, `VirtualizingBalancedPanel`, `SmoothScrollHelper`, `AudioVisualizerControl`). | Full WinUI 3 XAML + Composition rewrite (see Section 3). |

---

## 2. Side-by-Side Live Resize Benchmark Instructions

Both executables can be launched **simultaneously** on this machine (note: both read the same `%AppData%\MusicPlayer\music.db` SQLite database; for an isolated synthetic library, set `MUSICPLAYER_DATA_DIR` before launching).

### Launch Commands (PowerShell from Repo Root)
```powershell
# 1. Launch WPF Baseline
Start-Process ".\src\MusicPlayer\bin\Release\net8.0-windows10.0.19041.0\win-x64\MusicPlayer.exe"

# 2. Launch WinUI 3 Prototype
Start-Process ".\src\MusicPlayer.WinUI.Prototype\bin\x64\Release\net8.0-windows10.0.19041.0\win-x64\MusicPlayer.WinUI.Prototype.exe"
```

### What to Test & Measure (`WM_SIZING`)
1. **Slow Edge Resize**: Grab the right or bottom-right corner of each window and drag slowly (~50–100 px/sec) for 5 seconds.
   - **WPF (`MusicPlayer.exe`)**: Observe D3D9Ex back-buffer reallocation / titlebar-to-client-area desync and frame drops on the glass cards and right-edge border.
   - **WinUI 3 (`MusicPlayer.WinUI.Prototype.exe`)**: Observe DirectComposition swap-chain synchronization, `AppWindow.TitleBar` caption button alignment, and 3→2→1 column card reflow (`LibraryCardsGrid_SizeChanged`).
2. **Fast / Aggressive Edge Resize**: Rapidly oscillate the window width between ~600px and ~1500px for 5 seconds.
   - Watch for black/white gutter flashes, clipped borders, or input lag.
3. **Mica vs. Acrylic Backdrop Cost**: Click the top-right **Mica / Acrylic** toggle button in `MusicPlayer.WinUI.Prototype` and repeat the fast resize to compare DWM composition cost.
4. **GPU & RAM Usage (Task Manager / Process Explorer)**:
   - Open Task Manager → **Details** / **Performance** → enable **GPU** and **Memory (Private Working Set)** columns.
   - Compare `MusicPlayer.exe` vs. `MusicPlayer.WinUI.Prototype.exe` at idle, during continuous `WM_SIZING` resize, and during audio playback.

---

## 3. Full WinUI 3 Migration Effort, Control Mapping & Risks

### XAML & Custom Control Mapping
| WPF Control / Feature | WinUI 3 Equivalent | Complexity | Notes |
|---|---|---|---|
| `MainWindow.xaml` Custom Chrome (`WindowChrome`) | `AppWindow.TitleBar` (`ExtendsContentIntoTitleBar = true`) + `SetTitleBar` | **Low** | Proven in prototype; native Windows 11 Snap Layouts & transparent caption buttons work out of the box. |
| `HomeContent.xaml` Glass Cards | `Border` + `AcrylicBrush` / translucent fill + `ThemeShadow` / `DropShadow` | **Low–Med** | WinUI 3 `AcrylicBrush` and `MicaBackdrop` give native DWM blur without WPF `BlurEffect` cost. |
| `VirtualizingBalancedPanel` (`Panel` subclass) | `ItemsRepeater` + custom `VirtualizingLayout` (or `UniformGridLayout`) | **High** | WinUI 3 `VirtualizingLayout` uses `MeasureOverride`/`ArrangeOverride` with `LayoutContext.GetOrCreateElementAt`; cleanest direct port of balanced-row virtualization. |
| `SmoothScrollHelper` | WinUI `ScrollViewer` (built-in DirectManipulation + Composition inertia) | **Low** | WinUI `ScrollViewer` runs wheel/touch/inertia on the compositor thread natively—custom WPF smooth-scroll timer hacks are unnecessary. |
| `TrackTable` + `TrackTableHeader` (resizable columns) | `ItemsRepeater` / `ListView` + `CommunityToolkit.WinUI.UI.Controls.DataGrid` or custom `Grid` + `GridSplitter` | **High** | Needs careful virtualization tuning (`RecyclingElementFactory`) to match 5,000+ track scroll performance. |
| `AudioVisualizerControl` | Win2D (`Microsoft.Graphics.Win2D`) `CanvasAnimatedControl` or Composition `SpriteVisual`s | **Medium** | Hardware-accelerated 60/120fps rendering on D2D/D3D11 instead of WPF `DrawingContext`. |
| `NavigationChoreography` (Hero transitions) | `ConnectedAnimationService` + `ImplicitAnimations` (Composition) | **Medium** | WinUI 3 has built-in `ConnectedAnimationService.GetForCurrentView()` for artist/album hero morphs. |
| `Themes/APKTheme.xaml`, `Converters.cs` | WinUI 3 `ResourceDictionary` (`ThemeDictionaries`), `{x:Bind}` functions, `IValueConverter` (`Microsoft.UI.Xaml.Data`) | **Medium** | `{x:Bind}` eliminates reflection overhead; `MultiBinding` / `DataTrigger` must move to `VisualStateManager` or `{x:Bind}` helpers. |

### Estimated Effort (Full Production Migration)
- **Phase A — Decouple `MainViewModel` & Image Pipeline**: ~1.5–2 weeks (extract `DispatcherQueue` / timer abstractions, port `AlbumArtProvider` / `ArtistImageProvider` to `IAlbumArtPathResolver` + `BitmapImage` / Win2D).
- **Phase B — Core Shell, Navigation, Home, & Player Bars**: ~1–1.5 weeks (titlebar, sidebar rail, `HomeContent`, MiniPlayer, FullPlayer, Queue drawer, `ConnectedAnimationService`).
- **Phase C — Virtualized Library Views (`TrackTable`, `VirtualizingBalancedPanel`, Playlists, Folders, Settings, Equalizer)**: ~2–3 weeks.
- **Phase D — Win32 Interop (`SystemTrayService`, Media Keys, SMTC) & Perf Parity Suite**: ~1 week.
- **Total Estimate**: **~5.5–7.5 engineering weeks** for full feature & performance parity.

### Key Migration Risks
1. **Unpackaged Deployment (`WindowsPackageType=None`, `WindowsAppSDKSelfContained=true`)**: Increases published binary footprint (~100+ MB self-contained WinAppSDK runtime) vs. framework-dependent WPF, unless packaged as MSIX.
2. **WinUI 3 `ItemsRepeater` / `ListView` Virtualization Quirks**: High-density tables (5,000+ rows with inline artwork) require `RecyclingElementFactory` and strict `x:Phase` / async image cancellation to avoid scroll stutter.
3. **No `MultiBinding` or `DataTrigger` in WinUI 3 XAML**: Requires converting WPF `DataTrigger`s in `MainWindow.xaml` / `APKTheme.xaml` to `VisualStateManager` states or `{x:Bind}` computed properties.
4. **`WallpaperBlurEngine` Replacement**: WPF `BlurEffect` / `WriteableBitmap` pipelines must be replaced with Win2D `GaussianBlurEffect` or `CompositionEffectBrush` (`Microsoft.UI.Composition`).
