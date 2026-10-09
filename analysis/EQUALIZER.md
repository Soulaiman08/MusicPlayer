# Equalizer Analysis

## Architecture
The app has TWO equalizer engines:

### Legacy EQ (API < 29)
- **Class:** g5.j
- **Type:** android.media.audiofx.Equalizer
- **Session ID:** 1000
- **Bands:** 5
- **Frequencies:** 60, 230, 910, 3600, 14000 Hz

### Modern EQ (API 29+)
- **Class:** g5.i
- **Type:** android.media.audiofx.DynamicsProcessing
- **Session ID:** 1000
- **Bands:** 10
- **Frequencies:** 31, 62, 125, 250, 500, 1000, 2000, 4000, 8000, 16000 Hz
- **Pre-EQ:** Main 10-band gains
- **Post-EQ:** BassBoost, Virtualizer, Loudness
- **Gain unit:** hundredths of dB (e.g., 1500 = 15.0 dB)

## EQ Presets (23 total)
1. Custom (user-defined)
2. Normal
3. Classical
4. Dance
5. Straightness
6. Folk
7. Heavy Metal
8. Hip Hop
9. Jazz
10. Pop
11. Rock
12. Acoustic
13. Bass Boost
14. Treble Boost
15. Vocal Boost
16. Head phones
17. Deep
18. Electronic
19. Latin
20. Loud
21. Lounge
22. Piano
23. RB

## UI Layout
- **Tabs:** EQ | VOL (ViewPager2 + TabLayout)
- **EQ Tab:** Vertical band sliders + preset selector
- **VOL Tab:** System volume + Volume boost + Balance
