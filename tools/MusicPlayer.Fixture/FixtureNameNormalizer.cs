using System.Text.RegularExpressions;

namespace MusicPlayer.Fixture;

/// <summary>
/// Mirror of <c>MusicPlayer.ImageLoading.ArtistImageProvider.NormalizeKey</c>.
///
/// The fixture tool is deliberately independent of the WPF application, so this
/// copy has to stay in sync with the application implementation.
/// <c>FixtureNameNormalizerTests</c> in the test project asserts that both
/// produce identical results for a wide range of artist names.
/// </summary>
public static class FixtureNameNormalizer
{
    public static string NormalizeKey(string artist)
    {
        if (string.IsNullOrWhiteSpace(artist)) return string.Empty;
        var s = artist.Trim().ToLowerInvariant();
        s = Regex.Replace(s, @"\s*feat\.?.*$", "", RegexOptions.IgnoreCase);
        s = Regex.Replace(s, @"\s*ft\.?.*$", "", RegexOptions.IgnoreCase);
        s = Regex.Replace(s, @"\s*,.*$", "");
        s = Regex.Replace(s, @"\s*&.*$", "");
        s = Regex.Replace(s, @"[^a-z0-9\u0600-\u06FF\s]", "");
        s = Regex.Replace(s, @"\s+", "_").Trim('_');
        return s;
    }
}
