using System.Buffers.Binary;
using System.IO.Compression;
using System.Text;

namespace GTI.ModTools.Images;

public static class PngWriter
{
    private static readonly byte[] Signature = [137, 80, 78, 71, 13, 10, 26, 10];

    public static void WriteRgbaToFile(string outputPath, int width, int height, ReadOnlySpan<byte> rgba)
    {
        var directory = Path.GetDirectoryName(outputPath);
        if (!string.IsNullOrWhiteSpace(directory))
        {
            Directory.CreateDirectory(directory);
        }

        using var fs = File.Create(outputPath);
        WriteRgbaToStream(fs, width, height, rgba);
    }

    public static void WriteRgbaToStream(Stream output, int width, int height, ReadOnlySpan<byte> rgba)
    {
        if (width <= 0 || height <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(width), "PNG width/height must be > 0.");
        }

        var expectedLength = checked(width * height * 4);
        if (rgba.Length != expectedLength)
        {
            throw new ArgumentException($"RGBA length must be {expectedLength} bytes.", nameof(rgba));
        }

        output.Write(Signature);

        Span<byte> ihdr = stackalloc byte[13];
        BinaryPrimitives.WriteUInt32BigEndian(ihdr[0..4], (uint)width);
        BinaryPrimitives.WriteUInt32BigEndian(ihdr[4..8], (uint)height);
        ihdr[8] = 8;  // bit depth
        ihdr[9] = 6;  // RGBA
        ihdr[10] = 0; // compression
        ihdr[11] = 0; // filter
        ihdr[12] = 0; // interlace
        WriteChunk(output, "IHDR", ihdr);

        var scanlineBytes = checked(height * (1 + width * 4));
        var raw = new byte[scanlineBytes];
        var srcIndex = 0;
        var dstIndex = 0;
        var rowBytes = width * 4;

        for (var y = 0; y < height; y++)
        {
            raw[dstIndex++] = 0; // no filter
            rgba.Slice(srcIndex, rowBytes).CopyTo(raw.AsSpan(dstIndex, rowBytes));
            srcIndex += rowBytes;
            dstIndex += rowBytes;
        }

        byte[] compressed;
        using (var compressedBuffer = new MemoryStream())
        {
            using (var zlib = new ZLibStream(compressedBuffer, CompressionLevel.SmallestSize, leaveOpen: true))
            {
                zlib.Write(raw, 0, raw.Length);
            }

            compressed = compressedBuffer.ToArray();
        }

        WriteChunk(output, "IDAT", compressed);
        WriteChunk(output, "IEND", ReadOnlySpan<byte>.Empty);
    }

    private static void WriteChunk(Stream output, string type, ReadOnlySpan<byte> data)
    {
        Span<byte> lengthBytes = stackalloc byte[4];
        BinaryPrimitives.WriteUInt32BigEndian(lengthBytes, (uint)data.Length);
        output.Write(lengthBytes);

        var typeBytes = Encoding.ASCII.GetBytes(type);
        output.Write(typeBytes);
        output.Write(data);

        var crc = Crc32(typeBytes, data);
        Span<byte> crcBytes = stackalloc byte[4];
        BinaryPrimitives.WriteUInt32BigEndian(crcBytes, crc);
        output.Write(crcBytes);
    }

    private static uint Crc32(ReadOnlySpan<byte> type, ReadOnlySpan<byte> data)
    {
        uint crc = 0xFFFFFFFF;

        foreach (var b in type)
        {
            crc = UpdateCrc(crc, b);
        }

        foreach (var b in data)
        {
            crc = UpdateCrc(crc, b);
        }

        return crc ^ 0xFFFFFFFF;
    }

    private static uint UpdateCrc(uint crc, byte value)
    {
        crc ^= value;
        for (var i = 0; i < 8; i++)
        {
            var mask = (crc & 1) != 0 ? 0xEDB88320u : 0u;
            crc = (crc >> 1) ^ mask;
        }

        return crc;
    }
}
