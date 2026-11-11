using System;
using System.IO;
using System.Windows.Media.Imaging;

namespace MusicPlayer.ImageLoading;

public static class ImageDecoder
{
    public const int ThumbnailPixelSize = 80;
    public const int MediumPixelSize = 220;
    public const int LargePixelSize = 512;
    public const int FullPixelSize = 1024;

    public static int GetTargetPixelSize(ImageQuality quality) => quality switch
    {
        ImageQuality.Thumbnail => ThumbnailPixelSize,
        ImageQuality.Medium => MediumPixelSize,
        ImageQuality.Large => LargePixelSize,
        ImageQuality.Full => FullPixelSize,
        _ => MediumPixelSize
    };

    public static BitmapSource? Decode(byte[]? data, ImageQuality quality)
    {
        if (data == null || data.Length == 0) return null;
        try
        {
            using var ms = new MemoryStream(data);
            return Decode(ms, quality);
        }
        catch
        {
            return null;
        }
    }

    public static BitmapSource? Decode(Stream? stream, ImageQuality quality)
    {
        if (stream == null) return null;
        try
        {
            int size = GetTargetPixelSize(quality);
            return DecodeSized(stream, size, size);
        }
        catch
        {
            return null;
        }
    }

    public static BitmapSource? DecodeFromFile(string? filePath, ImageQuality quality)
    {
        if (string.IsNullOrWhiteSpace(filePath) || !File.Exists(filePath)) return null;
        try
        {
            using var fs = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.Read);
            using var ms = new MemoryStream();
            fs.CopyTo(ms);
            ms.Position = 0;
            return Decode(ms, quality);
        }
        catch
        {
            return null;
        }
    }

    public static BitmapSource? DecodeSized(Stream stream, int pixelWidth, int pixelHeight)
    {
        try
        {
            var bitmap = new BitmapImage();
            bitmap.BeginInit();
            bitmap.CacheOption = BitmapCacheOption.OnLoad;
            bitmap.CreateOptions = BitmapCreateOptions.IgnoreColorProfile;
            bitmap.StreamSource = stream;
            if (pixelWidth > 0) bitmap.DecodePixelWidth = pixelWidth;
            if (pixelHeight > 0) bitmap.DecodePixelHeight = pixelHeight;
            bitmap.EndInit();
            bitmap.Freeze();
            return bitmap;
        }
        catch
        {
            return null;
        }
    }

    public static BitmapSource? DecodePreservingAspect(Stream stream, int maxDimension)
    {
        try
        {
            var bitmap = new BitmapImage();
            bitmap.BeginInit();
            bitmap.CacheOption = BitmapCacheOption.OnLoad;
            bitmap.CreateOptions = BitmapCreateOptions.IgnoreColorProfile;
            bitmap.StreamSource = stream;
            if (maxDimension > 0) bitmap.DecodePixelWidth = maxDimension;
            bitmap.EndInit();
            bitmap.Freeze();
            return bitmap;
        }
        catch
        {
            return null;
        }
    }

    public static byte[]? EncodeToBytes(BitmapSource? bitmap, bool isPng = false, int jpegQuality = 85)
    {
        if (bitmap == null) return null;
        try
        {
            using var ms = new MemoryStream();
            BitmapEncoder encoder = isPng
                ? new PngBitmapEncoder()
                : new JpegBitmapEncoder { QualityLevel = Math.Clamp(jpegQuality, 10, 100) };
            encoder.Frames.Add(BitmapFrame.Create(bitmap));
            encoder.Save(ms);
            return ms.ToArray();
        }
        catch
        {
            return null;
        }
    }
}

