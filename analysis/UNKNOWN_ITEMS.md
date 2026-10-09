# Unknown / Unverified Items

## Items That Could NOT Be Fully Extracted

### 1. Sound Effect Algorithms
- **Electronic Tube:** Original DSP algorithm unknown. Only EQ preset values extracted.
- **3D Rotate:** Panning mechanism known, but exact virtualizer interaction unclear.
- **Tone Low:** EQ values extracted, algorithm is standard EQ boost/cut.
- **Surround Sound:** Uses Virtualizer at 0.9 strength, no custom DSP.
- **Magic Sound:** EQ values extracted, no custom algorithm.
- **Live Treble:** EQ values extracted, no custom algorithm.

**Windows Implementation:** Use equivalent DSP (NAudio filters) matching the EQ curves.

### 2. Shuffle Algorithm
- Standard shuffle likely used (Fisher-Yates or similar)
- Exact implementation not extracted due to ProGuard obfuscation
- Behavior: No repeat until all tracks played, then reset

**Windows Implementation:** Use standard shuffle algorithm (Fisher-Yates).

### 3. Lyrics Online Search
- Google search URL patterns not fully extracted
- Lyrics provider domains not visible in decompiled code
- Likely uses web scraping or API

**Windows Implementation:** Implement web search for lyrics files.

### 4. Image Loading (Glide)
- Glide configuration details obfuscated
- Cache strategy not fully visible
- Album art loading pipeline partially understood

**Windows Implementation:** Use async image loading with caching.

### 5. Ad Integration
- AdMob configuration partially visible
- Gift/ad display system complex
- Not needed for Windows version

### 6. Widget Data Provider
- WidgetMusicService data flow partially understood
- RemoteViews construction details obfuscated

### 7. Privacy Policy Flow
- PrivacyPolicyActivity details
- Data collection consent mechanism

### 8. Drive Mode Voice
- Voice reminder implementation details
- Text-to-speech engine used

### 9. Audio Editor Internals
- SoundWaveView rendering algorithm
- Trim/clip audio processing details
- Output format selection

### 10. Database Schema
- Room database entity definitions
- Migration strategies
- Exact table structures
