namespace MusicPlayer.Models;

public enum PlayMode
{
    Order = 0,
    LoopAll = 1,
    Shuffle = 2,
    Single = 3
}

public enum SortField
{
    Title,
    Artist,
    Album,
    DateAdded,
    DateModified,
    Duration,
    PlayCount
}

public enum SortDirection
{
    Ascending,
    Descending
}
