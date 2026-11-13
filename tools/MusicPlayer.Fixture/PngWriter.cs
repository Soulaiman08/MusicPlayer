using System;
using System.IO;
using System.IO.Compression;

namespace MusicPlayer.Fixture;

/// <summary>
/// Minimal PNG encoder (8-bit RGBA) so the fixture can produce real image files
/// without pulling an imaging dependency into the tool.
/// </summary>
internal static class PngWriter
{
    public static byte[] Encode(int width, int height, byte[] rgba)
    {
        using var output = new MemoryStream();

        output.Write(new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A });

        byte[] ihdr = new byte[13];
        WriteInt32(ihdr, 0, width);
        WriteInt32(ihdr, 4, height);
        ihdr[8] = 8;  // bit depth
        ihdr[9] = 6;  // colour type: truecolour with alpha
        ihdr[10] = 0; // compression
        ihdr[11] = 0; // filter
        ihdr[12] = 0; // interlace
        WriteChunk(output, "IHDR", ihdr);

        byte[] raw = new byte[(width * 4 + 1) * height];
        int offset = 0;
        for (int y = 0; y < height; y++)
        {
            raw[offset++] = 0; // filter type: none
            Buffer.BlockCopy(rgba, y * width * 4, raw, offset, width * 4);
            offset += width * 4;
        }

        byte[] compressed;
        using (var deflate = new MemoryStream())
        {
            using (var zlib = new ZLibStream(deflate, CompressionLevel.Fastest, leaveOpen: true))
            {
                zlib.Write(raw, 0, raw.Length);
            }

            compressed = deflate.ToArray();
        }

        WriteChunk(output, "IDAT", compressed);
        WriteChunk(output, "IEND", Array.Empty<byte>());

        return output.ToArray();
    }

    private static void WriteChunk(Stream stream, string type, byte[] data)
    {
        byte[] length = new byte[4];
        WriteInt32(length, 0, data.Length);
        stream.Write(length, 0, 4);

        byte[] typeBytes = new byte[4];
        for (int i = 0; i < 4; i++) typeBytes[i] = (byte)type[i];
        stream.Write(typeBytes, 0, 4);
        stream.Write(data, 0, data.Length);

        uint crc = Crc32(typeBytes, data);
        byte[] crcBytes = new byte[4];
        WriteInt32(crcBytes, 0, unchecked((int)crc));
        stream.Write(crcBytes, 0, 4);
    }

    private static void WriteInt32(byte[] buffer, int offset, int value)
    {
        buffer[offset] = (byte)((value >> 24) & 0xFF);
        buffer[offset + 1] = (byte)((value >> 16) & 0xFF);
        buffer[offset + 2] = (byte)((value >> 8) & 0xFF);
        buffer[offset + 3] = (byte)(value & 0xFF);
    }

    private static readonly uint[] CrcTable = BuildCrcTable();

    private static uint[] BuildCrcTable()
    {
        var table = new uint[256];
        for (uint n = 0; n < 256; n++)
        {
            uint c = n;
            for (int k = 0; k < 8; k++)
                c = (c & 1) != 0 ? 0xEDB88320u ^ (c >> 1) : c >> 1;
            table[n] = c;
        }

        return table;
    }

    private static uint Crc32(byte[] first, byte[] second)
    {
        uint c = 0xFFFFFFFFu;
        foreach (byte b in first) c = CrcTable[(c ^ b) & 0xFF] ^ (c >> 8);
        foreach (byte b in second) c = CrcTable[(c ^ b) & 0xFF] ^ (c >> 8);
        return c ^ 0xFFFFFFFFu;
    }
}
