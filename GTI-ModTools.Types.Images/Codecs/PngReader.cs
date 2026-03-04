using System.Buffers.Binary;
using System.IO.Compression;
using System.Text;

namespace GTI.ModTools.Images;

public static class PngReader
{
    private static readonly byte[] Signature = [137, 80, 78, 71, 13, 10, 26, 10];
    private static readonly (int XStart, int YStart, int XStep, int YStep)[] Adam7Passes =
    [
        (0, 0, 8, 8),
        (4, 0, 8, 8),
        (0, 4, 4, 8),
        (2, 0, 4, 4),
        (0, 2, 2, 4),
        (1, 0, 2, 2),
        (0, 1, 1, 2)
    ];

    public static DecodedImage ReadFromFile(string path)
    {
        using var stream = File.OpenRead(path);
        return ReadFromStream(stream);
    }

    public static DecodedImage ReadFromStream(Stream stream)
    {
        using var ms = new MemoryStream();
        stream.CopyTo(ms);
        var bytes = ms.ToArray();
        return Decode(bytes);
    }

    private static DecodedImage Decode(ReadOnlySpan<byte> bytes)
    {
        if (bytes.Length < 8 || !bytes[..8].SequenceEqual(Signature))
        {
            throw new InvalidDataException("Input is not a valid PNG file.");
        }

        var offset = 8;
        var width = 0;
        var height = 0;
        byte bitDepth = 0;
        byte colorType = 0;
        byte interlaceMethod = 0;
        var idat = new MemoryStream();

        while (offset + 12 <= bytes.Length)
        {
            var length = checked((int)BinaryPrimitives.ReadUInt32BigEndian(bytes.Slice(offset, 4)));
            offset += 4;

            if (offset + 4 + length + 4 > bytes.Length)
            {
                throw new InvalidDataException("PNG chunk length exceeds file size.");
            }

            var type = Encoding.ASCII.GetString(bytes.Slice(offset, 4));
            offset += 4;
            var chunkData = bytes.Slice(offset, length);
            offset += length;
            offset += 4; // skip CRC

            if (type == "IHDR")
            {
                width = checked((int)BinaryPrimitives.ReadUInt32BigEndian(chunkData[..4]));
                height = checked((int)BinaryPrimitives.ReadUInt32BigEndian(chunkData.Slice(4, 4)));
                bitDepth = chunkData[8];
                colorType = chunkData[9];
                if (chunkData[10] != 0 || chunkData[11] != 0)
                {
                    throw new NotSupportedException("PNG compression/filter method is unsupported.");
                }

                interlaceMethod = chunkData[12];
            }
            else if (type == "IDAT")
            {
                idat.Write(chunkData);
            }
            else if (type == "IEND")
            {
                break;
            }
        }

        if (width <= 0 || height <= 0)
        {
            throw new InvalidDataException("PNG missing valid IHDR chunk.");
        }

        if (bitDepth is not (8 or 16))
        {
            throw new NotSupportedException($"Only 8-bit or 16-bit PNG is supported (got bit depth {bitDepth}).");
        }

        var channels = colorType switch
        {
            0 => 1, // grayscale
            2 => 3, // RGB
            4 => 2, // grayscale + alpha
            6 => 4, // RGBA
            _ => throw new NotSupportedException($"PNG color type {colorType} is not supported.")
        };

        var bytesPerSample = bitDepth / 8;
        byte[] decompressed;
        using (var source = new MemoryStream(idat.ToArray()))
        using (var zlib = new ZLibStream(source, CompressionMode.Decompress))
        using (var outMs = new MemoryStream())
        {
            zlib.CopyTo(outMs);
            decompressed = outMs.ToArray();
        }

        var bytesPerPixel = checked(channels * bytesPerSample);
        var rgba = interlaceMethod switch
            {
                0 => DecodeNonInterlaced(decompressed, width, height, bytesPerPixel, colorType, bytesPerSample),
                1 => DecodeAdam7(decompressed, width, height, bytesPerPixel, colorType, bytesPerSample),
                _ => throw new NotSupportedException($"PNG interlace method {interlaceMethod} is not supported.")
            };

        return new DecodedImage
        {
            Width = width,
            Height = height,
            RgbaPixels = rgba
        };
    }

    private static byte[] DecodeNonInterlaced(ReadOnlySpan<byte> source, int width, int height, int bytesPerPixel, byte colorType, int bytesPerSample)
    {
        var stride = checked(width * bytesPerPixel);
        var expectedSize = checked(height * (1 + stride));
        if (source.Length < expectedSize)
        {
            throw new InvalidDataException("PNG image data is truncated.");
        }

        var rgba = new byte[width * height * 4];
        var prevRow = new byte[stride];
        var curRow = new byte[stride];
        var src = 0;

        for (var y = 0; y < height; y++)
        {
            var filter = source[src++];
            source.Slice(src, stride).CopyTo(curRow);
            src += stride;

            UnfilterRow(curRow, prevRow, bytesPerPixel, filter);

            for (var x = 0; x < width; x++)
            {
                var srcPx = x * bytesPerPixel;
                var dst = (y * width + x) * 4;
                MapPixelToRgba(curRow, srcPx, colorType, bytesPerSample, rgba, dst);
            }

            (prevRow, curRow) = (curRow, prevRow);
        }

        return rgba;
    }

    private static byte[] DecodeAdam7(ReadOnlySpan<byte> source, int width, int height, int bytesPerPixel, byte colorType, int bytesPerSample)
    {
        var rgba = new byte[width * height * 4];
        var src = 0;

        foreach (var pass in Adam7Passes)
        {
            var passWidth = GetPassSize(width, pass.XStart, pass.XStep);
            var passHeight = GetPassSize(height, pass.YStart, pass.YStep);
            if (passWidth == 0 || passHeight == 0)
            {
                continue;
            }

            var stride = checked(passWidth * bytesPerPixel);
            var prevRow = new byte[stride];
            var curRow = new byte[stride];

            for (var passY = 0; passY < passHeight; passY++)
            {
                if (src >= source.Length)
                {
                    throw new InvalidDataException("PNG image data is truncated.");
                }

                var filter = source[src++];
                if (src + stride > source.Length)
                {
                    throw new InvalidDataException("PNG image data is truncated.");
                }

                source.Slice(src, stride).CopyTo(curRow);
                src += stride;

                UnfilterRow(curRow, prevRow, bytesPerPixel, filter);

                var y = pass.YStart + passY * pass.YStep;
                for (var passX = 0; passX < passWidth; passX++)
                {
                    var x = pass.XStart + passX * pass.XStep;
                    var srcPx = passX * bytesPerPixel;
                    var dst = (y * width + x) * 4;
                    MapPixelToRgba(curRow, srcPx, colorType, bytesPerSample, rgba, dst);
                }

                (prevRow, curRow) = (curRow, prevRow);
            }
        }

        return rgba;
    }

    private static int GetPassSize(int fullSize, int start, int step)
    {
        return fullSize <= start ? 0 : (fullSize - start + step - 1) / step;
    }

    private static void MapPixelToRgba(ReadOnlySpan<byte> row, int srcPx, byte colorType, int bytesPerSample, byte[] rgba, int dst)
    {
        switch (colorType)
        {
            case 0:
            {
                var g = ReadSample(row, srcPx, bytesPerSample);
                rgba[dst] = g;
                rgba[dst + 1] = g;
                rgba[dst + 2] = g;
                rgba[dst + 3] = 255;
                break;
            }
            case 2:
                rgba[dst] = ReadSample(row, srcPx, bytesPerSample);
                rgba[dst + 1] = ReadSample(row, srcPx + bytesPerSample, bytesPerSample);
                rgba[dst + 2] = ReadSample(row, srcPx + bytesPerSample * 2, bytesPerSample);
                rgba[dst + 3] = 255;
                break;
            case 4:
            {
                var g = ReadSample(row, srcPx, bytesPerSample);
                rgba[dst] = g;
                rgba[dst + 1] = g;
                rgba[dst + 2] = g;
                rgba[dst + 3] = ReadSample(row, srcPx + bytesPerSample, bytesPerSample);
                break;
            }
            case 6:
                rgba[dst] = ReadSample(row, srcPx, bytesPerSample);
                rgba[dst + 1] = ReadSample(row, srcPx + bytesPerSample, bytesPerSample);
                rgba[dst + 2] = ReadSample(row, srcPx + bytesPerSample * 2, bytesPerSample);
                rgba[dst + 3] = ReadSample(row, srcPx + bytesPerSample * 3, bytesPerSample);
                break;
        }
    }

    private static byte ReadSample(ReadOnlySpan<byte> row, int offset, int bytesPerSample)
    {
        return bytesPerSample switch
        {
            1 => row[offset],
            // Downsample 16-bit PNG to 8-bit using the high byte.
            2 => row[offset],
            _ => throw new NotSupportedException($"Unsupported PNG sample size: {bytesPerSample}")
        };
    }

    private static void UnfilterRow(byte[] row, byte[] prevRow, int bytesPerPixel, byte filter)
    {
        switch (filter)
        {
            case 0: // None
                return;
            case 1: // Sub
                for (var i = bytesPerPixel; i < row.Length; i++)
                {
                    row[i] = unchecked((byte)(row[i] + row[i - bytesPerPixel]));
                }

                return;
            case 2: // Up
                for (var i = 0; i < row.Length; i++)
                {
                    row[i] = unchecked((byte)(row[i] + prevRow[i]));
                }

                return;
            case 3: // Average
                for (var i = 0; i < row.Length; i++)
                {
                    var left = i >= bytesPerPixel ? row[i - bytesPerPixel] : 0;
                    var up = prevRow[i];
                    row[i] = unchecked((byte)(row[i] + ((left + up) >> 1)));
                }

                return;
            case 4: // Paeth
                for (var i = 0; i < row.Length; i++)
                {
                    var left = i >= bytesPerPixel ? row[i - bytesPerPixel] : 0;
                    var up = prevRow[i];
                    var upLeft = i >= bytesPerPixel ? prevRow[i - bytesPerPixel] : 0;
                    row[i] = unchecked((byte)(row[i] + PaethPredictor(left, up, upLeft)));
                }

                return;
            default:
                throw new NotSupportedException($"Unsupported PNG filter type {filter}.");
        }
    }

    private static int PaethPredictor(int a, int b, int c)
    {
        var p = a + b - c;
        var pa = Math.Abs(p - a);
        var pb = Math.Abs(p - b);
        var pc = Math.Abs(p - c);
        if (pa <= pb && pa <= pc)
        {
            return a;
        }

        return pb <= pc ? b : c;
    }
}
