# Modern Windows MusicPlayer Icon System

## 1. Overview
The MusicPlayer icon system has been completely rewritten from the ground up for the Windows desktop platform. The legacy Android APK-derived icon geometries and `APK_Icon_*` resources have been **fully retired and replaced** with an original, unified, high-precision vector icon library defined in [`Themes/Icons.xaml`](../Themes/Icons.xaml).

---

## 2. Design Principles & Specifications

- **Coordinate System**: Built on a standardized **24×24 unit grid** with uniform bounding boxes.
- **Optical Weight**: Deterministic stroke weight and visual density (equivalent to 1.75px–2.0px stroke at 24×24), ensuring consistent balance between geometric silhouettes and stroke icons.
- **Rendering Technology**: WPF `StreamGeometry` with mini-language path data for minimal memory footprint and zero CPU parsing overhead at runtime.
- **Theme-Adaptive**: Designed for use with dynamic theme brushes (`Fill="{DynamicResource ...}"` or `Fill="{StaticResource ...}"`). Icons adapt seamlessly across Dark, Light, Amoled, and wallpaper themes.
- **High-DPI Determinism**: Pixel-snapped and vector-scaled, guaranteeing crisp rendering from 100% to 300% DPI without blurring.

---

## 3. Semantic Icon Catalog

### Navigation & Window
| Semantic Key | Visual Description | Primary Usage |
| :--- | :--- | :--- |
| `Icon.Menu` | 3 horizontal pill bars | Hamburger navigation drawer |
| `Icon.Back` | Smooth left chevron/arrow | Header back button across all screens |
| `Icon.ArrowRight` | Minimal right chevron | Navigation disclosure arrows |
| `Icon.ChevronUp` | Upward directional chevron | Collapsible panels & dropdowns |
| `Icon.ChevronDown` | Downward directional chevron | Collapsible panels & dropdowns |
| `Icon.Close` | Balanced cross with rounded caps | Dialog and panel close buttons |
| `Icon.Minimize` | Horizontal bar | Titlebar minimize |
| `Icon.Maximize` | Framed window outline | Titlebar maximize |
| `Icon.Restore` | Overlapping window outlines | Titlebar restore |

### Playback Controls
| Semantic Key | Visual Description | Primary Usage |
| :--- | :--- | :--- |
| `Icon.Play` | Equilateral triangle with rounded vertices | Play track / album / playlist |
| `Icon.Pause` | Dual vertical pill bars | Pause current playback |
| `Icon.Next` | Triangle + vertical bar | Skip to next track |
| `Icon.Previous` | Vertical bar + triangle | Skip to previous track |
| `Icon.Shuffle` | Intersecting curved arrows with arrowheads | Toggle shuffle playback |
| `Icon.Repeat` | Looping circular rounded arrows | Repeat all tracks |
| `Icon.RepeatOne` | Looping arrows with integrated "1" | Repeat current track |

### Library & Collections
| Semantic Key | Visual Description | Primary Usage |
| :--- | :--- | :--- |
| `Icon.Songs` | Modern beamed double eighth-note | All songs library view & card |
| `Icon.Folder` | Minimal tabbed folder outline | Folders view & card |
| `Icon.Album` | Vinyl record with center spindle hole | Albums view |
| `Icon.Artist` | Head-and-shoulders minimalist silhouette | Artists view |
| `Icon.Genre` | Stylized vertical audio equalizer bars | Genres view |
| `Icon.Playlist` | Stacked list lines with play arrow | Playlists view & dialogs |
| `Icon.Queue` | Stacked queue lines with trailing eighth-note | Playback queue drawer & panel |
| `Icon.Favorite` | Precision-curved heart outline | Unfavorited state |
| `Icon.FavoriteFilled`| Solid filled precision heart | Favorited state & Favorites card |
| `Icon.Recent` | Clock dial with 90° angle hands | Recent play view & card |
| `Icon.RecentAdd` | Clock dial with plus accent | Recent add view & card |
| `Icon.MostPlayed` | High-fidelity headphone outline | Most played view & card |

### Audio & DSP
| Semantic Key | Visual Description | Primary Usage |
| :--- | :--- | :--- |
| `Icon.Equalizer` | 3 vertical sliders with staggered adjustment knobs | Equalizer screen & buttons |
| `Icon.SoundEffects`| Headset with internal dynamic sound spectrum | Sound effects dialog |
| `Icon.SpeedPitch` | Radial gauge with speed indicator needle | Speed & Pitch control dialog |
| `Icon.Lyrics` | Document page with musical note header | Lyrics view toggle |
| `Icon.Visualizer` | Multi-bar audio frequency visualizer | Spectrum visualizer toggle |
| `Icon.Volume` | Speaker cone with sound waves | General volume indicator |
| `Icon.VolumeLow` | Speaker cone with single subtle arc | Low volume (1–32%) |
| `Icon.VolumeMedium` | Speaker cone with dual arcs | Medium volume (33–65%) |
| `Icon.VolumeHigh` | Speaker cone with triple full arcs | High volume (66–100%) |
| `Icon.VolumeMute` | Speaker cone with cross / mute mark | Muted volume (0%) |

### Actions & Tools
| Semantic Key | Visual Description | Primary Usage |
| :--- | :--- | :--- |
| `Icon.Search` | 45° angle magnifying glass | Search bar & toggle buttons |
| `Icon.More` | 3 vertically aligned uniform dots | Track options & context menus |
| `Icon.Add` / `Icon.Plus` | Centered balanced plus symbol | Add to playlist, add folder |
| `Icon.Delete` | Trash bin with defined lid & slats | Delete track / playlist |
| `Icon.Check` | Smooth 45° angled checkmark | Selected wallpaper, success status |
| `Icon.Info` | Circular badge with centered "i" | Track information bottom sheet |
| `Icon.Share` | 3-node connected network graph | Share track bottom sheet |
| `Icon.Hide` | Eye silhouette with diagonal strike-through | Hide track / folder |
| `Icon.Show` | Eye silhouette with centered pupil | Unhide track / folder |
| `Icon.Edit` | Sleek angled pencil with tip | Edit tag metadata |
| `Icon.Settings` | 8-tooth precision gear with center aperture | Settings screen |
| `Icon.Theme` | Artist palette with color wells | Themes & wallpapers screen |
| `Icon.Timer` / `Icon.Sleep` | Stopwatch with top knurled crown | Sleep timer dialog |
| `Icon.Artwork` | Framed photo with mountains and sun | Album art editor |
| `Icon.Ringtone` | Resonant bell with clapper | Set as ringtone |
| `Icon.Trim` | Audio waveform scissor cutter | Ringtone cutter |
| `Icon.Scan` | Radar scanner with circular sweep line | Media scanner |

---

## 4. Legacy APK Migration Mapping

| Legacy APK Key | New Modern Key | Status |
| :--- | :--- | :--- |
| `APK_Icon_ArrowBack` | `Icon.Back` | **Replaced** |
| `APK_Icon_ArrowRight` | `Icon.ArrowRight` | **Replaced** |
| `APK_Icon_ChevronDown` | `Icon.ChevronDown` | **Replaced** |
| `APK_Icon_ChevronUp` | `Icon.ChevronUp` | **Replaced** |
| `APK_Icon_Close` | `Icon.Close` | **Replaced** |
| `APK_Icon_Menu` | `Icon.Menu` | **Replaced** |
| `APK_Icon_Minimize` | `Icon.Minimize` | **Replaced** |
| `APK_Icon_Maximize` | `Icon.Maximize` | **Replaced** |
| `APK_Icon_Restore` | `Icon.Restore` | **Replaced** |
| `APK_Icon_Play` | `Icon.Play` | **Replaced** |
| `APK_Icon_Pause` | `Icon.Pause` | **Replaced** |
| `APK_Icon_Next` | `Icon.Next` | **Replaced** |
| `APK_Icon_Previous` | `Icon.Previous` | **Replaced** |
| `APK_Icon_Shuffle` | `Icon.Shuffle` | **Replaced** |
| `APK_Icon_RepeatAll` | `Icon.Repeat` | **Replaced** |
| `APK_Icon_RepeatOne` | `Icon.RepeatOne` | **Replaced** |
| `APK_Icon_Songs` | `Icon.Songs` | **Replaced** |
| `APK_Icon_Folder` | `Icon.Folder` | **Replaced** |
| `APK_Icon_Album` | `Icon.Album` | **Replaced** |
| `APK_Icon_Artist` | `Icon.Artist` | **Replaced** |
| `APK_Icon_Genre` | `Icon.Genre` | **Replaced** |
| `APK_Icon_Playlist` | `Icon.Playlist` | **Replaced** |
| `APK_Icon_Queue` | `Icon.Queue` | **Replaced** |
| `APK_Icon_Favorite` | `Icon.Favorite` | **Replaced** |
| `APK_Icon_FavoriteFilled` | `Icon.FavoriteFilled` | **Replaced** |
| `APK_Icon_MostPlay` | `Icon.MostPlayed` | **Replaced** |
| `APK_Icon_Recent` | `Icon.Recent` | **Replaced** |
| `APK_Icon_RecentAdd` | `Icon.RecentAdd` | **Replaced** |
| `APK_Icon_Equalizer` | `Icon.Equalizer` | **Replaced** |
| `APK_Icon_SoundEffect` | `Icon.SoundEffects` | **Replaced** |
| `APK_Icon_SpeedPitch` | `Icon.SpeedPitch` | **Replaced** |
| `APK_Icon_Volume` | `Icon.Volume` | **Replaced** |
| `APK_Icon_VolumeHigh` | `Icon.VolumeHigh` | **Replaced** |
| `APK_Icon_VolumeMedium` | `Icon.VolumeMedium` | **Replaced** |
| `APK_Icon_VolumeLow` | `Icon.VolumeLow` | **Replaced** |
| `APK_Icon_VolumeMute` | `Icon.VolumeMute` | **Replaced** |
| `APK_Icon_Search` | `Icon.Search` | **Replaced** |
| `APK_Icon_More` | `Icon.More` | **Replaced** |
| `APK_Icon_Add` | `Icon.Add` | **Replaced** |
| `APK_Icon_Plus` | `Icon.Plus` | **Replaced** |
| `APK_Icon_Delete` | `Icon.Delete` | **Replaced** |
| `APK_Icon_Check` | `Icon.Check` | **Replaced** |
| `APK_Icon_Info` | `Icon.Info` | **Replaced** |
| `APK_Icon_Share` | `Icon.Share` | **Replaced** |
| `APK_Icon_Settings` | `Icon.Settings` | **Replaced** |
| `APK_Icon_Skin` | `Icon.Theme` | **Replaced** |
| `APK_Icon_Sleep` | `Icon.Sleep` | **Replaced** |
| `APK_Icon_Timer` | `Icon.Timer` | **Replaced** |
| `APK_Icon_Artwork` | `Icon.Artwork` | **Replaced** |
| `APK_Icon_Ringtone` | `Icon.Ringtone` | **Replaced** |
| `APK_Icon_Trim` | `Icon.Trim` | **Replaced** |
| `APK_Icon_Scan` | `Icon.Scan` | **Replaced** |
| `APK_Icon_Hide` | `Icon.Hide` | **Replaced** |

---

## 5. Usage Guidelines

### XAML Usage
```xml
<Path Data="{StaticResource Icon.Search}"
      Fill="{DynamicResource APK_TextHighBrush}"
      Stretch="Uniform"
      Width="18" Height="18" />
```

### Code-Behind / ViewModel Usage
```csharp
var geometry = Application.Current?.TryFindResource("Icon.VolumeHigh") as Geometry;
```
