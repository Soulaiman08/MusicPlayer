# Queue Analysis

## Queue Structure
- Maintained by main player controller (a6.v singleton)
- Current index tracking
- Shuffle state with random order
- Repeat state (off/all/single)

## Queue Features
- View queue (ActivityMusicQueue)
- Add to queue from search
- Remove from queue
- Drag to reorder
- Clear queue
- Save queue as playlist

## Queue Settings
- Queue after searching: "All songs" or "Search results"
- Default depends on version (>700 = search results)

## Shuffle Algorithm
- Standard shuffle with no repeat until all played
- Reset when new shuffle enabled
- Maintains original order for unshuffle

## Persistence
- Queue stored in SharedPreferences
- Restored on app restart
- Current position and index preserved
