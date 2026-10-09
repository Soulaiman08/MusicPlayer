# Metadata Analysis

## Music Entity Fields
| Field | Type | Description |
|-------|------|-------------|
| id | int | Database ID |
| title | String | Song title |
| artist | String | Artist name |
| album | String | Album name |
| duration | long | Duration in ms |
| trackNumber | int | Track number |
| albumPath | String | Album art path |
| size | long | File size in bytes |
| dateAdded | long | Date added timestamp |
| genre | String | Genre |
| dateModified | long | Date modified timestamp |
| playCount | int | Play count |
| isFavorite | boolean | Favorite flag |
| lyricPath | String | Lyric file path |
| quality | int | Audio quality indicator |
| isHidden | boolean | Hidden flag |
| filePath | String | Full file path |

## MusicSet Entity (Playlist/Album/Artist)
| Field | Type | Description |
|-------|------|-------------|
| id | int | Set ID (-1=default, -3=recent play, -4=albums, -5=genres, -6=artists, -8=directories) |
| name | String | Name |
| description | String | Description |
| musicCount | int | Track count |
| albumCount | int | Album count |
| albumId | long | Album artwork ID |
| date | long | Date |
| albumNetPath | String | Network album art URL |
| sort | int | Sort order |
| artist | String | Artist name |

## Tag Editor (ActivityEditTags)
- Editable fields: Title, Artist, Album, Genre
- Save writes back to file ID3 tags
- Confirmation dialog on back-press

## Album Art
- Embedded artwork extraction
- Network artwork download (ActivityAlbumGetter)
- Crop support (UCrop)
- Save to storage (ActivityAlbumSave)
- Auto skin (artwork as background)
