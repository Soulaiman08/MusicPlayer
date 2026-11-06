using System.IO;
using System.Resources;
using System.Windows;

namespace MusicPlayer.Utilities;

/// <summary>
/// Reads WPF application resources directly from the embedded .g.resources file.
/// This avoids Release-only failures of Application.GetResourceStream for some
/// resource files while keeping the same resource names.
/// </summary>
public static class PackResourceLoader
{
    public static Stream? GetStream(string uri)
    {
        if (string.IsNullOrWhiteSpace(uri)) return null;
        if (!uri.StartsWith("pack:", StringComparison.OrdinalIgnoreCase)) return null;

        string path = uri;
        int idx = path.IndexOf(",,,/", StringComparison.Ordinal);
        if (idx >= 0) path = path.Substring(idx + 4);
        path = path.TrimStart('/');
        path = path.ToLowerInvariant();
        if (string.IsNullOrEmpty(path)) return null;

        try
        {
            var asm = Application.ResourceAssembly;
            if (asm == null) return null;
            using var rs = asm.GetManifestResourceStream(asm.GetName().Name + ".g.resources");
            if (rs == null) return null;
            using var reader = new ResourceReader(rs);
            foreach (System.Collections.DictionaryEntry e in reader)
            {
                if (string.Equals(e.Key?.ToString(), path, StringComparison.OrdinalIgnoreCase))
                {
                    if (e.Value is Stream s)
                    {
                        if (s.CanSeek)
                        {
                            s.Position = 0;
                            return s;
                        }
                        var ms = new MemoryStream();
                        s.CopyTo(ms);
                        ms.Position = 0;
                        return ms;
                    }
                }
            }
        }
        catch
        {
        }
        return null;
    }
}
