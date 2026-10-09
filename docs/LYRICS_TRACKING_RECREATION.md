# Lyrics Tracking & Animation Recreation

## Architecture

### Files Modified
- `Views/MainWindow.xaml` — Lyrics panel: ScrollViewer + StackPanel + ItemsControl with DataTemplate
- `Views/MainWindow.xaml.cs` — Scroll logic, click-to-seek, mouse wheel handling
- `ViewModels/MainViewModel.cs` — Exact-time lyric scheduling via DispatcherTimer
- `Lyrics/LyricParser.cs` — LRC parser (unchanged)

### Data Flow
```
AudioEngine (250ms timer) → PositionChanged → MainViewModel.UpdateLyricProgress()
  ├─ Finds current line via reverse scan
  ├─ Sets CurrentLyricIndex → PropertyChanged → MainWindow.ScrollLyricsToCurrent()
  └─ ScheduleNextLyricTransition() → DispatcherTimer fires at EXACT lyric timestamp
       └─ UpdateLyricProgress() → instant line switch
```

## Key Design Decisions

### 1. Exact-Time Scheduling (NOT polling)
- `ScheduleNextLyricTransition()` calculates delay to next lyric timestamp
- Single-shot `DispatcherTimer` fires at the exact moment the line should change
- Adjusts for playback speed (delay / speed)
- Eliminates the 250ms polling delay

### 2. ScrollViewer Centering
- `VerticalScrollBarVisibility="Hidden"` — allows programmatic + mouse wheel scrolling, hides scrollbar
- `TranslatePoint` to ScrollViewer for position calculation
- `container.UpdateLayout()` before TranslatePoint ensures correct layout
- Dynamic top/bottom padding = `visibleHeight / 2` so first line starts centered

### 3. Visual Style
- **Current line**: `#F0C040` (gold), FontSize=26, Bold, Opacity=1.0
- **Non-current lines**: `#55FFFFFF`, FontSize=16, Normal, Opacity=0.35
- **Gradient overlay**: LinearGradientBrush fades top/bottom edges to background `#FF101216`
- **Animation**: Cubic ease-in-out, 200ms duration, 16ms tick interval

### 4. User Interaction
- **Mouse wheel**: Lyrics page allows scrolling; `PlayingPageArea_PreviewMouseWheel` returns early when on Lyrics page
- **Click to seek**: `PreviewMouseLeftButtonDown` + `Background="Transparent"` ensures click reaches handler
- **Manual scroll pause**: Auto-tracking pauses 3 seconds when user scrolls manually, then resumes

## Root Causes Found & Fixed

| Issue | Root Cause | Fix |
|-------|-----------|-----|
| Line not centered at start | Lyrics page collapsed → ActualHeight=0 → scroll returns early | Call ScrollLyricsToCurrent on page switch + add retry at DispatcherPriority.Loaded |
| First line at top | TranslatePoint Y=0 for first item, formula gives negative offset clamped to 0 | Dynamic padding = visibleHeight/2 on StackPanel |
| 250ms delay | PositionChanged fires every 250ms | Exact-time DispatcherTimer scheduling |
| Mouse wheel navigates pages | PlayingPageArea_PreviewMouseWheel intercepts all events | Skip when on Lyrics page with lyrics |
| Click doesn't work | MouseLeftButtonDown consumed by parent | PreviewMouseLeftButtonDown + Background="Transparent" |
| TranslatePoint unreliable with TranslateTransform | Transform affects coordinate space | Use ScrollViewer native scrolling |
