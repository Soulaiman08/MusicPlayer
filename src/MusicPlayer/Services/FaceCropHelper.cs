using System;
using System.IO;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Windows.Graphics.Imaging;
using Windows.Media.FaceAnalysis;
using Windows.Storage.Streams;

namespace MusicPlayer.Services;

public static class FaceCropHelper
{
    private static FaceDetector? _faceDetector;
    private static bool _initialized;

    private static async Task<FaceDetector?> GetDetectorAsync()
    {
        if (_initialized) return _faceDetector;
        _initialized = true;

        try
        {
            if (FaceDetector.IsSupported)
            {
                _faceDetector = await FaceDetector.CreateAsync();
            }
        }
        catch { }

        return _faceDetector;
    }

    /// <summary>
    /// Processes raw image bytes, detects any human faces, and crops a square avatar
    /// perfectly centered on the artist's face/upper body with proper portrait headroom.
    /// Falls back to smart upper-third crop for tall portrait images when no frontal face is detected.
    /// </summary>
    public static async Task<byte[]> ProcessArtistImageAsync(byte[] inputBytes, int targetSize = 512)
    {
        if (inputBytes == null || inputBytes.Length == 0)
            return Array.Empty<byte>();

        try
        {
            // Load BitmapFrame from input bytes
            System.Windows.Media.Imaging.BitmapDecoder decoder;
            using (var inStream = new MemoryStream(inputBytes))
            {
                decoder = System.Windows.Media.Imaging.BitmapDecoder.Create(
                    inStream,
                    BitmapCreateOptions.PreservePixelFormat,
                    BitmapCacheOption.OnLoad);
            }

            var frame = decoder.Frames[0];
            int origW = frame.PixelWidth;
            int origH = frame.PixelHeight;

            if (origW <= 0 || origH <= 0)
                return inputBytes;

            // If already a square, just resize if necessary
            if (origW == origH)
            {
                return ResizeSquare(frame, targetSize);
            }

            // Detect faces using WinRT FaceDetector if available
            Rect? faceBounds = await DetectFaceBoundsAsync(inputBytes, origW, origH);

            // Compute the optimal square crop box
            Int32Rect cropRect = CalculateSmartSquareCrop(origW, origH, faceBounds);

            // Crop the frame
            var cropped = new CroppedBitmap(frame, cropRect);

            // Scale to targetSize x targetSize
            return ResizeSquare(cropped, targetSize);
        }
        catch
        {
            // Fallback: return original bytes if processing fails
            return inputBytes;
        }
    }

    private static async Task<Rect?> DetectFaceBoundsAsync(byte[] inputBytes, int origW, int origH)
    {
        try
        {
            var detector = await GetDetectorAsync();
            if (detector == null) return null;

            using var ras = new InMemoryRandomAccessStream();
            using (var writer = new DataWriter(ras.GetOutputStreamAt(0)))
            {
                writer.WriteBytes(inputBytes);
                await writer.StoreAsync();
            }

            var winDecoder = await Windows.Graphics.Imaging.BitmapDecoder.CreateAsync(ras);
            var origBitmap = await winDecoder.GetSoftwareBitmapAsync();

            SoftwareBitmap grayBitmap;
            if (origBitmap.BitmapPixelFormat != BitmapPixelFormat.Gray8)
            {
                grayBitmap = SoftwareBitmap.Convert(origBitmap, BitmapPixelFormat.Gray8);
            }
            else
            {
                grayBitmap = origBitmap;
            }

            var faces = await detector.DetectFacesAsync(grayBitmap);
            if (faces == null || faces.Count == 0) return null;

            // Compute union bounding box of all detected faces
            double minX = double.MaxValue, minY = double.MaxValue;
            double maxX = double.MinValue, maxY = double.MinValue;

            foreach (var face in faces)
            {
                var b = face.FaceBox;
                if (b.X < minX) minX = b.X;
                if (b.Y < minY) minY = b.Y;
                if (b.X + b.Width > maxX) maxX = b.X + b.Width;
                if (b.Y + b.Height > maxY) maxY = b.Y + b.Height;
            }

            return new Rect(minX, minY, Math.Max(1, maxX - minX), Math.Max(1, maxY - minY));
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    /// Computes the optimal square crop rectangle.
    /// </summary>
    public static Int32Rect CalculateSmartSquareCrop(int origW, int origH, Rect? faceBounds)
    {
        int squareSize = Math.Min(origW, origH);

        if (faceBounds.HasValue)
        {
            var fb = faceBounds.Value;
            double faceCenterX = fb.X + fb.Width / 2.0;
            double faceCenterY = fb.Y + fb.Height / 2.0;

            // In portrait photography, the eye-level / face center should be around 35-40% from top
            // of the circular frame, leaving headroom above the hair/crown.
            double desiredCropY = faceCenterY - (squareSize * 0.38);
            double desiredCropX = faceCenterX - (squareSize / 2.0);

            // Clamp crop box within image dimensions
            int cropX = (int)Math.Clamp(desiredCropX, 0, origW - squareSize);
            int cropY = (int)Math.Clamp(desiredCropY, 0, origH - squareSize);

            return new Int32Rect(cropX, cropY, squareSize, squareSize);
        }

        // --- FALLBACK HEURISTICS WHEN NO FRONTAL FACE IS DETECTED ---
        // 1. Tall / Portrait image (Height > Width):
        // The head and face of an artist are virtually always located in the upper 40% of the photo!
        // When centering vertically at 50%, the head gets pushed up and clipped by circular avatars.
        // Therefore, we crop the top square with a slight 5% breathing margin, ensuring the head,
        // face, sunglasses, or hair are never cut off!
        if (origH > origW)
        {
            int cropX = 0;
            // Place crop at top, with 0 to 5% offset
            int maxTopOffset = Math.Max(0, origH - squareSize);
            int cropY = (int)(maxTopOffset * 0.05);
            return new Int32Rect(cropX, cropY, squareSize, squareSize);
        }

        // 2. Wide / Landscape image (Width > Height):
        // Default to center crop horizontally
        int lCropX = (origW - squareSize) / 2;
        int lCropY = 0;
        return new Int32Rect(lCropX, lCropY, squareSize, squareSize);
    }

    private static byte[] ResizeSquare(BitmapSource source, int targetSize)
    {
        double scale = (double)targetSize / source.PixelWidth;

        BitmapSource scaled;
        if (Math.Abs(scale - 1.0) > 0.01)
        {
            var tb = new TransformedBitmap(source, new ScaleTransform(scale, scale));
            scaled = tb;
        }
        else
        {
            scaled = source;
        }

        using var outMs = new MemoryStream();
        var encoder = new JpegBitmapEncoder { QualityLevel = 92 };
        encoder.Frames.Add(System.Windows.Media.Imaging.BitmapFrame.Create(scaled));
        encoder.Save(outMs);
        return outMs.ToArray();
    }
}
