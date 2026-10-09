# Sound Effects Analysis

## 6 Built-in Sound Effects

### 1. Electronic Tube (Group 0)
- **Class:** f5.c
- **5-band EQ:** {0, 800, 0, 700, 0, 300, 0, 600, 0, 0}
- **10-band EQ:** {840, 723, 316, 590, 0, 0, 0, 0, 0, 0}
- **Bass Boost:** Enabled
- **Virtualizer:** Disabled
- **Reverb:** None
- **Loudness:** Disabled
- **Description:** Warm tube-like sound with low-mid emphasis

### 2. 3D Rotate (Group 1)
- **Class:** f5.b
- **Mechanism:** Dynamic left/right volume panning via Handler (500ms interval)
- **Oscillation:** 0.0 → 1.0 → 0.0 in 0.1 steps
- **Left/Right:** panValue / (1.0 - panValue)
- **Mobile:** Uses virtualizer 0.888f with stereo
- **Non-mobile:** Pure panning only
- **Description:** Spatial rotating effect

### 3. Tone Low (Group 2)
- **Class:** f5.h
- **5-band EQ:** {600, 600, 600, 1000, 600, 600, -300, -300, -300, -300}
- **10-band EQ:** {-1122, 1088, 743, -1122, -1122, 0, 0, 0, 0, 0}
- **Bass Boost:** Enabled
- **Description:** Warm low-end emphasis, attenuated highs

### 4. Surround Sound (Group 3)
- **Class:** f5.g
- **EQ:** All default (no changes)
- **Virtualizer:** Enabled, strength 0.9f
- **Description:** Pure spatial widening via Virtualizer

### 5. Magic Sound (Group 4)
- **Class:** f5.e
- **5-band EQ:** {0, 0, 0, 0, 0, 0, 0, 0, 1200, 1200}
- **10-band EQ:** {-200, 0, 800, 900, 400, 0, 0, 0, 0, 0}
- **Bass Boost:** Enabled
- **Description:** High-frequency sparkle (8000-16000 Hz boosted)

### 6. Live Treble (Group 5)
- **Class:** f5.d
- **5-band EQ:** {600, 1000, 800, 500, 600, 1200, 400, 500, 800, 1300}
- **10-band EQ:** {0, 0, 0, 1200, 1200, 0, 0, 0, 0, 0}
- **Bass Boost:** Enabled
- **Description:** Mid-high emphasis (250-500 Hz boosted +12dB)

### User Defined (Group -1)
- **Class:** f5.f
- Custom EQ values with all effects configurable

## Sound Effect Base Class (f5.j)
Each effect provides:
- 10-band EQ values for 5-band mode (method j())
- 10-band EQ values for 10-band mode (method k())
- Virtualizer/stereo strength (method m())
- Loudness enhancer gain (method i())
- Loudness enabled flag (method n())
- Bass boost enabled flag (method o())
- Virtualizer enabled flag (method p())
- Stereo enabled flag (method q())
- Reverb preset (method l())
