using System.Buffers.Binary;

namespace GTI.ModTools.Images;

public static class ImgEncoder
{
    public static byte[] Encode(DecodedImage image, ImgPixelFormat outputFormat, DecodeOptions options)
    {
        if (image.Width <= 0 || image.Height <= 0)
        {
            throw new InvalidDataException($"Invalid image dimensions: {image.Width}x{image.Height}");
        }

        if (image.Width % 4 != 0 || image.Height % 4 != 0)
        {
            throw new InvalidDataException(
                $"IMG requires dimensions to be multiples of 4. Got {image.Width}x{image.Height}.");
        }

        var pixelData = outputFormat switch
        {
            ImgPixelFormat.Unknown1 => EncodeRgba8888Format1(image, options),
            ImgPixelFormat.Rgb8 => EncodeRgb8(image, options),
            ImgPixelFormat.Rgba8888 => EncodeRgba8888(image, options),
            ImgPixelFormat.Unknown7 => EncodeLa4(image, options, allowSwizzle: false),
            ImgPixelFormat.Unknown8 => EncodeLa4(image, options, allowSwizzle: true),
            _ => throw new NotSupportedException($"Encoding to IMG format 0x{(uint)outputFormat:X} is not supported.")
        };

        var pixelBits = outputFormat switch
        {
            ImgPixelFormat.Rgb8 => 0x18,
            ImgPixelFormat.Rgba8888 or ImgPixelFormat.Unknown1 => 0x20,
            ImgPixelFormat.Unknown7 or ImgPixelFormat.Unknown8 => 0x08,
            _ => throw new NotSupportedException($"Cannot determine pixel bits for IMG format 0x{(uint)outputFormat:X}.")
        };
        var bytes = new byte[0x80 + pixelData.Length];
        WriteHeader(bytes, outputFormat, image.Width, image.Height, pixelBits, 0x80);
        Buffer.BlockCopy(pixelData, 0, bytes, 0x80, pixelData.Length);
        return bytes;
    }

    private static byte[] EncodeRgb8(DecodedImage image, DecodeOptions options)
    {
        var pixelCount = checked(image.Width * image.Height);
        var output = new byte[pixelCount * 3];
        var swizzle = options.UseSwizzle && Swizzle.CanSwizzle(image.Width, image.Height);

        for (var i = 0; i < pixelCount; i++)
        {
            var (x, y) = swizzle
                ? Swizzle.GetPixelCoordinates(i, image.Width, image.Height)
                : (i % image.Width, i / image.Width);

            var srcY = options.FlipVertical ? (image.Height - 1 - y) : y;
            var src = (srcY * image.Width + x) * 4;
            var dst = i * 3;

            var r = image.RgbaPixels[src];
            var g = image.RgbaPixels[src + 1];
            var b = image.RgbaPixels[src + 2];

            if (options.RgbOrder24 == ChannelOrder24.Rgb)
            {
                output[dst] = r;
                output[dst + 1] = g;
                output[dst + 2] = b;
            }
            else
            {
                output[dst] = b;
                output[dst + 1] = g;
                output[dst + 2] = r;
            }
        }

        return output;
    }

    private static byte[] EncodeRgba8888(DecodedImage image, DecodeOptions options)
    {
        var pixelCount = checked(image.Width * image.Height);
        var output = new byte[pixelCount * 4];
        var swizzle = options.UseSwizzle && Swizzle.CanSwizzle(image.Width, image.Height);

        for (var i = 0; i < pixelCount; i++)
        {
            var (x, y) = swizzle
                ? Swizzle.GetPixelCoordinates(i, image.Width, image.Height)
                : (i % image.Width, i / image.Width);

            var srcY = options.FlipVertical ? (image.Height - 1 - y) : y;
            var src = (srcY * image.Width + x) * 4;
            var dst = i * 4;

            var r = image.RgbaPixels[src];
            var g = image.RgbaPixels[src + 1];
            var b = image.RgbaPixels[src + 2];
            var a = image.RgbaPixels[src + 3];

            switch (options.RgbaOrder32)
            {
                case ChannelOrder32.Rgba:
                    output[dst] = r;
                    output[dst + 1] = g;
                    output[dst + 2] = b;
                    output[dst + 3] = a;
                    break;
                case ChannelOrder32.Argb:
                    output[dst] = a;
                    output[dst + 1] = r;
                    output[dst + 2] = g;
                    output[dst + 3] = b;
                    break;
                case ChannelOrder32.Abgr:
                    output[dst] = a;
                    output[dst + 1] = b;
                    output[dst + 2] = g;
                    output[dst + 3] = r;
                    break;
                case ChannelOrder32.Bgra:
                    output[dst] = b;
                    output[dst + 1] = g;
                    output[dst + 2] = r;
                    output[dst + 3] = a;
                    break;
                default:
                    throw new ArgumentOutOfRangeException(nameof(options.RgbaOrder32), options.RgbaOrder32, null);
            }
        }

        return output;
    }

    private static byte[] EncodeRgba8888Format1(DecodedImage image, DecodeOptions options)
    {
        var pixelCount = checked(image.Width * image.Height);
        var output = new byte[pixelCount * 4];
        var swizzle = options.UseSwizzle && Swizzle.CanSwizzle(image.Width, image.Height);

        for (var i = 0; i < pixelCount; i++)
        {
            var (x, y) = swizzle
                ? Swizzle.GetPixelCoordinates(i, image.Width, image.Height)
                : (i % image.Width, i / image.Width);

            var srcY = options.FlipVertical ? (image.Height - 1 - y) : y;
            var src = (srcY * image.Width + x) * 4;
            var dst = i * 4;

            var r = image.RgbaPixels[src];
            var g = image.RgbaPixels[src + 1];
            var b = image.RgbaPixels[src + 2];
            var a = image.RgbaPixels[src + 3];

            // Format 0x01 is BGRA-ordered 32-bit data.
            output[dst] = b;
            output[dst + 1] = g;
            output[dst + 2] = r;
            output[dst + 3] = a;
        }

        return output;
    }

    private static byte[] EncodeLa4(DecodedImage image, DecodeOptions options, bool allowSwizzle)
    {
        var pixelCount = checked(image.Width * image.Height);
        var output = new byte[pixelCount];
        var swizzle = allowSwizzle && options.UseSwizzle && Swizzle.CanSwizzle(image.Width, image.Height);

        for (var i = 0; i < pixelCount; i++)
        {
            var (x, y) = swizzle
                ? Swizzle.GetPixelCoordinates(i, image.Width, image.Height)
                : (i % image.Width, i / image.Width);

            var srcY = options.FlipVertical ? (image.Height - 1 - y) : y;
            var src = (srcY * image.Width + x) * 4;

            var r = image.RgbaPixels[src];
            var g = image.RgbaPixels[src + 1];
            var b = image.RgbaPixels[src + 2];
            var a = image.RgbaPixels[src + 3];

            var luminance = (byte)((77 * r + 150 * g + 29 * b + 128) >> 8);
            var luminance4 = QuantizeTo4(luminance);
            var alpha4 = QuantizeTo4(a);
            output[i] = (byte)((luminance4 << 4) | alpha4);
        }

        return output;
    }

    private static void WriteHeader(byte[] destination, ImgPixelFormat format, int width, int height, int pixelBits, int dataOffset)
    {
        destination[0] = 0x00;
        destination[1] = 0x63;
        destination[2] = 0x74;
        destination[3] = 0x65;
        BinaryPrimitives.WriteUInt32LittleEndian(destination.AsSpan(0x04, 4), (uint)format);
        BinaryPrimitives.WriteUInt32LittleEndian(destination.AsSpan(0x08, 4), (uint)width);
        BinaryPrimitives.WriteUInt32LittleEndian(destination.AsSpan(0x0C, 4), (uint)height);
        BinaryPrimitives.WriteUInt32LittleEndian(destination.AsSpan(0x10, 4), (uint)pixelBits);
        BinaryPrimitives.WriteUInt32LittleEndian(destination.AsSpan(0x18, 4), (uint)dataOffset);
    }

    private static int QuantizeTo4(byte value)
    {
        return Math.Clamp((value + 8) / 17, 0, 15);
    }
}
