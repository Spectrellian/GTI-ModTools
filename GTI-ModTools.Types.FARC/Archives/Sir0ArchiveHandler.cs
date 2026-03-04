using System.Buffers.Binary;
using System.Text.Json;

namespace GTI.ModTools.FARC;

public sealed class Sir0ArchiveHandler : IArchiveHandler
{
    public string TypeId => "sir0";
    public string TypeDisplayName => "SIR0";

    public bool CanHandle(ReadOnlySpan<byte> bytes, string extension)
    {
        return bytes.Length >= 4 &&
               bytes[0] == (byte)'S' &&
               bytes[1] == (byte)'I' &&
               bytes[2] == (byte)'R' &&
               bytes[3] == (byte)'0';
    }

    public IReadOnlyList<ArchiveOptionDefinition> GetOptions() => [];

    public ArchiveFileAnalysis Analyze(string filePath, byte[] bytes)
    {
        if (bytes.Length < 0x10)
        {
            return new ArchiveFileAnalysis(
                InputPath: Path.GetFullPath(filePath),
                FileSize: bytes.Length,
                TypeId: TypeId,
                TypeDisplayName: TypeDisplayName,
                IsKnownType: true,
                IsExtractable: false,
                Entries: [],
                ReferencedNames: [],
                Summary: "SIR0 header too small",
                Error: "SIR0 header is truncated.");
        }

        var pointerOffset = checked((int)BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(0x04, 4)));
        var dataOffset = checked((int)BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(0x08, 4)));
        var pointers = DecodePointerOffsets(bytes, pointerOffset);

        var payloadStart = Math.Clamp(dataOffset, 0, bytes.Length);
        var payloadEnd = pointerOffset > payloadStart && pointerOffset <= bytes.Length ? pointerOffset : bytes.Length;
        var payloadLength = Math.Max(0, payloadEnd - payloadStart);

        var entries = new List<ArchiveEntryInfo>
        {
            new(
                Name: "sir0_payload",
                Offset: $"0x{payloadStart:X8}",
                Length: payloadLength.ToString(),
                Kind: ".bin",
                Details: "Data region before pointer table"),
            new(
                Name: "sir0_pointer_table",
                Offset: $"0x{Math.Max(pointerOffset, 0):X8}",
                Length: pointerOffset >= 0 && pointerOffset < bytes.Length ? (bytes.Length - pointerOffset).ToString() : "0",
                Kind: ".tbl",
                Details: $"decoded-pointers={pointers.Count}")
        };

        return new ArchiveFileAnalysis(
            InputPath: Path.GetFullPath(filePath),
            FileSize: bytes.Length,
            TypeId: TypeId,
            TypeDisplayName: TypeDisplayName,
            IsKnownType: true,
            IsExtractable: true,
            Entries: entries,
            ReferencedNames: [],
            Summary: $"data=0x{dataOffset:X8}, ptr=0x{pointerOffset:X8}, pointers={pointers.Count}");
    }

    public ArchiveExtractResult Extract(string filePath, byte[] bytes, string outputRoot, IReadOnlyDictionary<string, bool> options)
    {
        var outDir = Path.Combine(outputRoot, Path.GetFileNameWithoutExtension(filePath));
        Directory.CreateDirectory(outDir);

        var pointerOffset = bytes.Length >= 0x08 ? checked((int)BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(0x04, 4))) : 0;
        var dataOffset = bytes.Length >= 0x0C ? checked((int)BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(0x08, 4))) : 0;

        var pointers = DecodePointerOffsets(bytes, pointerOffset);
        var payloadStart = Math.Clamp(dataOffset, 0, bytes.Length);
        var payloadEnd = pointerOffset > payloadStart && pointerOffset <= bytes.Length ? pointerOffset : bytes.Length;
        var payloadLength = Math.Max(0, payloadEnd - payloadStart);

        File.WriteAllBytes(Path.Combine(outDir, "sir0_payload.bin"), bytes.AsSpan(payloadStart, payloadLength).ToArray());
        File.WriteAllBytes(Path.Combine(outDir, "sir0_full.bin"), bytes);

        var pointerDump = pointers
            .Select(offset =>
            {
                var target = offset >= 0 && offset + 4 <= bytes.Length
                    ? BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(offset, 4)).ToString("X8")
                    : "????????";
                return $"0x{offset:X8} -> 0x{target}";
            })
            .ToArray();
        File.WriteAllLines(Path.Combine(outDir, "sir0_pointers.txt"), pointerDump);

        var header = new
        {
            inputPath = Path.GetFullPath(filePath),
            fileSize = bytes.Length,
            dataOffset,
            pointerOffset,
            decodedPointerCount = pointers.Count
        };
        File.WriteAllText(Path.Combine(outDir, "sir0_manifest.json"), JsonSerializer.Serialize(header, JsonOptions));

        return new ArchiveExtractResult(true, outDir, $"Extracted SIR0 payload and {pointers.Count} decoded pointers.");
    }

    private static IReadOnlyList<int> DecodePointerOffsets(byte[] bytes, int pointerOffset)
    {
        if (pointerOffset < 0 || pointerOffset >= bytes.Length)
        {
            return [];
        }

        var pointers = new List<int>();
        var current = 0;
        var cursor = pointerOffset;

        while (cursor < bytes.Length)
        {
            var b = bytes[cursor++];
            if (b == 0)
            {
                break;
            }

            var value = b & 0x7F;
            while ((b & 0x80) != 0 && cursor < bytes.Length)
            {
                b = bytes[cursor++];
                value = (value << 7) | (b & 0x7F);
            }

            current += value;
            if (current >= 0 && current < bytes.Length)
            {
                pointers.Add(current);
            }
        }

        return pointers;
    }

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true
    };
}
