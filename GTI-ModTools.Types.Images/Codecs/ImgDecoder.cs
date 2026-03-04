namespace GTI.ModTools.Images;

public static class ImgDecoder
{
    public static DecodedImage DecodeFile(string path, DecodeOptions? options = null)
    {
        var bytes = File.ReadAllBytes(path);
        return DecodeBytes(bytes, options ?? new DecodeOptions());
    }

    public static DecodedImage DecodeBytes(ReadOnlySpan<byte> bytes, DecodeOptions options)
    {
        var header = ImgHeader.Parse(bytes);
        var data = bytes[header.DataOffset..];

        return header.Format switch
        {
            ImgPixelFormat.Unknown1 => DecodeRgba8888Format1(header, data, options),
            ImgPixelFormat.Rgb8 => DecodeRgb8(header, data, options),
            ImgPixelFormat.Rgba8888 => DecodeRgba8888(header, data, options),
            ImgPixelFormat.Etc1 => DecodeEtc1(header, data, options),
            ImgPixelFormat.Unknown5 => DecodeEtc1A4(header, data, options),
            ImgPixelFormat.Xbgr1555 => DecodeXbgr1555(header, data, options),
            ImgPixelFormat.Unknown7 => DecodeLa4(header, data, options, allowSwizzle: false),
            ImgPixelFormat.Unknown8 => DecodeLa4(header, data, options, allowSwizzle: true),
            _ => throw new NotSupportedException($"Unsupported IMG format: 0x{(uint)header.Format:X}")
        };
    }

    private static DecodedImage DecodeRgb8(ImgHeader header, ReadOnlySpan<byte> data, DecodeOptions options)
    {
        var pixelCount = checked(header.Width * header.Height);
        var byteCount = checked(pixelCount * 3);
        EnsureDataLength(data, byteCount, header);

        var rgba = new byte[pixelCount * 4];
        var canSwizzle = options.UseSwizzle && Swizzle.CanSwizzle(header.Width, header.Height);

        for (var i = 0; i < pixelCount; i++)
        {
            var src = i * 3;
            var (x, y) = canSwizzle
                ? Swizzle.GetPixelCoordinates(i, header.Width, header.Height)
                : (i % header.Width, i / header.Width);

            var dstY = options.FlipVertical ? (header.Height - 1 - y) : y;
            var dst = (dstY * header.Width + x) * 4;

            var c0 = data[src];
            var c1 = data[src + 1];
            var c2 = data[src + 2];
            MapRgb24(c0, c1, c2, options.RgbOrder24, rgba, dst);
        }

        return new DecodedImage { Width = header.Width, Height = header.Height, RgbaPixels = rgba };
    }

    private static DecodedImage DecodeRgba8888(ImgHeader header, ReadOnlySpan<byte> data, DecodeOptions options)
    {
        var pixelCount = checked(header.Width * header.Height);
        var byteCount = checked(pixelCount * 4);
        EnsureDataLength(data, byteCount, header);

        var rgba = new byte[pixelCount * 4];
        var canSwizzle = options.UseSwizzle && Swizzle.CanSwizzle(header.Width, header.Height);

        for (var i = 0; i < pixelCount; i++)
        {
            var src = i * 4;
            var (x, y) = canSwizzle
                ? Swizzle.GetPixelCoordinates(i, header.Width, header.Height)
                : (i % header.Width, i / header.Width);

            var dstY = options.FlipVertical ? (header.Height - 1 - y) : y;
            var dst = (dstY * header.Width + x) * 4;

            var c0 = data[src];
            var c1 = data[src + 1];
            var c2 = data[src + 2];
            var c3 = data[src + 3];
            MapRgba32(c0, c1, c2, c3, options.RgbaOrder32, rgba, dst);
        }

        return new DecodedImage { Width = header.Width, Height = header.Height, RgbaPixels = rgba };
    }

    private static DecodedImage DecodeRgba8888Format1(ImgHeader header, ReadOnlySpan<byte> data, DecodeOptions options)
    {
        var pixelCount = checked(header.Width * header.Height);
        var byteCount = checked(pixelCount * 4);
        EnsureDataLength(data, byteCount, header);

        var rgba = new byte[pixelCount * 4];
        var canSwizzle = options.UseSwizzle && Swizzle.CanSwizzle(header.Width, header.Height);

        for (var i = 0; i < pixelCount; i++)
        {
            var src = i * 4;
            var (x, y) = canSwizzle
                ? Swizzle.GetPixelCoordinates(i, header.Width, header.Height)
                : (i % header.Width, i / header.Width);

            var dstY = options.FlipVertical ? (header.Height - 1 - y) : y;
            var dst = (dstY * header.Width + x) * 4;

            // Format 0x01 appears to be BGRA-ordered 32-bit data.
            MapRgba32(data[src], data[src + 1], data[src + 2], data[src + 3], ChannelOrder32.Bgra, rgba, dst);
        }

        return new DecodedImage { Width = header.Width, Height = header.Height, RgbaPixels = rgba };
    }

    private static DecodedImage DecodeEtc1(ImgHeader header, ReadOnlySpan<byte> data, DecodeOptions options)
    {
        var byteCount = checked(header.Width * header.Height / 2);
        EnsureDataLength(data, byteCount, header);

        var rgba = Etc1Decoder.DecodeEtc1(data[..byteCount], header.Width, header.Height);
        if (options.FlipVertical)
        {
            FlipVerticalInPlace(rgba, header.Width, header.Height);
        }

        return new DecodedImage { Width = header.Width, Height = header.Height, RgbaPixels = rgba };
    }

    private static DecodedImage DecodeEtc1A4(ImgHeader header, ReadOnlySpan<byte> data, DecodeOptions options)
    {
        var byteCount = checked(header.Width * header.Height);
        EnsureDataLength(data, byteCount, header);

        var rgba = Etc1Decoder.DecodeEtc1A4(data[..byteCount], header.Width, header.Height);
        if (options.FlipVertical)
        {
            FlipVerticalInPlace(rgba, header.Width, header.Height);
        }

        return new DecodedImage { Width = header.Width, Height = header.Height, RgbaPixels = rgba };
    }

    private static DecodedImage DecodeXbgr1555(ImgHeader header, ReadOnlySpan<byte> data, DecodeOptions options)
    {
        var pixelCount = checked(header.Width * header.Height);
        var byteCount = checked(pixelCount * 2);
        EnsureDataLength(data, byteCount, header);

        var rgba = new byte[pixelCount * 4];
        var canSwizzle = options.UseSwizzle && Swizzle.CanSwizzle(header.Width, header.Height);

        for (var i = 0; i < pixelCount; i++)
        {
            var src = i * 2;
            // Format 0x06 uses 16-bit big-endian words.
            var word = (ushort)((data[src] << 8) | data[src + 1]);

            var (x, y) = canSwizzle
                ? Swizzle.GetPixelCoordinates(i, header.Width, header.Height)
                : (i % header.Width, i / header.Width);

            var dstY = options.FlipVertical ? (header.Height - 1 - y) : y;
            var dst = (dstY * header.Width + x) * 4;

            var r = Expand5(word & 0x1F);
            var g = Expand5((word >> 5) & 0x1F);
            var b = Expand5((word >> 10) & 0x1F);

            rgba[dst] = r;
            rgba[dst + 1] = g;
            rgba[dst + 2] = b;
            rgba[dst + 3] = 255;
        }

        return new DecodedImage { Width = header.Width, Height = header.Height, RgbaPixels = rgba };
    }

    private static DecodedImage DecodeLa4(ImgHeader header, ReadOnlySpan<byte> data, DecodeOptions options, bool allowSwizzle)
    {
        var pixelCount = checked(header.Width * header.Height);
        var byteCount = pixelCount;
        EnsureDataLength(data, byteCount, header);

        var rgba = new byte[pixelCount * 4];
        var canSwizzle = allowSwizzle && options.UseSwizzle && Swizzle.CanSwizzle(header.Width, header.Height);

        for (var i = 0; i < pixelCount; i++)
        {
            var packed = data[i];
            var luminance = Expand4((packed >> 4) & 0x0F);
            var alpha = Expand4(packed & 0x0F);

            var (x, y) = canSwizzle
                ? Swizzle.GetPixelCoordinates(i, header.Width, header.Height)
                : (i % header.Width, i / header.Width);

            var dstY = options.FlipVertical ? (header.Height - 1 - y) : y;
            var dst = (dstY * header.Width + x) * 4;

            rgba[dst] = luminance;
            rgba[dst + 1] = luminance;
            rgba[dst + 2] = luminance;
            rgba[dst + 3] = alpha;
        }

        return new DecodedImage { Width = header.Width, Height = header.Height, RgbaPixels = rgba };
    }

    private static void EnsureDataLength(ReadOnlySpan<byte> data, int requiredBytes, ImgHeader header)
    {
        if (data.Length < requiredBytes)
        {
            throw new InvalidDataException(
                $"IMG pixel data truncated. Need {requiredBytes} bytes for {header.Width}x{header.Height} format 0x{(uint)header.Format:X}.");
        }
    }

    private static void MapRgb24(byte c0, byte c1, byte c2, ChannelOrder24 order, byte[] rgba, int dst)
    {
        switch (order)
        {
            case ChannelOrder24.Rgb:
                rgba[dst] = c0;
                rgba[dst + 1] = c1;
                rgba[dst + 2] = c2;
                break;
            case ChannelOrder24.Bgr:
                rgba[dst] = c2;
                rgba[dst + 1] = c1;
                rgba[dst + 2] = c0;
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(order), order, null);
        }

        rgba[dst + 3] = 255;
    }

    private static void MapRgba32(byte c0, byte c1, byte c2, byte c3, ChannelOrder32 order, byte[] rgba, int dst)
    {
        switch (order)
        {
            case ChannelOrder32.Rgba:
                rgba[dst] = c0;
                rgba[dst + 1] = c1;
                rgba[dst + 2] = c2;
                rgba[dst + 3] = c3;
                break;
            case ChannelOrder32.Argb:
                rgba[dst] = c1;
                rgba[dst + 1] = c2;
                rgba[dst + 2] = c3;
                rgba[dst + 3] = c0;
                break;
            case ChannelOrder32.Abgr:
                rgba[dst] = c3;
                rgba[dst + 1] = c2;
                rgba[dst + 2] = c1;
                rgba[dst + 3] = c0;
                break;
            case ChannelOrder32.Bgra:
                rgba[dst] = c2;
                rgba[dst + 1] = c1;
                rgba[dst + 2] = c0;
                rgba[dst + 3] = c3;
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(order), order, null);
        }
    }

    private static void FlipVerticalInPlace(byte[] rgba, int width, int height)
    {
        var stride = width * 4;
        var rowBuffer = new byte[stride];

        for (var y = 0; y < height / 2; y++)
        {
            var top = y * stride;
            var bottom = (height - 1 - y) * stride;

            Buffer.BlockCopy(rgba, top, rowBuffer, 0, stride);
            Buffer.BlockCopy(rgba, bottom, rgba, top, stride);
            Buffer.BlockCopy(rowBuffer, 0, rgba, bottom, stride);
        }
    }

    private static byte Expand5(int value) => (byte)((value << 3) | (value >> 2));

    private static byte Expand4(int value) => (byte)((value << 4) | value);
}
