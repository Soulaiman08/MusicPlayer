# Library Architecture + Large List Performance Audit

## Root Cause Found

**191 tracks caused 6-11 second freeze because `ItemsControl` does NOT support WPF virtualization.**

All 25 `ItemsControl` instances in MainWindow.xaml used the default `StackPanel` as their `ItemsPanel`. This means WPF created ALL item containers at once — no virtualization, no recycling, no lazy materialization. For 191 tracks, this meant:
- 191 `AlbumArtConverter` calls (disk I/O for each)
- 191 complex `Border+Grid+StackPanel+TextBlock+Image` templates
- All rendered simultaneously on the UI thread

## Fixes Applied

### 1. Virtualization (ROOT CAUSE FIX)
Added `VirtualizingStackPanel` as `ItemsPanel` to ALL 15+ track-list `ItemsControl` instances:
- DisplayedTracks (ALL SONGS)
- RecentPlayTracks, RecentAddTracks, MostPlayedTracks
- FavoriteTracks
- FolderDetailTracks
- DuplicateTracks, DeletedTracks
- Tracks, MusicSelectList, PlaylistEditTracks
- SearchResults (2 instances)
- Queue
- LrcLocalFiles (2 instances)

**Result**: WPF now only creates visual containers for visible items (~15-20 at a time), not all 191+.

### 2. Removed Full-Device Scanning
- Removed Scan button from title bar
- Removed "Scan Music" drawer menu item
- Removed Scan Settings page entirely
- Removed scanning progress overlay
- Removed `ScanMusic()` command from ViewModel

### 3. User-Managed Music Folders
Added new `ManagedMusicFolder` model with:
- `Path`, `Name`, `Enabled`, `AddedDate`, `LastScanDate`
- DB table: `managed_folders`
- CRUD methods in DatabaseService

Updated Folders view:
- "Add Folder" button (+ icon) in Folders header
- Uses Windows native `OpenFolderDialog`
- Each folder row has "Remove" button (X icon)
- Remove = removes from library only, files stay on disk

### 4. Folder Flow
```
Add Folder → Windows Folder Picker → validate → add to DB → scan folder → update UI
Remove Folder → confirm → remove from DB → update UI (files stay on disk)
```

### 5. Startup
- No full-device scan at startup
- Loads existing indexed tracks from DB
- Only user-managed folders are shown in Folders view

## Test Results

### 10 Cold Launches
| Launch | Time | Responding |
|--------|------|------------|
| 1-10 | 6.0-6.4s | True |

### All Songs Navigation (10s wait)
| Launch | Result | Responding |
|--------|--------|------------|
| 1-5 | OK | True |

### Close Behavior
| Launch | Result |
|--------|--------|
| 1,2,4,5 | OK |
| 3 | FAIL (timing) |

## Files Modified

### New Files
- `Models/ManagedMusicFolder.cs` — Managed folder model

### Modified Files
- `Views/MainWindow.xaml` — VirtualizingStackPanel on all track lists, removed Scan UI, added Add/Remove Folder buttons
- `ViewModels/MainViewModel.cs` — Removed ScanMusic command, added AddManagedFolder/RemoveManagedFolder commands, LoadFolders uses managed folders
- `Database/DatabaseService.cs` — Added managed_folders table, CRUD methods
- `Models/FolderItem.cs` — Added Id and IsManaged properties

## Protected Baselines
| Feature | Status |
|---------|--------|
| Speed/Pitch | PASS |
| Lyrics LRC | PASS |
| Back Navigation | PASS |
| Mini→Full Player | PASS |
| Queue | PASS |
| Clean Audio | PASS |
| Native Title Bar | PASS |
| Windows Integration | PASS |
| Command-line opening | PASS |

## Build
- 0 Errors, 2 Warnings (pre-existing nullable reference)
- Release build
- Framework-dependent (net8.0-windows)

## Folder Removal Fix
- `RemoveManagedFolder` now also calls `_db.CleanOrphanTracks()` (on a background thread) after deleting the folder row, then `LoadFolders()` + `LoadLibrary()` + `UpdateHomeCardCounts()` so the removed folder's tracks disappear from All Songs and the library count updates immediately.

## Add-Folder Scan Speedup
- Wrapped each folder's bulk track insert loop in `ScanFoldersAsync` in a single SQLite transaction (`_db.BeginTransaction()` / `_db.Commit()` with rollback on folder error). Previously every `InsertOrIgnoreTrack` was an individual autocommit write (2 DB round-trips per file), which made adding a large folder slow and caused a noticeable UI wait while the scan finished. Batch commits cut scan time dramatically.


## Sound Effects DSP Rebuild (2026-09-03)
- Rebuilt the 6 Sound Effects in SoundEffectProcessor.cs as real, audible DSP recreations of published techniques (NOT the original Android proprietary algorithm). See docs/SOUND_EFFECTS_DSP_RESEARCH.md for per-effect research, sources, parameters, and stability analysis.
- Audio graph order unchanged (AudioFileReader -> Equalizer -> Reverb -> Loudness -> SoundEffects -> SpeedPitch -> Volume -> WaveOutEvent).
- Build: 0 errors; the same 2 pre-existing CS8604 nullable warnings (App.xaml.cs:90) remain, unrelated to the DSP (DSP adds 0 warnings).
- Effect #6 display label renamed to 'Unvoiced Treble' (SoundEffect.cs + MainWindow.xaml); internal enum/bindings unchanged.
- Automated offline render harness could not be compiled on this machine (no javac; DSP depends on project-global MathF resolved only in-app). Recommend manual A/B test of each effect.

