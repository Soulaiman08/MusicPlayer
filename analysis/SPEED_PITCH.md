# Speed and Pitch Analysis

## Speed Control
- **UI:** SeekBar with max=20
- **Range:** 0.5x to 2.0x
- **Default:** 1.0x
- **Preference key:** "preference_play_speed"
- **Display:** "Speed: 1.2 x"

### Speed Calculation (a6.l0)
- If slider < 0.5: speed = (slider * 2 * 50) / 100.0 → range 0.0-1.0
- If slider >= 0.5: speed = ((slider - 0.5) * 2 * 10) / 10.0 → range 1.0-2.0
- Effective range: 0.5x to 2.0x

## Pitch Control
- **UI:** SeekBar with max=24
- **Range:** -12 to +12 semitones
- **Default:** 0 semitones (1.0x pitch)
- **Preference key:** "preference_play_pitch"
- **Display:** "-3", "+5" etc.

### Pitch Calculation (a6.l0)
- mapped_value = ((slider * 2 - 1) * 1500) / 1500 → range [-12, +12]
- pitch = 2^(mapped_value / 12.0)
- Effective range: one octave down to one octave up

## Implementation
- Uses MediaPlayer.PlaybackParams (API 23+)
- `playbackParams.setSpeed(speed)`
- `playbackParams.setPitch(pitch)`
- Applied via x5.e.t(speed, pitch) method
- Changes applied live if playing, deferred if paused

## Independence
Speed and Pitch work INDEPENDENTLY:
- Speed=1.5x, Pitch=0 → Faster, same pitch
- Speed=1.0x, Pitch=+3 → Normal speed, higher pitch
- Speed=0.75x, Pitch=-3 → Slower, lower pitch
