# Audio Features Analysis

## Playback Engine
- **Core:** Android MediaPlayer (x5/e.java)
- **Audio Attributes:** USAGE_MEDIA, CONTENT_TYPE_MUSIC, STREAM_MUSIC
- **Wake Lock:** Enabled
- **Audio Session:** Shared (ID 1000)
- **Background Playback:** Foreground service with notification

## Playback Controls
- Play/Pause
- Previous/Next
- Seek (forward/backward configurable: 5/10/15/20/30/60 seconds)
- Shuffle (random order)
- Repeat (off/all/single)
- Queue management

## Playback Modes
| Mode | ID | Description |
|------|----|-------------|
| Order | 0 | Sequential |
| Loop All | 1 | Repeat all tracks (default) |
| Shuffle | 2 | Random order |
| Single | 3 | Repeat current track |

## Volume Control
- System volume (AudioManager.STREAM_MUSIC)
- Per-channel volume (left/right balance)
- Volume boost (LoudnessEnhancer)
- Volume fade (play/pause transitions)
- Mute/Unmute

## Audio Focus
- API 26+: AudioFocusRequest with AUDIOFOCUS_GAIN
- Legacy: requestAudioFocus with STREAM_MUSIC
- Handles: GAIN, LOSS_TRANSIENT, LOSS
- Telephone state monitoring (incoming calls)
- Simultaneous play option

## MediaSession
- MediaSessionCompat with transport controls
- Metadata: TITLE, ARTIST, ALBUM, DURATION, GENRE, ALBUM_ART
- Actions: PLAY, PAUSE, PLAY_PAUSE, STOP, SKIP_NEXT, SKIP_PREV, SEEK
- Bluetooth car lyrics support

## ReplayGain
- Modes: None, Track, Album
- Pre-amplifier gain
- Applied as volume multiplier per-channel

## Gapless Playback
- Enabled by default
- Crossfade: configurable 0-60 seconds
