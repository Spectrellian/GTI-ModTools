using System.Buffers.Binary;
using GTI.ModTools.FARC;

namespace GTI.ModTools.FARC.Tests;

public class ArchiveServiceTests
{
    [Fact]
    public void Scan_ClassifiesKnownAndUnknownArchiveTypes()
    {
        var root = Path.Combine(Path.GetTempPath(), "gti-archive-scan-tests-" + Guid.NewGuid());
        Directory.CreateDirectory(root);
        try
        {
            File.WriteAllBytes(Path.Combine(root, "a_farc.bin"), BuildSingleSectionFarc("icon.img"));
            File.WriteAllBytes(Path.Combine(root, "a1_exefs.bin"), BuildExeFs(".code", [1, 2, 3, 4]));
            File.WriteAllBytes(Path.Combine(root, "b_sir0.bin"), BuildSir0());
            File.WriteAllBytes(Path.Combine(root, "c_narc.bin"), BuildNarc(["abc"u8.ToArray()]));
            File.WriteAllBytes(Path.Combine(root, "d_unknown.bin"), "????"u8.ToArray());

            var report = ArchiveService.Scan(root, recursive: false);

            Assert.Equal(5, report.Scanned);
            Assert.Equal(4, report.Known);
            Assert.Equal(1, report.Unknown);
            Assert.Empty(report.Failures);

            Assert.Contains(report.Files, file => file.TypeDisplayName == "FARC");
            Assert.Contains(report.Files, file => file.TypeDisplayName == "ExeFS");
            Assert.Contains(report.Files, file => file.TypeDisplayName == "SIR0");
            Assert.Contains(report.Files, file => file.TypeDisplayName == "NARC");
            Assert.Contains(report.Files, file => file.TypeDisplayName == "Unknown");
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void ExtractAll_ExtractsKnownAndSkipsUnknown()
    {
        var root = Path.Combine(Path.GetTempPath(), "gti-archive-extract-tests-" + Guid.NewGuid());
        Directory.CreateDirectory(root);
        try
        {
            File.WriteAllBytes(Path.Combine(root, "a_farc.bin"), BuildSingleSectionFarc("icon.img"));
            File.WriteAllBytes(Path.Combine(root, "a1_exefs.bin"), BuildExeFs(".code", [1, 2, 3, 4]));
            File.WriteAllBytes(Path.Combine(root, "b_sir0.bin"), BuildSir0());
            File.WriteAllBytes(Path.Combine(root, "c_narc.bin"), BuildNarc(["first"u8.ToArray(), "second"u8.ToArray()]));
            File.WriteAllBytes(Path.Combine(root, "d_unknown.bin"), "????"u8.ToArray());

            var output = Path.Combine(root, "out");
            var options = new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase)
            {
                [FarcArchiveHandler.CarveBchOptionKey] = false
            };

            var report = ArchiveService.ExtractAll(root, output, recursive: false, options);

            Assert.Equal(5, report.Scanned);
            Assert.Equal(4, report.Extracted);
            Assert.Equal(1, report.SkippedUnknown);
            Assert.Empty(report.Failed);

            Assert.True(Directory.EnumerateFiles(output, "manifest.json", SearchOption.AllDirectories).Any());
            Assert.True(Directory.EnumerateFiles(output, "exefs_manifest.json", SearchOption.AllDirectories).Any());
            Assert.True(Directory.EnumerateFiles(output, "sir0_manifest.json", SearchOption.AllDirectories).Any());
            Assert.True(Directory.EnumerateFiles(output, "narc_manifest.json", SearchOption.AllDirectories).Any());
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void GetOptionDefinitionsForType_FarcContainsCarveBch()
    {
        var options = ArchiveService.GetOptionDefinitionsForType("farc");
        Assert.Contains(options, option => option.Key == FarcArchiveHandler.CarveBchOptionKey);
    }

    [Fact]
    public void Scan_DetectsDb10Database()
    {
        var root = Path.Combine(Path.GetTempPath(), "gti-db10-scan-tests-" + Guid.NewGuid());
        Directory.CreateDirectory(root);
        try
        {
            File.WriteAllBytes(Path.Combine(root, "sample_db.bin"), BuildDb10("ga_db_1.0", ["AR_HOLE", "BANQUET"]));

            var report = ArchiveService.Scan(root, recursive: false);

            Assert.Equal(1, report.Scanned);
            Assert.Equal(1, report.Known);
            Assert.Equal(0, report.Unknown);
            Assert.Empty(report.Failures);

            var file = Assert.Single(report.Files);
            Assert.Equal("GTI DB", file.TypeDisplayName);
            Assert.Contains("records=2", file.Summary);
            Assert.Contains(file.Entries, entry => entry.Name.Contains("AR_HOLE", StringComparison.OrdinalIgnoreCase));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void ExtractAll_ExtractsDb10Manifest()
    {
        var root = Path.Combine(Path.GetTempPath(), "gti-db10-extract-tests-" + Guid.NewGuid());
        Directory.CreateDirectory(root);
        try
        {
            File.WriteAllBytes(Path.Combine(root, "sample_db.bin"), BuildDb10("gg_db_1.0", ["AR_HOLE", "BARRICADE"]));

            var output = Path.Combine(root, "out");
            var report = ArchiveService.ExtractAll(root, output, recursive: false, options: null);

            Assert.Equal(1, report.Scanned);
            Assert.Equal(1, report.Extracted);
            Assert.Equal(0, report.SkippedUnknown);
            Assert.Empty(report.Failed);
            Assert.True(Directory.EnumerateFiles(output, "db_manifest.json", SearchOption.AllDirectories).Any());
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    private static byte[] BuildSingleSectionFarc(string referencedName)
    {
        var section = BuildSectionPayload(referencedName);
        var sectionOffset = 0x28;
        var totalLength = sectionOffset + section.Length;
        var bytes = new byte[totalLength];

        bytes[0] = (byte)'F';
        bytes[1] = (byte)'A';
        bytes[2] = (byte)'R';
        bytes[3] = (byte)'C';

        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(0x20, 4), 1);
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(0x24, 4), (uint)sectionOffset);
        section.CopyTo(bytes, sectionOffset);

        return bytes;
    }

    private static byte[] BuildSectionPayload(string referencedName)
    {
        var payload = new List<byte>();
        payload.Add((byte)'S');
        payload.Add((byte)'I');
        payload.Add((byte)'R');
        payload.Add((byte)'0');
        payload.AddRange(new byte[8]);
        payload.AddRange(System.Text.Encoding.ASCII.GetBytes(referencedName));
        payload.Add(0);
        payload.AddRange(new byte[8]);
        return payload.ToArray();
    }

    private static byte[] BuildSir0()
    {
        var bytes = new byte[0x21];
        bytes[0] = (byte)'S';
        bytes[1] = (byte)'I';
        bytes[2] = (byte)'R';
        bytes[3] = (byte)'0';

        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(0x04, 4), 0x20);
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(0x08, 4), 0x10);

        for (var i = 0x10; i < 0x20; i++)
        {
            bytes[i] = (byte)(i - 0x10 + 1);
        }

        bytes[0x20] = 0x00;
        return bytes;
    }

    private static byte[] BuildNarc(IReadOnlyList<byte[]> files)
    {
        var fileCount = files.Count;
        var fimgDataLength = files.Sum(file => file.Length);

        var btafSize = 0x0C + (fileCount * 8);
        var btnfSize = 0x08;
        var fimgSize = 0x08 + fimgDataLength;

        var totalSize = 0x10 + btafSize + btnfSize + fimgSize;
        var bytes = new byte[totalSize];

        bytes[0] = (byte)'N';
        bytes[1] = (byte)'A';
        bytes[2] = (byte)'R';
        bytes[3] = (byte)'C';
        BinaryPrimitives.WriteUInt16LittleEndian(bytes.AsSpan(0x04, 2), 0xFEFF);
        BinaryPrimitives.WriteUInt16LittleEndian(bytes.AsSpan(0x06, 2), 0x0100);
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(0x08, 4), (uint)totalSize);
        BinaryPrimitives.WriteUInt16LittleEndian(bytes.AsSpan(0x0C, 2), 0x10);
        BinaryPrimitives.WriteUInt16LittleEndian(bytes.AsSpan(0x0E, 2), 3);

        var cursor = 0x10;

        WriteMagic(bytes, cursor, "BTAF");
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(cursor + 4, 4), (uint)btafSize);
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(cursor + 8, 4), (uint)fileCount);

        var relativeOffset = 0;
        for (var i = 0; i < fileCount; i++)
        {
            var start = relativeOffset;
            var end = start + files[i].Length;
            BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(cursor + 0x0C + i * 8, 4), (uint)start);
            BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(cursor + 0x10 + i * 8, 4), (uint)end);
            relativeOffset = end;
        }

        cursor += btafSize;

        WriteMagic(bytes, cursor, "BTNF");
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(cursor + 4, 4), (uint)btnfSize);

        cursor += btnfSize;

        WriteMagic(bytes, cursor, "GMIF");
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(cursor + 4, 4), (uint)fimgSize);

        var dataCursor = cursor + 8;
        foreach (var file in files)
        {
            file.CopyTo(bytes, dataCursor);
            dataCursor += file.Length;
        }

        return bytes;
    }

    private static void WriteMagic(byte[] buffer, int offset, string magic)
    {
        buffer[offset] = (byte)magic[0];
        buffer[offset + 1] = (byte)magic[1];
        buffer[offset + 2] = (byte)magic[2];
        buffer[offset + 3] = (byte)magic[3];
    }

    private static byte[] BuildExeFs(string name, byte[] payload)
    {
        var bytes = new byte[0x200 + payload.Length];
        var nameBytes = System.Text.Encoding.ASCII.GetBytes(name);
        Array.Copy(nameBytes, 0, bytes, 0x00, Math.Min(8, nameBytes.Length));
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(0x08, 4), 0);
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(0x0C, 4), (uint)payload.Length);
        payload.CopyTo(bytes, 0x200);
        return bytes;
    }

    private static byte[] BuildDb10(string magic, IReadOnlyList<string> names)
    {
        var recordSize = 0x40;
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
