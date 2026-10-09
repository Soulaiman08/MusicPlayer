# UI Screens Analysis

## Complete Screen Inventory

### Activities (38 total)

| # | Activity | Purpose | Theme |
|---|----------|---------|-------|
| 1 | WelcomeActivity | Splash/welcome with permissions | Welcome.Theme |
| 2 | MainActivity | Main screen with drawer, fragments, sliding panel | AppTheme |
| 3 | MusicPlayActivity | Full player screen | AppTheme.Transparent |
| 4 | ActivityAlbum | Album browsing | AppTheme |
| 5 | ActivityAlbumMusic | Songs in album | AppTheme |
| 6 | ActivityAlbumGetter | Album art search online | AppTheme |
| 7 | ActivityAlbumSave | Save album art | AppTheme |
| 8 | ActivityAudioEditor | Audio trim/clip editor | AppTheme |
| 9 | ActivityDeletedMusic | Deleted music management | AppTheme |
| 10 | ActivityDriveMode | Simplified drive UI | AppTheme.Drive |
| 11 | ActivityDriveRemind | Drive mode warning | AppTheme |
| 12 | ActivityDuplicatedFinder | Find duplicate files | AppTheme |
| 13 | ActivityEdit | Batch edit songs | AppTheme |
| 14 | ActivityEditTags | ID3 tag editor | AppTheme |
| 15 | ActivityEffectGroup | Sound effects grid | AppTheme |
| 16 | ActivityEqualizer | EQ + Volume tabs | AppTheme |
| 17 | ActivityHiddenFolders | Hidden folders list | AppTheme |
| 18 | ActivityHiddenFoldersAdd | Add hidden folders | AppTheme |
| 19 | ActivityLrcBrowser | Browse local lyrics | AppTheme |
| 20 | ActivityLrcNetSearch | Search lyrics online | AppTheme |
| 21 | ActivityLyricList | Lyric file management | AppTheme |
| 22 | ActivityMusicDirectory | Browse by folder | AppTheme |
| 23 | ActivityMusicQueue | Play queue | AppTheme |
| 24 | ActivityMusicSelect | Select songs for playlist | AppTheme |
| 25 | ActivityMusicSetEdit | Edit playlist contents | AppTheme |
| 26 | ActivityPlaylistEdit | Reorder playlists | AppTheme |
| 27 | ActivityPlaylistSelect | Select playlist | AppTheme |
| 28 | ActivitySearch | Music search | AppTheme |
| 29 | ActivitySetting | Settings | AppTheme |
| 30 | ActivityShortcutLauncher | Handle shortcuts | AppTheme.Empty |
| 31 | ActivitySleep | Sleep timer | AppTheme |
| 32 | ActivityStatusBarLyrics | Status bar lyrics config | AppTheme |
| 33 | ActivityTheme | Theme gallery | AppTheme |
| 34 | ActivityThemeEdit | Theme editor | AppTheme |
| 35 | ActivityWidget | Widget selection | AppTheme |
| 36 | LockActivity | Lock screen player | AppTheme.Lock |
| 37 | ScanMusicActivity | Music library scan | AppTheme |
| 38 | ScanSettingActivity | Scan folder selection | AppTheme |

### Key Layouts Per Screen

**Main Activity:** DrawerLayout > TranslucentBarLinearLayout > SlidingUpPanelLayout > FragmentContainer + SlidingPanel(236dp)

**Fragment Main:** CoordinatorLayout > AppBarLayout > Toolbar + NestedScrollView > NoScrollGridView + PlaylistHeader + PlaylistRecyclerView

**Fragment Library:** CoordinatorLayout > AppBarLayout > Toolbar + TabLayout(scrollable) + ViewPager2

**Full Player:** DragDismissLayout > FrameLayout(bg) > SkinImageView(full bg) > TranslucentBarFrameLayout(content)

**Settings:** ScrollView > 8 category sections with PreferenceItemView items

**Equalizer:** ViewPager2 with 2 tabs: EQ fragment + VOL fragment
