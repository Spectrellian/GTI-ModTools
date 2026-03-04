using System.Buffers.Binary;
using GTI.ModTools.Databases;

namespace GTI.ModTools.Types.Databases.Tests;

public class Db10ParserTests
{
    [Fact]
    public void TryParse_ValidDb10_ReturnsHeaderAndRecords()
    {
        var bytes = BuildDb10("gg_db_1.0", ["AR_HOLE", "BANQUET"]);

        var ok = Db10Parser.TryParse(bytes, out var db, out var error);

        Assert.True(ok, error);
        Assert.Equal("gg_db_1.0", db.Header.Magic);
        Assert.Equal(2, db.Header.Count);
        Assert.Equal(0x40, db.Header.RecordSize);
        Assert.Equal(2, db.Records.Count);
        Assert.Contains("AR_HOLE", db.Records[0].Strings, StringComparer.OrdinalIgnoreCase);
    }

    [Fact]
    public void TryParse_InvalidMagic_ReturnsFalse()
    {
        var bytes = BuildDb10("invalid", ["AR_HOLE"]);

        var ok = Db10Parser.TryParse(bytes, out _, out _);

        Assert.False(ok);
    }

    private static byte[] BuildDb10(string magic, IReadOnlyList<string> names)
    {
        const int recordSize = 0x40;
        var bytes = new byte[0x20 + names.Count * recordSize];
        var magicBytes = System.Text.Encoding.ASCII.GetBytes(magic);
        Array.Copy(magicBytes, bytes, Math.Min(12, magicBytes.Length));

        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(0x10, 4), (uint)names.Count);

        for (var i = 0; i < names.Count; i++)
        {
            var start = 0x20 + i * recordSize;
            var ascii = System.Text.Encoding.ASCII.GetBytes(names[i]);
            Array.Copy(ascii, 0, bytes, start, Math.Min(ascii.Length, 0x1F));
            bytes[start + Math.Min(ascii.Length, 0x1F)] = 0;
        }

        return bytes;
    }
}
