# FullPlayer & App-Wide Animation Investigation & Fix Report

## Build Info

| Item | Value |
|---|---|
| Branch | `ui-redesign` |
| HEAD | `e1ff631` |
| Build Config | Release / net8.0-windows10.0.19041.0 / win-x64 |
| Release EXE SHA-256 | `AFBC7CE189AEE1A59CE8262F05A84E8E6E8E98A35B7C4A33C4324689909F3086` |
| New DLL SHA-256 | `A146F22411E96C6F64D4AA18D147222FFF0696E34330699ABAC418D7AD48B9B2` |
| Status | 0 Errors, 0 Warnings |

---

## The True Root Causes Found and Eliminated

### 1. Root Cause of the "VSync Disabled / Tearing" Artifact
- **The Culprit: `Timeline.SetDesiredFrameRate(..., 60)`**
  In WPF, `Timeline.SetDesiredFrameRate` decouples animations from DWM's hardware VSync presentation callback and instead hooks them to a software Windows multimedia timer targeting 16.666ms.
  Because display refresh rates are non-integer (59.94Hz to 60.004Hz) and Windows timer resolution drifts, the software timer ticks out of phase with DWM VSync. Frames arrive alternately right before and right after VSync pulses, causing periodic frame skips and double presentations (beat frequency). To the human eye, this produces horizontal micro-jitter and frame pacing tear that looks identical to disabled VSync.
- **The Solution:** Completely eliminated every single call to `SetDesiredFrameRate` (17 calls across `MainWindow.xaml.cs`). Animations now lock directly to hardware DWM VSync through WPF's native compositor.

### 2. Root Cause of Reversal Jumps on Rapid Switching (Album Art ↔ Lyrics)
- **The Culprit: Manual Offset Math & Clock Detachment Snap**
  Previous code was calling `BeginAnimation(..., null)`, which forcibly snapped dependency properties to their base value for one tick before applying a newly computed animation with manual `From` coordinates.
- **The Solution:** Implemented native WPF `HandoffBehavior.SnapshotAndReplace` with omitted `From` values. When reversing mid-flight, WPF captures an instantaneous hardware subpixel snapshot of each surface and smoothly reverses its trajectory to the target destination. Zero manual offset formulas, zero clock detachment snaps, zero seam divergence.

### 3. Root Cause of Low FPS on Integrated GPU (Intel HD 4400)
- **The Culprit: Full Subtree Re-Rasterization on Every Animation Frame**
  Without caching, moving `AlbumArtPageGrid` (bicubic scaling) and `LyricsPageGrid` (dozens of TextBlocks) forced WPF to recalculate layouts and re-rasterize vector trees every frame.
- **The Solution:** Activated `BitmapCache` (`RenderAtScale = 1.0, SnapsToDevicePixels = false`) on `incomingGrid` and `outgoingGrid` (except dynamic visualizers) strictly for the duration of the horizontal slide. The GPU renders two pre-rasterized textures sliding horizontally. Once at rest (`SettleFullPlayerPages`), `CacheMode` is removed to restore native text rendering.
- Similarly, `TrackBottomSheetPanel` and `FullPlayerGrid` use `BitmapCache` during slide-up/slide-down transitions.

### 4. Pacing & Snappiness
- Restored fast, responsive animation durations:
  - Page slide: **220ms**
  - FullPlayer enter: **240ms**
  - FullPlayer exit: **220ms**
  - Overlays: **220ms**
- Used `CubicEase` for snappy, immediate responsiveness.
