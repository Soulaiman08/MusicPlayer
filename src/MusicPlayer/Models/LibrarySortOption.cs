namespace MusicPlayer.Models;

public class LibrarySortOption
{
    public string Key { get; set; }
    public string DisplayName { get; set; }

    public LibrarySortOption(string key, string displayName)
    {
        Key = key;
        DisplayName = displayName;
    }
}
