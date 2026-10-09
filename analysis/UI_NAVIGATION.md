# UI Navigation Analysis

## Primary Navigation Flow

### Drawer Navigation (MainActivity)
Left Drawer (FrameLayout - main_menu):
  → Home (fragment_main)
  → Library (fragment_library with tabs)
  → Equalizer (ActivityEqualizer)
  → Sleep Timer (ActivitySleep)
  → Themes (ActivityTheme)
  → Scan (ScanMusicActivity)
  → Settings (ActivitySetting)
  → Exit

### Home Screen Tabs
Main fragment container:
  → Home grid cards → Library
  → Playlists section → Playlist detail
  → Mini Player → Full Player (MusicPlayActivity)

### Library Tabs (scrollable)
TabLayout with ViewPager2:
  → Songs (fragment_music_list)
  → Albums (fragment_album)
  → Artists
  → Genres
  → Folders (fragment_folder)
  → Playlists (fragment_playlist)

### Full Player
MusicPlayActivity:
  → Album art (tap for lyrics)
  → Seek bar
  → Previous/Next/Play/Pause
  → Shuffle/Repeat/Favorite
  → Queue (ActivityMusicQueue)
  → Effects (ActivityEffectGroup)
  → Back → Main

### Sliding Panel
Main screen bottom panel:
  → Mini player (60dp collapsed)
  → Expand → Full player details

### Activity Stack
WelcomeActivity → MainActivity → [ActivityLibrary activities]
MainActivity → MusicPlayActivity → [Effect/Lyrics activities]
MainActivity → ActivitySetting
MainActivity → ActivityEqualizer → [EQ details]
