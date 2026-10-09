# Playback Crash Investigation — Root Cause & Fix

Date: 2026-09-03 · Scope: `src/MusicPlayer` (development only)

## Summary

After the Sound-Effects DSP rewrite, clicking a track started playback then the app
**froze and closed** with no visible error. Investigation showed the DSP math itself
is bounds-/NaN-safe, but two **framework-level defects were hiding and amplifying the
real problem**:

1. `App.xaml.cs` installed three exception handlers that **silently swallowed every
   unhandled exception** (`args.Handled = true;` and empty bodies). Any exception on
   the audio thread produced a silent freeze with **no log, no stack, no message** —
   exactly the "Application crashed" with no detail the user reported.
2. `App.xaml.cs` also started a **ShutdownWatchdog thread** that, ~15 s after launch,
   force-killed the process whenever `Main(...).Responding` was `false`. This turned
   any UI/audio thread stall into a hard, unexplained close.

Together these mean: an error occurred → it was hidden → the app appeared frozen →
the watchdog killed it → "crash with no exception." The real fault was therefore
un-diagnosable, which violates the requirement to capture the true exception.

## What was changed (real fixes, NOT disabling sound effects)

| File | Fix |
|------|-----|
| `App.xaml.cs` | Replaced the three swallowing exception handlers with **logging** handlers that record exception type + message + stack via `LogHelper.LogError` (also appended to `logs\error_*.log` at `%APPDATA%\MusicPlayer\logs`). |
| `App.xaml.cs` | **Removed the ShutdownWatchdog auto-kill** thread (it was masking real bugs by force-closing a stalled app). |
| `Audio/EqualizerSampleProvider.cs` | **Hardened the real-time `Read` loop** so the stereo Virtualizer and Balance stages process only whole frames within `read` and can never index `buffer[offset+i+1]` past the `read`-valid region (a real latent out-of-bounds on a short/odd read). |
| `DSP/SoundEffectProcessor.cs` | **Fade ramp now uses `read` (the samples actually written) instead of `count`**, with a frame-bound guard, so a short/atomic read can never touch uninitialized/tail samples or go out of bounds during an effect-transition fade. |
| `Audio/AudioEngine.cs` | **Moved all track-start builder work off the UI thread.** Track load/play (`LoadTrack` + `Play`) now runs on a **single serialized background worker** fed by `LoadAndPlayAsync`, so the heavy DSP graph build (file probe + SoundTouch + effects + output init) no longer blocks the WPF UI thread and the startup "not responding then recover" freeze is gone. Engine events (`PlaybackStarted` / `PositionChanged` / `TrackChanged`) were already marshalled to the UI thread by the ViewModel (`MainViewModel.WireAudioEvents` → `Dispatcher.BeginInvoke`), so firing them from the worker is safe. Rapid Next/Previous/Pause re-queue into the same worker (generation/lock guarded) — no lost updates, no overlap. |
| `Services/PlaybackService.cs`, `ViewModels/MainViewModel.cs` | Call sites switched from synchronous `LoadTrack(); Play();` to `LoadAndPlayAsync(track)`. |

The earlier failed attempt (a `Task.Run(...).then(...)` future-based refactor breaking on a
nested/static result class whose `void` was then assigned to `_`) was reverted (build broke
under the UniLib transpiler). This final design avoids a result holder class entirely and
therefore survives the transpiler cleanly.

### Follow-up: first-click freeze after fresh launch (SoundTouch lazy-building)
After the above, playback no longer crashed, but the **first** track-click right after
launching the app still froze the UI briefly (same symptom as the All Songs list). The
heavy one-time cost was the **native SoundTouch processor**, which the DSP chain constructed
on **every** track load even when Speed/Pitch were neutral (speed 1 / pitch 0). On a weak
CPU the very first construction also pays for lazy OS DLL load:

- `DSP/SpeedPitchSampleProvider.cs` — `SoundTouchProcessor` is now **created lazily on first
  real need** (first time `Speed` or `Pitch` deviates from neutral), guarded by the existing
  `_sync` lock and idempotent. On the default path `Read` passes audio straight through and
  the heavy native processor is **never built at click time**, so it is no longer a
  per-track or first-click cost. `Speed`/`Pitch` setters create it (on the UI thread) only
  when the user actually changes speed/pitch — an intentional live action.

Suspect remaining on the **UI thread** (not yet changed — flagged for confirmation): playing
from a large list (e.g. All Songs) calls `Queue.ReplaceAll(engine.Queue)` synchronously in
`MainViewModel.PlayTrack`/`PlaybackStarted`, which re-materials the whole list into the bound
queue UI list; for a big library this is the same churn as the All Songs list. This is a
candidate for a targeted, test-needing fix (e.g. only refresh the queue UI when the queue
view is actually visible).

### Follow-up 2: first-click UI-thread freeze + diagnostic + queue fix
The user confirmed the first-click freeze persisted after the worker + lazy SoundTouch, so
the freeze is on the **UI thread**, not the audio engine. Two changes were made:

- `ViewModels/MainViewModel.cs` — **`RefreshQueue()` now skips the O(N) `Queue.ReplaceAll`
  (plus the WPF `Reset`/re-layout) when the bound queue already matches `Engine.Queue`
  (same size and track ID order via `SameTrackOrder`). Behavior-identical when unchanged;
  removes the redundant big-list rebuild that fires on every playback start.
- Added **self-diagnostic timing** (second-resolution, confirmed `DateTimeOffset.UtcNow`
  API) via `LogHelper.LogInfo` in `MainViewModel.PlayTrack` (context/find/playList/ui-meta)
  and `AudioEngine.PlaybackWorker` (load+play). On the next run the console (`LogInfo`) will
  show which segment of the first-click path is slow, so a precise fix can be applied
  without guessing. If the freeze is sub-second and no segment shows a 1s+ jump, the freeze
  is in the WPF binding re-render triggered by the observable property changes themselves,
  and we would then need a finer (ms) probe.

Sound effects themselves were **not disabled, not bypassed**; NONE remains a true
bypass and all 6 effects are still active.

## Root-cause conclusion (evidence-based)

- **Provable math defects in the DSP:** none found that crash. All biquad coefficient
  sets are stable for the used freq/Q/gain; every effect indexes strictly inside
  `[offset, offset+read)`; `tanh` and `x/(1+|x|)` saturators cannot produce NaN/Inf;
  mono inputs (`_channels < 2`) are guarded by early returns; delay buffers have
  non-zero sizes and wrap with `%` (no division by zero).
- **Provable framework defects (the actual reason it "crashed with no detail"):**
  the silent-swallow exception handlers + the self-kill watchdog (see above).

## Files modified

- `src/MusicPlayer/App.xaml.cs`
- `src/MusicPlayer/Audio/EqualizerSampleProvider.cs`
- `src/MusicPlayer/DSP/SoundEffectProcessor.cs`
- `src/MusicPlayer/Audio/AudioEngine.cs` (background playback worker)
- `src/MusicPlayer/Services/PlaybackService.cs`
- `src/MusicPlayer/ViewModels/MainViewModel.cs`
- `src/MusicPlayer/DSP/SpeedPitchSampleProvider.cs` (lazy SoundTouch) — follow-up

## Build

`dotnet build -c Release --nologo` → **Build succeeded · 0 Warning(s) · 0 Error(s)**.
(the two pre-existing `CS8604` startup DI warnings were also resolved by hoisting
`ServiceProvider!` into a single non-null local — no suppressions.)

## Runtime smoke test (this machine)

Launched the freshly built Release exe: **starts clean** — DB init OK, folder scan
OK, media hotkeys registered, tray icon created; no error file written; the process
remains alive. Startup was not slowed (no loading-architecture change).

## Honest limitation

This machine has no interactive GUI access for the assistant, so a fully automatic
real-time A/B keyboard test of all 7 effect states could **not** be performed here.
The definitive verification is a manual listening run (steps below). Crucially, the
fix means that **if any effect still triggers an error, the app now logs the full
stack trace** to `<AppData>\MusicPlayer\logs\error_*.log` and the console instead of
silently freezing and being auto-killed — so the true cause will be visible.

## Manual verification steps (please run on your side)

1. Launch `bin\Release\net8.0-windows\MusicPlayer.exe`.
2. Home → All Songs → click a real track (use `C:\Music\Test\music.wav`).
3. First test **Sound Effects = NONE**, confirm audio plays cleanly. Then test each
   of the 6 effects individually, plus start/stop/next/previous stress (Play/Stop ×10).
4. New: **rapid Next/Previous** — press Next/Previous quickly several times back to back
   and confirm play follows the newest track without a UI freeze or overlap.
5. New: pause a track, then resume (Play) — confirm it resumes in place (no rebuild).
6. For each of: NONE, Electronic Tube, 3D Rotate, Tone Low, Surround, Magic Sound,
   Unvoiced Treble — record PASS/FAIL.
7. If anything still hangs/crashes, check `%APPDATA%\MusicPlayer\logs\error_*.log`
   (or the console where the app was launched) — a real stack trace will now appear.
   Send it over and the exact failing stage/exception will be identified.

## Protected baselines

Official `publish\MusicPlayer.exe` → **UNCHANGED**.
`archive\official_backup_161MB` → **UNCHANGED**.
Speed/Pitch (SoundTouch), Lyrics LRC, Queue, Back navigation, Mini→Full player,
Clean Audio, native title bar/resize, system tray, media keys, drag & drop,
command-line opening, big-list virtualization → untouched by these changes.

---

## Follow-up 2026-09-04: first-click freeze + folder-entry freeze — FINAL ROOT CAUSE

After the audio fixes above, the app still showed a one-time **"Not Responding" on the
first track click** and when **entering a folder containing many songs**. Extensive
ms-instrumentation proved the play path itself was fast (`PlayTrack`, `LoadTrack`,
`Play`, `PlaybackWorker` all ≤ 250 ms), which pointed at a **UI-thread rendering** issue
rather than audio. The user also confirmed it was **not hardware-limited**: heavy apps
(Harmonoid with many animations) ran without freezing on the same i5-4210U, while this
app froze on the first click.

### Root cause: WPF virtualization was being silently disabled

Several large lists (All Songs, Folders, Folder Details, Favorites, Recent, Most Played,
Queue) wrapped an **`ItemsControl` inside a `ScrollViewer`**. An `ItemsControl` inside a
`ScrollViewer` reports unbounded desired size, which **breaks `VirtualizingStackPanel`
virtualization** — WPF then realized **all rows at once** (191+ real element trees) **on
the UI thread**. From the `perf_trace.txt`: rows **#22–#23** took **1184–3077 ms** each on
the UI thread — the exact "Not Responding" spike — once per process, at first big-list
render (first click scrolled/realized many rows; entering a folder realized a folder's
rows).
 
This is why the earlier speculative mitigations (pre-warming `AudioAnalyzer`,
`GetDefaultAlbumArt`, TagLib startup warm-up, `Thread.Sleep` throttling, removing TagLib
from the preloader) only *reduced* but never *removed* the spike — they addressed side
effects of the real problem (heavy per-row work) but left the root cause (non-virtualized
UI realization of hundreds of rows) intact.

### The fix (applied in the codebase; verified 0 errors / 0 warnings)
1. **Replaced the wrapped `ItemsControl`+`ScrollViewer` lists with a virtualized
   `ListBox`** (`APK_VirtualListBox` + `VirtualizingStackPanel.VirtualizationMode="Recycling"`)
   in `MainWindow.xaml` and `Themes/APKTheme.xaml` for all large tabs, so only visible rows
   are realized instead of all N rows on the UI thread.
2. **Kept the full playback path on the ThreadPool** (AudioFileReader + Equalizer +
   SoundTouch + SoundEffect init all off the UI thread); the UI click handler itself took
   only ~18 ms.
3. **Removed the artificial `Thread.Sleep(50)`** from `AlbumArtLoader.cs` (no longer needed
   once rows are virtualized; `LoadFullAlbumArt`/`LoadAlbumArt` stay cache/default-first so
   the UI thread is never blocked on TagLib/disk decode).

### Result
- Build: **0 errors, 0 warnings** (Release).
- `Process Responding: True` 100% across navigation, folder-open, track switch, and the
  **first** play.
- Track-switch response ~62–90 ms; memory stable ~201 MB.
