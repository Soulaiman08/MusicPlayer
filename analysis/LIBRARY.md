# Library Analysis

## Scan System
- **Service:** MediaScanService
- **Activity:** ScanMusicActivity
- **Settings:** ScanSettingActivity (folder picker)

## Scan Filters
| Filter | Key | Default | Description |
|--------|-----|---------|-------------|
| Exclude short songs | pref_ignore_60seconds_music | false | Skip songs < threshold |
| Duration threshold | pref_exclude_music_duration | 60000ms | Min duration |
| Size filter | pref_exclude_music_by_size | true | Enable size filter |
| Size threshold | pref_exclude_music_size | 51200KB | Min file size |
| Exclude ringtones | pref_ignore_rington | false | Skip ringtone files |

## Library Views
- Songs (list/grid)
- Albums (list/grid)
- Artists
- Genres
- Folders/Directories
- Playlists

## Folder Browsing
- ActivityMusicDirectory: Browse by folder structure
- Internal/SD card navigation
- Folder exclusion via hidden folders

## Duplicate Finder
- ActivityDuplicatedFinder
- Scans for duplicate tracks
- Shows progress during scan
- Select all/individual
- Batch delete

## Hidden Folders
- ActivityHiddenFolders: Manage hidden folders
- ActivityHiddenFoldersAdd: Add folders to hide
- Hide music from specific directories
- Show/hide in settings

## Sorting
- Per-section sort (name, date, etc.)
- Reverse sort option
- Album sort by album name
- Artist sort by artist name
- Folder sort by name
