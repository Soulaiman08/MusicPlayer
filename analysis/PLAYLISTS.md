# Playlists Analysis

## Playlist Management
- **Create:** dialog_new_playlist.xml
- **Rename:** Via context menu
- **Delete:** Via context menu
- **Add songs:** ActivityMusicSelect
- **Remove songs:** ActivityMusicSetEdit
- **Reorder:** ActivityPlaylistEdit (drag-and-drop)

## Default Playlists
- Favorite
- Default list

## Smart Playlists
- Recent Play (id: -3)
- Recent Add
- Most Play
- Configurable track limit

## Playlist Settings
- Add position: Top (0) or End (1) (default: End)
- Track limit: 0 = no limit
- Click add to queue: false by default

## Playlist UI
- Horizontal scrolling list on Home screen
- Add playlist button
- Playlist item: SquareRelativeLayout with album art + name + count
- Long-press for context menu (rename, delete, share)

## Queue
- ActivityMusicQueue
- Queue after searching: All songs (0) or Search results (1)
- Drag to reorder
- Remove from queue
- Save queue as playlist
- Clear queue
