using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using SQLite;

namespace MusicPlayer.Fixture;

/// <summary>
/// Generates a synthetic music library for scalability measurements.
///
/// Output layout:
///   &lt;out&gt;/data      → use as MUSICPLAYER_DATA_DIR (database, caches, logs, artist images)
///   &lt;out&gt;/library   → managed music folder referenced by the managed_folders row
///
/// The tracks are inserted straight into the SQLite database with the metadata
/// the UI needs (title / artist / album / genre). A placeholder file is created
/// for every track so the startup scanner keeps the rows (it deletes tracks
/// whose file disappeared) and does not rewrite the metadata (it only re-reads a
/// track when its size or modification time changed).
/// </summary>
public static class Program
{
    private static readonly string[] ArtistPrefixes =
    [
        "Aurora", "Neon", "Velvet", "Crystal", "Midnight", "Silver", "Golden", "Crimson",
        "Echo", "Lunar", "Solar", "Polar", "Amber", "Cobalt", "Onyx", "Ivory",
        "Static", "Paper", "Electric", "Quiet", "Northern", "Southern", "Paper", "Glass",
        "Wild", "Quiet", "Bright", "Hollow", "Iron", "Salt", "Rain", "Stone"
    ];

    private static readonly string[] ArtistSuffixes =
    [
        "Fields", "Harbour", "Foxes", "Machine", "Orchestra", "Rivers", "Lanterns", "Signal",
        "Society", "Mirage", "Garden", "Circuit", "Parade", "Museum", "Tigers", "Ravens",
        "Cathedral", "Highway", "Atlas", "Meridian", "Nomads", "Antennae", "Pilgrims", "Sisters"
    ];

    private static readonly string[] AlbumWords =
    [
        "Dawn", "Chapters", "Nocturne", "Harvest", "Transit", "Silhouette", "Ritual", "Drift",
        "Fracture", "Lantern", "Orbit", "Static", "Bloom", "Passage", "Vantage", "Meridian",
        "Ember", "Frequency", "Horizon", "Tide"
    ];

    private static readonly string[] Genres =
    [
        "Electronic", "Indie", "Hip-Hop", "Rock", "Ambient", "Jazz", "Pop", "Folk", "Soul", "Techno"
    ];

    public static int Main(string[] args)
    {
        try
        {
            return Run(args);
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"fixture failed: {ex}");
            return 1;
        }
    }

    private static int Run(string[] args)
    {
        string outputDir = GetOption(args, "--out") ?? Path.Combine(Path.GetTempPath(), "MusicPlayerFixture");
        int trackCount = int.TryParse(GetOption(args, "--tracks"), out int tracks) ? tracks : 5000;
        int artistCount = int.TryParse(GetOption(args, "--artists"), out int artists) ? artists : 0;
        bool withArt = !HasFlag(args, "--no-art");
        int extraFolders = int.TryParse(GetOption(args, "--extra-folders"), out int folders) ? folders : 0;

        if (artistCount <= 0)
            artistCount = Math.Max(1, tracks / 8);

        string dataDir = Path.Combine(outputDir, "data");
        string libraryDir = Path.Combine(outputDir, "library");
        string artistImagesDir = Path.Combine(dataDir, "ArtistImages");
        string albumArtDir = Path.Combine(dataDir, "FixtureAlbumArt");

        Directory.CreateDirectory(dataDir);
        Directory.CreateDirectory(libraryDir);
        Directory.CreateDirectory(artistImagesDir);
        Directory.CreateDirectory(albumArtDir);
        Directory.CreateDirectory(Path.Combine(dataDir, "Cache", "AlbumArt"));
        Directory.CreateDirectory(Path.Combine(dataDir, "Cache", "Lyrics"));
        Directory.CreateDirectory(Path.Combine(dataDir, "Logs"));

        var sw = Stopwatch.StartNew();
        var artistNames = BuildArtistNames(artistCount);
        var albumArtPaths = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        var trackRows = new List<FixtureTrack>(trackCount);
        for (int i = 0; i < trackCount; i++)
        {
            string artist = artistNames[i % artistNames.Count];
            int albumsForArtist = Math.Max(1, (int)Math.Ceiling(trackCount / (double)artistNames.Count) / 3);
            string album = $"{artist} — {AlbumWords[(i / albumsForArtist) % AlbumWords.Length]} {i / albumsForArtist + 1}";
            string title = $"Track {i + 1:00000}";
            string genre = Genres[i % Genres.Length];

            string albumDir = Path.Combine(libraryDir, Sanitize(artist), Sanitize(album));
            Directory.CreateDirectory(albumDir);
            string filePath = Path.Combine(albumDir, $"{i + 1:00000} {Sanitize(title)}.mp3");

            // A placeholder file: it must exist so the scanner does not delete
            // the row, and it must not be a parsable audio file so nothing tries
            // to read tags from it.
            byte[] payload = Encoding.ASCII.GetBytes($"MUSICPLAYER-FIXTURE-PLACEHOLDER {i}\n");
            File.WriteAllBytes(filePath, payload);

            var info = new FileInfo(filePath);
            string albumArtPath;
            if (!albumArtPaths.TryGetValue(album, out albumArtPath!))
            {
                albumArtPath = Path.Combine(albumArtDir, $"art_{albumArtPaths.Count:00000}.png");
                File.WriteAllBytes(albumArtPath, CreateImagePng(512, 512, (uint)(albumArtPaths.Count * 37 % 360), 0.55));
                albumArtPaths[album] = albumArtPath;
            }

            trackRows.Add(new FixtureTrack
            {
                FilePath = filePath,
                Title = title,
                Artist = artist,
                Album = album,
                AlbumArtist = artist,
                Genre = genre,
                Duration = 180_000 + (i % 120) * 1000,
                FileSize = info.Length,
                Bitrate = 320,
                SampleRate = 44100,
                Channels = 2,
                AlbumArtPath = albumArtPath,
                FolderPath = albumDir,
                DateAdded = new DateTimeOffset(info.LastWriteTime).ToUnixTimeSeconds(),
                DateModified = new DateTimeOffset(info.LastWriteTime).ToUnixTimeSeconds()
            });
        }

        // Artist images so the Artists grid never has to hit the network.
        foreach (string artist in artistNames)
        {
            string key = FixtureNameNormalizer.NormalizeKey(artist);
            if (string.IsNullOrEmpty(key)) continue;
            string path = Path.Combine(artistImagesDir, key + ".jpg");
            if (File.Exists(path)) continue;
            File.WriteAllBytes(path, CreateImagePng(256, 256, (uint)(key.GetHashCode(StringComparison.Ordinal) & 0xFFFF), 0.7));
        }

        string dbPath = Path.Combine(dataDir, "music.db");
        if (File.Exists(dbPath)) File.Delete(dbPath);

        SQLitePCL.Batteries_V2.Init();
        using (var db = new SQLiteConnection(dbPath))
        {
            db.CreateTable<FixtureTrack>();
            db.CreateTable<FixtureManagedFolder>();
            db.Insert(new FixtureManagedFolder
            {
                Path = libraryDir,
                Name = "Fixture Library",
                Enabled = true,
                AddedDate = DateTimeOffset.UtcNow.ToUnixTimeSeconds()
            });

            // Extra managed folders make the folder list big enough to sort.
            for (int i = 0; i < extraFolders; i++)
            {
                db.Insert(new FixtureManagedFolder
                {
                    Path = Path.Combine(libraryDir, $"Extra {i + 1}"),
                    Name = $"Extra Library {i + 1}",
                    Enabled = true,
                    AddedDate = DateTimeOffset.UtcNow.ToUnixTimeSeconds()
                });
            }

            db.BeginTransaction();
            try
            {
                db.InsertAll(trackRows);
                db.Commit();
            }
            catch
            {
                db.Rollback();
                throw;
            }
        }

        sw.Stop();
        Console.WriteLine($"fixture ready in {sw.ElapsedMilliseconds} ms");
        Console.WriteLine($"  data dir   : {dataDir}");        Console.WriteLine($"  library    : {libraryDir}");
        Console.WriteLine($"  tracks     : {trackRows.Count}");
        Console.WriteLine($"  artists    : {artistNames.Count}");
        Console.WriteLine($"  albums     : {albumArtPaths.Count}");
        Console.WriteLine($"  artist imgs: {Directory.GetFiles(artistImagesDir).Length}");
        Console.WriteLine($"  db         : {dbPath}");
        Console.WriteLine();
        Console.WriteLine($"set MUSICPLAYER_DATA_DIR=\"{dataDir}\" before starting MusicPlayer");
        return 0;
    }

    private static List<string> BuildArtistNames(int count)
    {
        var names = new List<string>(count);
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        int i = 0;
        while (names.Count < count)
        {
            string name = $"{ArtistPrefixes[i % ArtistPrefixes.Length]} {ArtistSuffixes[(i / ArtistPrefixes.Length) % ArtistSuffixes.Length]}";
            i++;
            if (i > ArtistPrefixes.Length * ArtistSuffixes.Length)
            {
                name = $"{name} {names.Count + 1}";
            }

            if (seen.Add(name)) names.Add(name);
            if (i > count * 20) break;
        }

        return names;
    }

    private static string Sanitize(string value)
    {
        var invalid = Path.GetInvalidFileNameChars();
        var sb = new StringBuilder(value.Length);
        foreach (char c in value)
            sb.Append(Array.IndexOf(invalid, c) >= 0 ? '_' : c);
        return sb.ToString();
    }

    /// <summary>Creates a small PNG without any imaging dependency.</summary>
    private static byte[] CreateImagePng(int width, int height, uint hue, double saturation)
    {
        var pixels = new byte[width * height * 4];
        for (int y = 0; y < height; y++)
        {
            for (int x = 0; x < width; x++)
            {
                double t = (double)(x + y) / (width + height);
                byte r = (byte)(40 + 200 * (0.5 + 0.5 * Math.Sin(hue * Math.PI / 180.0 + t * 6.0)) * saturation);
                byte g = (byte)(40 + 200 * (0.5 + 0.5 * Math.Sin(hue * Math.PI / 180.0 + t * 6.0 + 2.0)) * saturation);
                byte b = (byte)(40 + 200 * (0.5 + 0.5 * Math.Sin(hue * Math.PI / 180.0 + t * 6.0 + 4.0)) * saturation);
                int index = (y * width + x) * 4;
                pixels[index] = r;
                pixels[index + 1] = g;
                pixels[index + 2] = b;
                pixels[index + 3] = 255;
            }
        }

        return PngWriter.Encode(width, height, pixels);
    }

    private static string? GetOption(string[] args, string name)
    {
        for (int i = 0; i < args.Length - 1; i++)
            if (string.Equals(args[i], name, StringComparison.OrdinalIgnoreCase))
                return args[i + 1];
        return null;
    }

    private static bool HasFlag(string[] args, string name) =>
        args.Any(a => string.Equals(a, name, StringComparison.OrdinalIgnoreCase));
}
