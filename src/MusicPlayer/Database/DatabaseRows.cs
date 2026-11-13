namespace MusicPlayer.Database;

/// <summary>
/// Projection used to build the album cards in a single pass over the tracks
/// table (one representative track per album).
/// </summary>
internal sealed class AlbumCardRow
{
    public int Id { get; set; }
    public string Album { get; set; } = string.Empty;
    public string Artist { get; set; } = string.Empty;
    public int TrackNumber { get; set; }
    public string Title { get; set; } = string.Empty;
    public string FilePath { get; set; } = string.Empty;
    public string AlbumArtPath { get; set; } = string.Empty;
    public long Duration { get; set; }
    public int Year { get; set; }
    public long FileSize { get; set; }
    public long DateAdded { get; set; }
}

/// <summary>Projection for "which artist owns this album" (most tracks wins).</summary>
internal sealed class AlbumArtistRow
{
    public string Artist { get; set; } = string.Empty;
    public int TrackCount { get; set; }
}

/// <summary>Projection for the per-folder aggregates.</summary>
internal sealed class FolderAggregateRow
{
    public string FilePath { get; set; } = string.Empty;
    public long Duration { get; set; }
    public int Year { get; set; }
    public long DateAdded { get; set; }
}

/// <summary>Projection for the artist / genre cards (with their aggregates).</summary>
internal sealed class GroupCardRow
{
    public string Name { get; set; } = string.Empty;
    public string Album { get; set; } = string.Empty;
    public long Duration { get; set; }
    public int Year { get; set; }
    public long DateAdded { get; set; }
}

/// <summary>The information of a managed folder that the folder list shows/sorts by.</summary>
public sealed class FolderAggregate
{
    public int TrackCount { get; set; }
    public long TotalDuration { get; set; }
    public int NewestYear { get; set; }
    public long NewestDateAdded { get; set; }
}
