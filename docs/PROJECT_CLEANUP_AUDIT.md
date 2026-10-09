# PROJECT CLEANUP AUDIT — 2026-09-01
Scope: src/MusicPlayer (excl. bin/obj/publish, excl. analysis/, excl. decompiled/)

## Inventory

| Path | Type | Used? | Referenced by | Duplicate? | Can merge? | Can delete? | Reason | Action |
|------|------|-------|---------------|------------|------------|-------------|--------|--------|
| src/MusicPlayer/Themes/DarkTheme.xaml | ResourceDictionary | **No** | None (App.xaml merges APKTheme+APKIcons only) | Yes — old prototype from reset | No | **Yes → Archive** | Replaced by APKTheme.xaml which is APK-accurate (#FF1D2026/#FF4995F3). DarkTheme kept only in archive. | Move to archive/old_implementation/Themes_DarkTheme.xaml |
| src/MusicPlayer/ViewModels/MiniPlayerViewModel.cs | ViewModel | **No (dead)** | DI registered in App.xaml.cs:60 but never injected, DataContext is MainViewModel | Yes — duplicates MainViewModel mini-player state (IsPlaying/CurrentTrack/Position/Queue) | No | **Yes → Archive** | Single source of truth is MainViewModel + AudioEngine. MiniPlayerViewModel adds second queue state unsynced. | Remove DI registration + archive file |
| src/MusicPlayer/Services/NavigationService.cs | Service | **Partial — redundant** | Injected in MainViewModel but MainViewModel uses own _viewHistory Stack<string> for CurrentView navigation; _nav.NavigateTo never called except via CloseTopmost? | Yes — duplicate navigation state (NavigationService._backStack vs MainViewModel._viewHistory) | No | **Keep but deprecate → single source** | Make NavigationService canonical or remove. Decision: keep NavigationService as thin wrapper around MainViewModel history? Actually MainViewModel history is source of truth for UI, NavigationService is dead code for now — keep file but mark deprecated; do not delete yet, will consolidate in Phase 2. | No delete now — document |
| src/MusicPlayer/Converters/Converters.cs | Converters (9 converters in 1 file) | **Yes** | App.xaml merges 7 converters; MainWindow.xaml uses DurationConverter, BoolToVisibility, AlbumArtConverter, StringNullOrEmpty, EqualityToVisibility, ZeroToVisibility, PlayModeToVisibility, IntEqualityToBrush, BoolToBrush | No — canonical merged file (good) | Already merged | No | Logical organization — keep | Keep |
| src/MusicPlayer/Utilities/LogHelper.cs | Helper | **Low use** | Only AppPaths? Check: used nowhere? Grep shows no reference | Potentially dead | No | No | Keep — may be used for future diagnostics, lightweight | Keep |
| src/MusicPlayer/Utilities/AppPaths.cs | Helper | **Yes** | App.xaml.cs, DatabaseService, Library | No | No | No | Single source for %AppData%/MusicPlayer paths | Keep |
| src/MusicPlayer/Library/AlbumArtLoader.cs | Service | **Yes** | MainViewModel (via DI), Converters/AlbumArtConverter delegates to it | No | No | No | Distinct from converter | Keep |
| src/MusicPlayer/DSP/* (7 files) | DSP | **Yes** | AudioEngine → EqualizerSampleProvider→SoundEffectProcessor→SpeedPitchSampleProvider → Biquad/EqBand/EqPreset/SoundEffect | No | No | No | Each has single responsibility | Keep |
| src/MusicPlayer/Audio/EqualizerSampleProvider.cs | Provider | **Yes** | AudioEngine | No | No | No | 10-band + Bass/Virtualizer | Keep |
| src/MusicPlayer/Audio/AudioEngine.cs | Engine | **Yes** | PlaybackService, MainViewModel | No | No | No | Single audio source | Keep |
| src/MusicPlayer/Database/DatabaseService.cs | DB | **Yes** | All ViewModels/Services | No | No | No | Single DB source | Keep |
| src/MusicPlayer/Services/PlaybackService.cs | Service | **Yes** | MainViewModel, AudioEngine wrapper | No | No | No | Facade over AudioEngine + DB play count | Keep |
| src/MusicPlayer/Services/SettingsService.cs | Service | **Yes** | MainViewModel, App | No | No | No | Single settings source (32 keys) | Keep |
| src/MusicPlayer/Models/* (8) | Models | **Yes** | DB, ViewModels | No | No | No | Keep |
| src/MusicPlayer/Views/NewPlaylistDialog.cs | Dialog | **Yes** | MainWindow.xaml.cs ShowNewPlaylistDialog | No | No | No | Keep |
| src/MusicPlayer/Assets/** (11) | Resource | **Yes** | APKTheme background + MainWindow Image Source | No | No | No | Keep |
| src/MusicPlayer/MusicPlayer.csproj | Project | **Yes** | — | No | No | Clean up (remove duplicate includes) | Has implicit includes via SDK, explicit Resource Include for Assets is redundant but harmless — keep | Keep (or consolidate explicit) |
| decompiled/*, tools/*, archive/* | Analysis/Tools | **No (reference only)** | — | No | No | No | Keep for knowledge base | Keep |
| publish/*, bin/*, obj/* | Artifacts | No | — | — | — | — | Build output — not source | Keep publish (final exe), ignore bin/obj |

## Duplicate Classes Found
- **MiniPlayerViewModel vs MainViewModel mini state**: duplicate IsPlaying/CurrentTrack/Position. Resolution: keep MainViewModel as canonical, archive MiniPlayerViewModel.
- **NavigationService vs MainViewModel._viewHistory**: duplicate navigation stack. Resolution: keep MainViewModel as canonical UI source for now, NavigationService stays registered but unused — document as deprecated, consolidate later (needs refactor of CurrentView binding to service).
- **DarkTheme vs APKTheme**: duplicate theme. Resolution: archive DarkTheme.

## Duplicate XAML Resources
- **APK_AccentButton** was missing earlier (error at hidden_folders panel) — added canonical style in APKTheme.xaml:133. No duplicate, was missing. Now canonical.
- **Padding on StackPanel** (8 occurrences at lines 813,820 etc) — invalid property, fixed to Margin. Not duplicate, bug.
- No unused DataTemplates found that are unreferenced; all panels referenced via CurrentView EqualityToVisibility.

## Packages
- CommunityToolkit.Mvvm 8.4.2 — used (ObservableObject, RelayCommand)
- Microsoft.Extensions.DI/Logging 10.0.11 — used (App.xaml.cs ServiceCollection)
- NAudio 2.2.1 — used (AudioEngine)
- sqlite-net-pcl 1.11.285 + SQLitePCLRaw.bundle_green 2.1.11 — used (DatabaseService)
- TagLibSharp 2.3.0 — used (MusicScanner, EditTags, AlbumSave)

All packages are used — none to remove. No duplicate PackageReferences.

## Dead Code / Empty Handlers Scan
- Grep for `NotImplementedException`, `TODO`, `FIXME`, `Coming soon`, `Placeholder` — 0 hits in src (except SpeedPitch comments). 
- Grep for empty handlers `=> {}` — none.
- All 38 Activities now have real handlers (verified: AddHiddenFolderDialog via OpenFolderDialog, etc.) — no dead controls.

## Archival Actions Planned
1. Move `Themes/DarkTheme.xaml` → `archive/old_implementation/DarkTheme.xaml`
2. Move `ViewModels/MiniPlayerViewModel.cs` → `archive/old_implementation/MiniPlayerViewModel.cs` + remove DI registration
3. Keep NavigationService but annotate deprecated
4. Clean `MusicPlayer.csproj` explicit Resource Include if redundant (keep for clarity, not removed)

## Risk Assessment
- **DarkTheme removal**: Low risk — not referenced. Build verification confirms.
- **MiniPlayerViewModel removal**: Low risk — DI only, no XAML binding. Remove registration + file, build verifies.
- **NavigationService consolidation**: **Risky** — MainViewModel history is UI-bound, NavigationService is not wired. Removing service entirely could break future DI expectations. Action: keep file, document duplicate, defer full consolidation to next structural batch.
- **Converter merge**: Already optimal — no change.
- **XAML Padding→Margin**: Low risk — visual layout unchanged, Build verifies.

## Next Steps (Actual Cleanup Execution)
1. Archive DarkTheme
2. Archive MiniPlayerViewModel + remove DI
3. Fix csproj if needed (no packages removed)
4. Build → Run → Smoke test (startup, library, playback, mini/full, lyrics, EQ, queue, navigation)
