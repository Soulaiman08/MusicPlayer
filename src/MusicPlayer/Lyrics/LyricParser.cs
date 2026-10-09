using System;
using System.IO;
using System.Text.RegularExpressions;

namespace MusicPlayer.Lyrics;

public class LyricParser
{
    private static readonly Regex LrcRegex = new(@"\[(\d{1,3}):(\d{2})(?:\.(\d{1,3}))?\]", RegexOptions.Compiled);
    private static readonly Regex LrcTagRegex = new(@"\[([a-zA-Z]+):(.*)\]", RegexOptions.Compiled);
    
    public string Title { get; private set; } = string.Empty;
    public string Artist { get; private set; } = string.Empty;
    public string Album { get; private set; } = string.Empty;
    public List<LyricLine> Lines { get; private set; } = new();
    
    public bool HasTimestamps => Lines.Any(l => l.Time > TimeSpan.Zero || Lines.Count > 1);
    
    public void Parse(string content)
    {
        Lines.Clear();
        Title = string.Empty;
        Artist = string.Empty;
        Album = string.Empty;
        
        foreach (var line in content.Split('\n'))
        {
            var trimmed = line.Trim();
            if (string.IsNullOrEmpty(trimmed)) continue;
            
            var tagMatch = LrcTagRegex.Match(trimmed);
            if (tagMatch.Success)
            {
                var key = tagMatch.Groups[1].Value.ToLower();
                var value = tagMatch.Groups[2].Value.Trim();
                switch (key)
                {
                    case "ti": Title = value; break;
                    case "ar": Artist = value; break;
                    case "al": Album = value; break;
                }
                continue;
            }
            
            var matches = LrcRegex.Matches(trimmed);
            if (matches.Count > 0)
            {
                var text = LrcRegex.Replace(trimmed, "").Trim();
                foreach (Match match in matches)
                {
                    int min = int.Parse(match.Groups[1].Value);
                    int sec = int.Parse(match.Groups[2].Value);
                    var fraction = match.Groups[3].Value;
                    int ms = fraction.Length switch
                    {
                        0 => 0,
                        1 => int.Parse(fraction) * 100,
                        2 => int.Parse(fraction) * 10,
                        _ => int.Parse(fraction[..3])
                    };
                    
                    var time = new TimeSpan(0, 0, min, sec, ms);
                    Lines.Add(new LyricLine(time, text));
                }
            }
            else
            {
                Lines.Add(new LyricLine(TimeSpan.Zero, trimmed));
            }
        }
        
        Lines = Lines.OrderBy(l => l.Time).ToList();

        var seen = new HashSet<TimeSpan>();
        for (int i = Lines.Count - 1; i >= 0; i--)
        {
            if (!seen.Add(Lines[i].Time))
            {
                Lines.RemoveAt(i);
            }
        }
    }
    
    public void LoadFromFile(string filePath)
    {
        if (!File.Exists(filePath)) return;
        try
        {
            Parse(File.ReadAllText(filePath));
        }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }
    
    public LyricLine? GetLineAtTime(TimeSpan currentTime, int offsetMs = 0)
    {
        var offset = TimeSpan.FromMilliseconds(offsetMs);
        var adjusted = currentTime + offset;
        
        LyricLine? result = null;
        foreach (var line in Lines)
        {
            if (line.Time <= adjusted)
                result = line;
            else
                break;
        }
        return result;
    }
    
    public int GetLineIndex(TimeSpan currentTime, int offsetMs = 0)
    {
        var offset = TimeSpan.FromMilliseconds(offsetMs);
        var adjusted = currentTime + offset;
        
        for (int i = Lines.Count - 1; i >= 0; i--)
        {
            if (Lines[i].Time <= adjusted)
                return i;
        }
        return 0;
    }
    
    public static LyricParser? FindLyricForTrack(string trackPath)
    {
        var dir = Path.GetDirectoryName(trackPath);
        var name = Path.GetFileNameWithoutExtension(trackPath);
        
        if (string.IsNullOrEmpty(dir)) return null;
        
        // Same directory + same base name. LRC remains the first local match.
        string[] extensions = { ".lrc", ".txt" };
        foreach (var ext in extensions)
        {
            var lrcPath = Path.Combine(dir, name + ext);
            if (File.Exists(lrcPath))
            {
                var parser = new LyricParser();
                parser.LoadFromFile(lrcPath);
                return parser;
            }
        }
        
        return null;
    }
}
