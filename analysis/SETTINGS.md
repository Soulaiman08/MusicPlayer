# Settings Analysis

## Settings Architecture
- **Activity:** ActivitySetting
- **Storage:** SharedPreferences (music_preference)
- **UI:** Custom PreferenceItemView (NOT standard Android Preference)

## Settings Categories

### 1. Normal Settings
| Setting | Key | Default | Type |
|---------|-----|---------|------|
| Use 10-band EQ | use_ten_band | true | Toggle |
| Show hidden folders | show_hidden_folders | true | Toggle |
| Show directories | show_directory | true | Toggle |
| Dark mode | (theme toggle) | - | Toggle |
| Show quality logo | show_quality_logo | false | Toggle |
| Show forward/backward | show_forward_backward | false | Toggle |
| Time forward/backward | time_forward_backward | 15s | Options: 5,10,15,20,30,60 |
| Queue for searching | queue_for_searching | 0/1 | Options |
| Show shuffle button | show_shuffle_button | true | Toggle |
| Accent color | (color picker) | #ff4995f3 | Color |
| Library order | preference_library_order | - | Reorder |
| Keep alive background | keep_alive_background | - | Button |
| Find duplicate | music_find_duplicate | - | Button |

### 2. Lyrics Settings
| Setting | Key | Default | Type |
|---------|-----|---------|------|
| Desktop lyrics | show_desktop_lyrics | false | Toggle |
| Bluetooth lyric | bluetooth_lyric | true | Toggle |
| Status bar lyrics | sbar_lyric | - | Button |

### 3. Audio Settings
| Setting | Key | Default | Type |
|---------|-----|---------|------|
| Shake to change | preference_shake_change_music | false | Toggle |
| Swipe to change | swipe_change_songs | true | Toggle |
| Simultaneous play | simultaneous_play | false | Toggle |
| Volume fade | preference_volume_fade | false | Toggle |
| Gapless playback | gapless_playback | true | Toggle |
| Crossfade duration | preference_fade_duration | 3000ms | Slider |
| Track click operation | preference_track_click_operation | false | Toggle |
| Replay song | preference_replay_song | false | Toggle |
| Shake level | shake_level | 0.5 | Slider |

### 4. ReplayGain Settings
| Setting | Key | Default | Type |
|---------|-----|---------|------|
| RG mode | replay_gain_mode | 0 | Options: None/Track/Album |
| RG preamp | replay_gain_preamp | - | Button |

### 5. Playlist Settings
| Setting | Key | Default | Type |
|---------|-----|---------|------|
| Click add queue | preference_click_add_queue | false | Toggle |
| Add position | preference_playlist_add_position | 1 (end) | Options: Top/End |
| Track limit | preference_playlist_track_limit | 0 | Button |

### 6. Notification Settings
| Setting | Key | Default | Type |
|---------|-----|---------|------|
| Use notification | use_notification_bar | false | Toggle |
| Classic notification | old_notification | false | Toggle |
| Color notification | color_notification | true | Toggle |

### 7. Lock Screen Settings
| Setting | Key | Default | Type |
|---------|-----|---------|------|
| Lock screen | preference_lock_screen | true | Toggle |
| Time format | lock_time_format | 2 (auto) | Options: 12h/24h/Auto |
| Lock background | lock_background | 1 | Options: Theme/Artwork |

### 8. Headset Settings
| Setting | Key | Default | Type |
|---------|-----|---------|------|
| Play when inserted | preference_headset_in_play | false | Toggle |
| Pause when unplugged | preference_headset_out_stop | true | Toggle |
| BT auto start | preference_bluetooth_auto_start | false | Toggle |
| BT auto stop | preference_bluetooth_auto_stop | true | Toggle |
| Headset control | preference_headset_control_allow | true | Toggle |

### 9. Others
| Setting | Key | Default | Type |
|---------|-----|---------|------|
| Use English | preference_use_english | false | Toggle |
| Hide update reminder | hide_update_reminder | - | Toggle |
| Check update | check_for_update | - | Button |
| Share | share_this_app | - | Button |
| Rate | adv_rate_for_us | - | Button |
| Stop ads | stop_ads | - | Button |
