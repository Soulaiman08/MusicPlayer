# Feature Matrix

## Confirmed Features (from APK)
| Feature | Status | Implementation |
|---------|--------|----------------|
| Music Playback | Confirmed | MediaPlayer + Foreground Service |
| Play/Pause/Next/Prev | Confirmed | MediaButtonReceiver + UI |
| Seek | Confirmed | MediaPlayer.seekTo() |
| Shuffle | Confirmed | Random order list |
| Repeat | Confirmed | Off/All/Single modes |
| Queue | Confirmed | In-memory list + persistence |
| Volume Control | Confirmed | AudioManager.STREAM_MUSIC |
| Equalizer (5-band) | Confirmed | android.media.audiofx.Equalizer |
| Equalizer (10-band) | Confirmed | DynamicsProcessing (API 29+) |
| EQ Presets (23) | Confirmed | Hardcoded preset values |
| Bass Boost | Confirmed | android.media.audiofx.BassBoost |
| Reverb | Confirmed | android.media.audiofx.PresetReverb |
| Virtualizer | Confirmed | android.media.audiofx.Virtualizer |
| Loudness Enhancer | Confirmed | android.media.audiofx.LoudnessEnhancer |
| Speed Control | Confirmed | PlaybackParams.setSpeed() (0.5x-2.0x) |
| Pitch Control | Confirmed | PlaybackParams.setPitch() (-12 to +12 semitones) |
| Electronic Tube | Confirmed | Custom EQ preset |
| 3D Rotate | Confirmed | Dynamic L/R panning |
| Tone Low | Confirmed | Custom EQ preset |
| Surround Sound | Confirmed | Virtualizer at 0.9 |
| Magic Sound | Confirmed | Custom EQ preset |
| Live Treble | Confirmed | Custom EQ preset |
| Lyrics (LRC) | Confirmed | LRC parser |
| Lyrics (Online) | Confirmed | Google search |
| Desktop Lyrics | Confirmed | Floating overlay |
| Status Bar Lyrics | Confirmed | System overlay |
| Lock Screen Player | Confirmed | LockActivity |
| Drive Mode | Confirmed | Simplified UI |
| Theme/Skin System | Confirmed | Background + alpha + blur |
| Sleep Timer | Confirmed | 10/20/30/60/90 min + custom |
| Audio Editor | Confirmed | Trim/clip with waveform |
| Duplicate Finder | Confirmed | Scan + batch delete |
| Hidden Folders | Confirmed | Folder exclusion |
| ID3 Tag Editor | Confirmed | Title/Artist/Album/Genre |
| Album Art | Confirmed | Embedded + network |
| Playlists | Confirmed | Create/Edit/Reorder/Delete |
| Folder Browsing | Confirmed | Directory structure |
| Search | Confirmed | Full text search |
| Widgets (6 types) | Confirmed | 4x1/4x2/4x4/List |
| Bluetooth Control | Confirmed | Auto start/stop |
| Headset Control | Confirmed | Button + plug detection |
| ReplayGain | Confirmed | Track/Album modes |
| Gapless Playback | Confirmed | Enabled by default |
| Crossfade | Confirmed | 0-60s configurable |
| Scan Settings | Confirmed | Duration/Size/Folder filters |
| Dark Mode | Confirmed | Night theme support |
| Accent Color | Confirmed | Configurable |
| 236 Languages | Confirmed | Localized strings |
| RTL Support | Confirmed | Layout direction |

## Features NOT in APK (Will be Windows additions)
| Feature | Status | Notes |
|---------|--------|-------|
| System Tray | New | Windows-specific |
| Global Hotkeys | New | Media key support |
| File Association | New | .mp3/.flac/.wav etc. |
| Mini Window Mode | New | Always on top |
| Equalizer Visualization | New | Real-time spectrum |
| Lyrics Desktop Widget | New | Windows desktop widget |
