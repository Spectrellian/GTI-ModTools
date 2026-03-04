using System.Buffers.Binary;

namespace GTI.ModTools.Images;

public readonly record struct ImgHeader(
    ImgPixelFormat Format,
    int Width,
    int Height,
    int PixelLengthBits,
    int DataOffset)
{
    public const int HeaderLength = 0x20;
    private static readonly byte[] Magic = [0x00, 0x63, 0x74, 0x65];

    public static ImgHeader Parse(ReadOnlySpan<byte> bytes)
    {
        if (bytes.Length < HeaderLength)
        {
            throw new InvalidDataException($"IMG file too small. Need at least {HeaderLength} bytes for header.");
        }

        if (!bytes[..4].SequenceEqual(Magic))
        {
            throw new InvalidDataException("Invalid IMG magic. Expected 00 63 74 65 (.cte).");
        }

        var format = (ImgPixelFormat)BinaryPrimitives.ReadUInt32LittleEndian(bytes[0x04..0x08]);
        var width = checked((int)BinaryPrimitives.ReadUInt32LittleEndian(bytes[0x08..0x0C]));
        var height = checked((int)BinaryPrimitives.ReadUInt32LittleEndian(bytes[0x0C..0x10]));
        var pixelLengthBits = checked((int)BinaryPrimitives.ReadUInt32LittleEndian(bytes[0x10..0x14]));
        var dataOffset = checked((int)BinaryPrimitives.ReadUInt32LittleEndian(bytes[0x18..0x1C]));

        if (width <= 0 || height <= 0)
        {
            throw new InvalidDataException($"Invalid dimensions: {width}x{height}");
        }

        if (dataOffset < 0 || dataOffset > bytes.Length)
        {
            throw new InvalidDataException($"Invalid data offset: 0x{dataOffset:X}");
        }

        return new ImgHeader(format, width, height, pixelLengthBits, dataOffset);
    }
}
