using System.Buffers.Binary;

namespace GTI.ModTools.Images;

public static class Etc1Decoder
{
    private static readonly int[][] ModifierTable =
    [
        [2, 8, -2, -8],
        [5, 17, -5, -17],
        [9, 29, -9, -29],
        [13, 42, -13, -42],
        [18, 60, -18, -60],
        [24, 80, -24, -80],
        [33, 106, -33, -106],
        [47, 183, -47, -183]
    ];

    public static byte[] DecodeEtc1(ReadOnlySpan<byte> data, int width, int height)
    {
        var rgba = new byte[width * height * 4];
        Decode(data, width, height, rgba, hasAlpha: false);
        return rgba;
    }

    public static byte[] DecodeEtc1A4(ReadOnlySpan<byte> data, int width, int height)
    {
        var rgba = new byte[width * height * 4];
        Decode(data, width, height, rgba, hasAlpha: true);
        return rgba;
    }

    private static void Decode(ReadOnlySpan<byte> data, int width, int height, Span<byte> rgba, bool hasAlpha)
    {
        if (width % 4 != 0 || height % 4 != 0)
        {
            throw new InvalidDataException("ETC textures require width/height multiples of 4.");
        }

        var bytesPerBlock = hasAlpha ? 16 : 8;
        var blockIndex = 0;

        if (width % 8 == 0 && height % 8 == 0)
        {
            var tilesX = width / 8;
            var tilesY = height / 8;

            for (var tileY = 0; tileY < tilesY; tileY++)
            {
                for (var tileX = 0; tileX < tilesX; tileX++)
                {
                    for (var by = 0; by < 2; by++)
                    {
                        for (var bx = 0; bx < 2; bx++)
                        {
                            DecodeBlock(
                                data,
                                blockIndex++,
                                bytesPerBlock,
                                rgba,
                                width,
                                tileX * 8 + bx * 4,
                                tileY * 8 + by * 4,
                                hasAlpha);
                        }
                    }
                }
            }
        }
        else
        {
            var blocksX = width / 4;
            var blocksY = height / 4;

            for (var blockY = 0; blockY < blocksY; blockY++)
            {
                for (var blockX = 0; blockX < blocksX; blockX++)
                {
                    DecodeBlock(
                        data,
                        blockIndex++,
                        bytesPerBlock,
                        rgba,
                        width,
                        blockX * 4,
                        blockY * 4,
                        hasAlpha);
                }
            }
        }
    }

    private static void DecodeBlock(
        ReadOnlySpan<byte> source,
        int blockIndex,
        int bytesPerBlock,
        Span<byte> rgba,
        int width,
        int startX,
        int startY,
        bool hasAlpha)
    {
        var offset = blockIndex * bytesPerBlock;
        if (offset + bytesPerBlock > source.Length)
        {
            throw new InvalidDataException("Unexpected end of ETC data.");
        }

        ReadOnlySpan<byte> alphaSpan = default;
        var colorOffset = offset;
        if (hasAlpha)
        {
            alphaSpan = source.Slice(offset, 8);
            colorOffset += 8;
        }

        // PMD:GTI ETC1 stores each 64-bit block as two little-endian 32-bit words:
        // selector bits first, then color/control bits.
        var low = BinaryPrimitives.ReadUInt32LittleEndian(source.Slice(colorOffset, 4));
        var high = BinaryPrimitives.ReadUInt32LittleEndian(source.Slice(colorOffset + 4, 4));

        var diffBit = ((high >> 1) & 1) != 0;
        var flipBit = (high & 1) != 0;
        var table1 = (int)((high >> 5) & 0x7);
        var table2 = (int)((high >> 2) & 0x7);

        (int r0, int g0, int b0, int r1, int g1, int b1) bases;
        if (diffBit)
        {
            var rBase = (int)((high >> 27) & 0x1F);
            var gBase = (int)((high >> 19) & 0x1F);
            var bBase = (int)((high >> 11) & 0x1F);

            var dr = SignExtend3((int)((high >> 24) & 0x7));
            var dg = SignExtend3((int)((high >> 16) & 0x7));
            var db = SignExtend3((int)((high >> 8) & 0x7));

            var r2 = Clamp(rBase + dr, 0, 31);
            var g2 = Clamp(gBase + dg, 0, 31);
            var b2 = Clamp(bBase + db, 0, 31);

            bases = (Expand5(rBase), Expand5(gBase), Expand5(bBase), Expand5(r2), Expand5(g2), Expand5(b2));
        }
        else
        {
            var r0 = (int)((high >> 28) & 0xF);
            var r1 = (int)((high >> 24) & 0xF);
            var g0 = (int)((high >> 20) & 0xF);
            var g1 = (int)((high >> 16) & 0xF);
            var b0 = (int)((high >> 12) & 0xF);
            var b1 = (int)((high >> 8) & 0xF);

            bases = (Expand4(r0), Expand4(g0), Expand4(b0), Expand4(r1), Expand4(g1), Expand4(b1));
        }

        for (var y = 0; y < 4; y++)
        {
            for (var x = 0; x < 4; x++)
            {
                var bitIndex = x * 4 + y;
                var lsb = (int)((low >> bitIndex) & 1);
                var msb = (int)((low >> (bitIndex + 16)) & 1);
                var code = (msb << 1) | lsb;

                var secondSubBlock = flipBit ? y >= 2 : x >= 2;
                var table = secondSubBlock ? table2 : table1;
                var delta = ModifierTable[table][code];

                var rBase = secondSubBlock ? bases.r1 : bases.r0;
                var gBase = secondSubBlock ? bases.g1 : bases.g0;
                var bBase = secondSubBlock ? bases.b1 : bases.b0;

                var r = Clamp(rBase + delta, 0, 255);
                var g = Clamp(gBase + delta, 0, 255);
                var b = Clamp(bBase + delta, 0, 255);
                var a = 255;

                if (hasAlpha)
                {
                    var alphaIndex = x * 4 + y;
                    var alphaByte = alphaSpan[alphaIndex / 2];
                    var nibble = alphaIndex % 2 == 0 ? (alphaByte >> 4) & 0xF : alphaByte & 0xF;
                    a = Expand4(nibble);
                }

                var px = startX + x;
                var py = startY + y;
                var dst = (py * width + px) * 4;

                rgba[dst] = (byte)r;
                rgba[dst + 1] = (byte)g;
                rgba[dst + 2] = (byte)b;
                rgba[dst + 3] = (byte)a;
            }
        }
    }

    private static int Expand4(int value) => (value << 4) | value;
    private static int Expand5(int value) => (value << 3) | (value >> 2);
    private static int Clamp(int value, int min, int max) => value < min ? min : (value > max ? max : value);

    private static int SignExtend3(int value)
    {
        value &= 0x7;
        return (value & 0x4) != 0 ? value - 8 : value;
    }
}
