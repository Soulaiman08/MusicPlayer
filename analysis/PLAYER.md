# Player Analysis

## Full Player (MusicPlayActivity)
- **Layout:** activity_musicplay.xml
- **Root:** DragDismissLayout (swipe to dismiss)
- **Background:** SkinImageView (album art / theme skin)
- **Overlay:** TranslucentBarFrameLayout (#66000000)

## Player Components
1. Album Art (SkinImageView - full screen background)
2. Title (TextView - 20sp, white)
3. Artist (TextView - 14sp, #b3ffffff)
4. Favorite button (ImageView - toggle)
5. SeekBar (custom, with thumb)
6. Current time / Total time (TextViews - 12sp)
7. Play/Pause (56dp button)
8. Previous/Next (40dp buttons)
9. Forward/Backward 15s (40dp buttons)
10. Mode (shuffle/repeat/order/single)
11. Equalizer shortcut
12. Lyrics overlay

## Mini Player
- **Height:** 60dp
- **Components:**
  - Album thumbnail (in LoopViewPager)
  - Song title (20sp, white)
  - Artist name (14sp, #b3ffffff)
  - Favorite button
  - Progress seekbar (2dp height)
  - Play/Pause (40dp)
  - Next (40dp)
  - Queue button

## Player Controls (music_play_controller.xml)
Layout: ConstraintLayout, horizontal chain
- Mode button: 40x40dp, padding 8dp
- Backward 15s: 40x40dp, padding 7dp
- Previous: 40x40dp, padding 8dp
- Play/Pause: 56x56dp, padding 10dp
- Next: 40x40dp, padding 8dp
- Forward 15s: 40x40dp, padding 7dp
- Equalizer: 40x40dp, padding 8dp

## Lock Screen Player (LockActivity)
- SingleInstance activity
- Shows on lock screen
- SkinImageView background
- Lyrics display (LyricView)
- SeekBar
- Controls: Previous, Play/Pause, Next
- Favorite, Queue
- DragDismissLayout (swipe up to unlock)
