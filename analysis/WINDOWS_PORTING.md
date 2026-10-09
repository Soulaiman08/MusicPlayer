# Windows Porting Analysis

## Android-specific → Windows equivalents

| Android | Windows Equivalent |
|---------|-------------------|
| MediaPlayer | NAudio / CSCore |
| AudioEffect | NAudio DSP |
| DynamicsProcessing | Custom DSP |
| MediaSession | System Media Transport Controls |
| Foreground Service | Background process / System Tray |
| Notification | System Tray notification |
| SharedPreferences | SQLite / Settings file |
| MediaStore | File system scan |
| MediaPlayer.PlaybackParams | NAudio Tempo / Pitch |
| BassBoost | NAudio Dsp.BassBoost |
| Virtualizer | Custom stereo widening |
| PresetReverb | NAudio Dsp.QueuedAudioSink |
| LoudnessEnhancer | NAudio volume normalization |
| Widget | System Tray / Desktop widget |
| Lock Screen | N/A (no equivalent) |
| Status Bar Lyrics | Desktop floating lyrics |
| Desktop Lyrics | WPF overlay window |
| Shake detection | N/A |
| Bluetooth control | Windows Bluetooth API |
| Headset button | Media keys |
| Slide to dismiss | WPF gesture / drag |

## Key Differences
1. No MediaStore - must scan file system directly
2. No Foreground Service - use background process
3. No Widget - use system tray or desktop gadget
4. No Lock Screen - different paradigm
5. No Shake detection - remove or use keyboard shortcut
6. Landscape layout required (16:9)
7. Window management (resize/min/max/restore)
8. File system paths differ
9. Unicode/RTL support needed
10. Different audio backend (WASAPI vs AudioTrack)
