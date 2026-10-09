# MASTER SPECIFICATION
# Music Player Windows Recreation

## Source APK
- **Package:** media.music.musicplayer
- **Version:** 7.3.5 (735)
- **Developer:** ijoysoft
- **APK Size:** 10.6 MB

---

## PROJECT GOAL

Create a Windows desktop recreation of the Android Music Player app, maintaining the original experience and visual fidelity, adapted for landscape 16:9 desktop usage.

---

## TECHNOLOGY STACK

- **Language:** C# (.NET 8+)
- **UI Framework:** WPF + XAML
- **Audio Engine:** NAudio / CSCore
- **Database:** SQLite (via sqlite-net)
- **Image Loading:** Custom async loader with disk cache
- **Metadata:** TagLib# (TagLibSharp)
- **Lyrics:** Custom LRC parser

---

## CORE FEATURES (Recreated from APK)

### 1. Audio Playback
- Play/Pause/Next/Previous
- Seek with configurable jump (5/10/15/20/30/60 seconds)
- Shuffle/Repeat (off/all/single)
- Queue management with persistence
- Gapless playback
- Crossfade (0-60 seconds)
- Volume control (system + per-channel balance)
- Volume boost
- Audio focus management (pause on other audio)
- Media keys support
- System tray controls

### 2. Equalizer
- 10-band equalizer (31Hz to 16kHz)
- 23 built-in presets
- Custom user presets
- Bass Boost
- Reverb (7 presets)
- Virtualizer/Stereo widening
- Loudness enhancer
- Speed control (0.5x to 2.0x)
- Pitch control (-12 to +12 semitones)

### 3. Sound Effects
- Electronic Tube
- 3D Rotate
- Tone Low
- Surround Sound
- Magic Sound
- Live Treble
- User Defined (custom)

### 4. Library
- File system music scanning
- Songs/Albums/Artists/Genres/Folders views
- Sort options per view
- Search with debounce
- Hidden folders
- Duplicate finder
- Album art extraction (embedded + folder)
- Metadata reading (ID3 tags)

### 5. Playlists
- Create/Rename/Delete playlists
- Add/Remove/Reorder songs
- Drag-and-drop reorder
- Smart playlists (Recent Play, Most Play, Favorites)
- Queue save as playlist

### 6. Lyrics
- Embedded lyrics (USLT/SYLT)
- LRC file parsing (synced)
- Desktop floating lyrics overlay
- Click-to-seek
- Offset adjustment
- Auto-scroll
- Highlight current line

### 7. UI Screens
- Home (grid cards + playlists)
- Library (tabbed: Songs/Albums/Artists/Genres/Folders)
- Full Player (album art, controls, lyrics)
- Mini Player (bottom panel)
- Equalizer (EQ + VOL tabs)
- Sound Effects (6 effects grid)
- Settings (9 categories)
- Sleep Timer
- Theme Editor
- Search
- Queue
- Playlist Editor

### 8. Theming
- Dark/Light mode
- Accent color picker
- Background image
- Blur/Alpha overlay

---

## DESIGN SPECIFICATIONS

### Window
- Default size: 1280 x 720 (16:9)
- Minimum size: 960 x 540
- Resizable, minimizable, maximizable
- Title bar: Custom (app title + controls)
- Start position: Center

### Color Palette
- Primary accent: #FF4995F3 (blue)
- Selection: #FFFFCF4D (yellow)
- Dark background: #FF1D2026
- Light background: #FFFFFFFF
- Text primary: #FFFFFFFF (dark bg) / #FF000000 (light bg)
- Text secondary: #B3FFFFFF (dark bg) / #8A000000 (light bg)
- Dividers: #1AFFFFFF (dark bg) / #1A000000 (light bg)

### Typography
- Title: 20sp, sans-serif-medium
- Body: 14-16sp
- Caption: 12sp
- Tab: 14sp, sans-serif-medium
- Home grid label: 13sp

### Spacing
- Toolbar height: 56dp
- List item height: 72dp
- Mini player height: 60dp
- Grid spacing: 8dp
- Content padding: 16dp
- Card padding: 8dp

### Home Screen
- Grid: 3 columns (landscape)
- Cards: SquareRelativeLayout with icon + label + count
- Playlists: Horizontal scroll below grid
- Mini player: Bottom panel, 60dp height
- Sliding panel: Expand to full player

### Full Player
- Background: Album art (full screen, blurred)
- Overlay: Semi-transparent dark
- Controls: Centered, play/pause 56dp, others 40dp
- Seek bar: With time labels
- Title/Artist: White text

---

## LIBRARY SCANNING

### Scan Filters
- Minimum duration: 60 seconds (configurable)
- Minimum file size: 51200 KB (configurable)
- Exclude ringtones: false (configurable)
- Folder exclusion: via hidden folders list

### Supported Formats
- MP3, FLAC, WAV, AAC, OGG, M4A, WMA
- (All formats supported by NAudio/TagLib#)

### Metadata Fields
- Title, Artist, Album, Album Artist, Genre, Year
- Track number, Disc number
- Duration, File size, Bitrate, Sample rate
- Album art (embedded)

---

## STORAGE

### SQLite Database
- Tracks cache (path, metadata, play count, favorite, hidden)
- Playlists (name, tracks, order)
- Queue (tracks, current index, shuffle order)
- Settings (all preferences)
- Hidden folders
- Lyrics bindings

### File Cache
- Album art cache (thumbnail + full)
- Lyrics cache
- Settings file (JSON backup)

---

## PERFORMANCE REQUIREMENTS

- Handle 10,000+ tracks without UI freeze
- Virtual scrolling for song lists
- Async file scanning
- Background metadata reading
- Thumbnail caching with disk persistence
- Debounced search (300ms)

---

## UNICODE SUPPORT

- Full Unicode support
- Arabic/Hebrew/Chinese/Japanese
- RTL layout support
- File path handling with special characters

---

## ANALYSIS STATISTICS

| Metric | Count |
|--------|-------|
| Files analyzed | 1,680+ |
| Activities | 38 |
| Fragments | 20+ |
| Layouts | 375 |
| Drawables | 783 |
| Animations | 69 |
| Menu files | 16 |
| Color resources | 102 |
| Value directories | 263 |
| String locales | 236 |
| Custom views | 42+ |
| Services | 4 |
| Receivers | 9 |
| Widgets | 6 |
| EQ presets | 23 |
| Sound effects | 6 |
| Settings | 40+ |
| DEX classes | 6,419 |

---

## DOCUMENTATION INDEX

| File | Content |
|------|---------|
| APK_STRUCTURE.md | Package, permissions, libraries |
| UI_SCREENS.md | All 38 activities |
| UI_COMPONENTS.md | 42+ custom views |
| UI_COLORS.md | Color definitions |
| UI_DIMENSIONS.md | All dimensions |
| UI_STYLES.md | Themes and styles |
| UI_ANIMATIONS.md | Animation files |
| UI_NAVIGATION.md | Navigation flow |
| AUDIO_FEATURES.md | Playback engine |
| PLAYER.md | Player UI details |
| EQUALIZER.md | EQ system |
| SOUND_EFFECTS.md | 6 effects details |
| SPEED_PITCH.md | Speed/pitch math |
| LYRICS.md | Lyrics system |
| METADATA.md | Data models |
| LIBRARY.md | Library system |
| PLAYLISTS.md | Playlist management |
| QUEUE.md | Queue system |
| SETTINGS.md | All 40+ settings |
| THEMES.md | Theme system |
| WINDOWS_PORTING.md | Android→Windows mapping |
| FEATURE_MATRIX.md | Feature status |
| UNKNOWN_ITEMS.md | Unverified items |
| MASTER_SPECIFICATION.md | This file |

---

**STATUS: ANALYSIS COMPLETE**

**DO NOT PROCEED TO IMPLEMENTATION WITHOUT USER APPROVAL**
