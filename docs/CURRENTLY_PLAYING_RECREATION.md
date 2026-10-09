# CURRENTLY PLAYING — RECREATION STATUS

Date: 2026-09-02 | After Full Visual Audit + Corrected Implementation

## Layout (matching original APK screenshots)

```
LEFT ≈ 55% (sliding panel)    RIGHT ≈ 45% (fixed controls)
┌──────────────────────┐      ┌──────────────────────────┐
│                      │      │ [<]  Title / Artist  [≡] │
│  ┌────────────────┐  │      │                          │
│  │  Album Art     │  │      │  ♡   T          [LRC]  ⋮ │
│  │  (full bleed)  │←→│      │  ═════════════════ [♪]   │
│  └────────────────┘  │      │  00:00          04:00     │
│    OR Lyrics page    │      │  ⟲   ◀  ▶  ▷   [EQ]     │
│    OR Visualizer     │      └──────────────────────────┘
│    ● ● ●            │
└──────────────────────┘
```

## Left ≈ 55% — 3-Page Sliding Panel

### Page 0: Album Art (default)
- Full bleed, edge-to-edge
- No border, no overlay, no title/artist on art
- Swipe left → Lyrics, swipe right → Visualizer

### Page 1: Lyrics (swipe left)
- Replaces album art (slides in from right)
- Current line: yellow/accent, bold, FontSize 17
- Other lines: gray, FontSize 15
- Centered text, auto-scroll to current line
- Click-to-seek preserved

### Page 2: Visualizer (swipe right)
- Replaces album art (slides in from right)
- Real audio-reactive frequency bars
- Gradient colors: red (bass) → orange → yellow → green (highs)
- Bars at bottom with horizontal baseline
- Driven by AudioAnalyzer (FFT 2048 → 64 bands)

### Slide Animation
- TranslateTransform on sliding container
- CubicEase EaseOut, 280ms
- AlbumArt → X=0, Lyrics → X=-width, Visualizer → X=-2*width

### Page Dots
- 3 dots (● ● ●)
- Active dot filled white, inactive subtle

## Right ≈ 45% — Fixed Controls

### Row 0: Top Bar
- Back `<` → CloseFullPlayerCommand
- Title/Artist (center) — FontSize 17/13
- Queue icon (right) → ShowQueuePanelCommand

### Row 1: Action Row
- Heart ♡ (left) → ToggleFavoriteCommand
- T button (text/lrc)
- LRC button (right) → toggles Lyrics page
- Three-dot menu ⋮ → ContextMenu (Speed/Pitch, Sound Effects, Queue)

### Row 2: Progress Bar
- Slider → PlaybackProgress
- Sound effects icon [♪] right of slider → ShowSoundEffectsPanelCommand
- Time labels (Position / Duration)

### Row 3: Transport Controls
- Shuffle (left) → ToggleShuffleCommand
- Previous → PreviousCommand
- Large Play/Pause (center) → PlayPauseCommand
- Next → NextCommand
- EQ icon (far right) → ShowEqualizerPanelCommand

## Audio Analysis Pipeline (non-destructive)

```
AudioFileReader → EQ → Reverb → Loudness → Effects → SpeedPitch → Volume
    → AudioAnalysisCaptureProvider (pass-through + ring buffer + RMS)
    → WaveOutEvent
```

- AudioAnalyzer: FFT 2048 → 64 log-spaced bands + level
- Timer-driven ~30fps on background thread
- Visualizer reads via CompositionTarget.Rendering

## Lyrics Pipeline (PROTECTED — unchanged)

- music.mp3 → same directory → music.lrc
- Auto-scroll via PropertyChanged → BringIntoView
- Click-to-seek via SeekToLyricCommand
- Current line highlight via BoolToBrushConverter

## Protected Baseline (unchanged)

- Speed/Pitch (SoundTouch) ✅
- Lyrics LRC loader ✅
- Back Navigation ✅
- Mini Player → Full Player ✅
- Queue ✅
- Clean Audio Defaults ✅
- publish\MusicPlayer.exe hash: 7063... ✅
- archive\official_backup_161MB hash: 3423... ✅

## Build Status

- Debug: 0 Error, 0 Warning ✅
- Release: 0 Error, 0 Warning ✅
- Runtime: App starts OK ✅
