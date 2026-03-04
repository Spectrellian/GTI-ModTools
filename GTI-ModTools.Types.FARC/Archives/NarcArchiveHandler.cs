using System.Buffers.Binary;
using System.Text.Json;

namespace GTI.ModTools.FARC;

public sealed class NarcArchiveHandler : IArchiveHandler
{
    public string TypeId => "narc";
    public string TypeDisplayName => "NARC";

    public bool CanHandle(ReadOnlySpan<byte> bytes, string extension)
    {
        return bytes.Length >= 4 &&
            ((bytes[0] == (byte)'N' && bytes[1] == (byte)'A' && bytes[2] == (byte)'R' && bytes[3] == (byte)'C') ||
             (bytes[0] == (byte)'C' && bytes[1] == (byte)'R' && bytes[2] == (byte)'A' && bytes[3] == (byte)'N'));
    }

    public IReadOnlyList<ArchiveOptionDefinition> GetOptions() => [];

    public ArchiveFileAnalysis Analyze(string filePath, byte[] bytes)
    {
        if (!TryParse(bytes, out var entries, out var error))
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
                Summary: "NARC parse failed",
                Error: error);
        }

        var details = entries
            .Select((entry, index) => new ArchiveEntryInfo(
                Name: $"file_{index:D4}.bin",
                Offset: $"0x{entry.AbsoluteOffset:X8}",
                Length: entry.Length.ToString(),
                Kind: ".bin",
                Details: $"rel=[0x{entry.RelativeStart:X8}..0x{entry.RelativeEnd:X8})"))
            .ToArray();

        return new ArchiveFileAnalysis(
            InputPath: Path.GetFullPath(filePath),
            FileSize: bytes.Length,
            TypeId: TypeId,
            TypeDisplayName: TypeDisplayName,
            IsKnownType: true,
            IsExtractable: true,
            Entries: details,
            ReferencedNames: [],
            Summary: $"entries={entries.Count}");
    }

    public ArchiveExtractResult Extract(string filePath, byte[] bytes, string outputRoot, IReadOnlyDictionary<string, bool> options)
    {
        if (!TryParse(bytes, out var entries, out var error))
        {
            return new ArchiveExtractResult(false, null, $"NARC parse failed: {error}");
        }

        var outDir = Path.Combine(outputRoot, Path.GetFileNameWithoutExtension(filePath));
        Directory.CreateDirectory(outDir);

        for (var i = 0; i < entries.Count; i++)
        {
            var entry = entries[i];
            var outPath = Path.Combine(outDir, $"file_{i:D4}.bin");
            File.WriteAllBytes(outPath, bytes.AsSpan(entry.AbsoluteOffset, entry.Length).ToArray());
        }

        var manifest = new
        {
            inputPath = Path.GetFullPath(filePath),
            fileSize = bytes.Length,
            fileCount = entries.Count,
            files = entries.Select((entry, index) => new
            {
                index,
                outputFile = $"file_{index:D4}.bin",
                absoluteOffset = entry.AbsoluteOffset,
                length = entry.Length,
                relativeStart = entry.RelativeStart,
                relativeEnd = entry.RelativeEnd
            }).ToArray()
        };

        File.WriteAllText(Path.Combine(outDir, "narc_manifest.json"), JsonSerializer.Serialize(manifest, JsonOptions));
        return new ArchiveExtractResult(true, outDir, $"Extracted {entries.Count} files from NARC.");
    }

    private static bool TryParse(byte[] bytes, out IReadOnlyList<NarcEntry> entries, out string error)
    {
        entries = [];
        error = string.Empty;

        if (bytes.Length < 0x20)
        {
            error = "File too small for NARC.";
            return false;
        }

        var headerSize = BinaryPrimitives.ReadUInt16LittleEndian(bytes.AsSpan(0x0C, 2));
        if (headerSize < 0x10 || headerSize >= bytes.Length)
        {
            error = "Invalid NARC header size.";
            return false;
        }

        var cursor = (int)headerSize;
        int? btafOffset = null;
        int? fimgOffset = null;

        for (var section = 0; section < 8 && cursor + 8 <= bytes.Length; section++)
        {
            var magic = GetMagic(bytes, cursor);
            var sectionSize = checked((int)BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(cursor + 4, 4)));
            if (sectionSize < 8 || cursor + sectionSize > bytes.Length)
            {
                break;
            }

            if (magic is "BTAF" or "FATB")
            {
                btafOffset = cursor;
            }
            else if (magic is "FIMG" or "GMIF")
            {
                fimgOffset = cursor;
            }

            cursor += sectionSize;
        }

        if (!btafOffset.HasValue || !fimgOffset.HasValue)
        {
            error = "Missing BTAF/FIMG blocks.";
            return false;
        }

        var fat = btafOffset.Value;
        var fatSize = checked((int)BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(fat + 4, 4)));
        if (fat + fatSize > bytes.Length || fat + 0x0C > bytes.Length)
        {
            error = "Invalid BTAF block size.";
            return false;
        }

        var fileCount = checked((int)BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(fat + 8, 4)));
        var entryBase = fat + 0x0C;
        var expectedEntryBytes = checked(fileCount * 8);
        if (entryBase + expectedEntryBytes > fat + fatSize)
        {
            error = "Invalid BTAF entry table.";
            return false;
        }

        var img = fimgOffset.Value;
        var imgSize = checked((int)BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(img + 4, 4)));
        if (img + imgSize > bytes.Length)
        {
            error = "Invalid FIMG block size.";
            return false;
        }

        var dataBase = img + 8;
        var dataEnd = img + imgSize;

        var parsed = new List<NarcEntry>(fileCount);
        for (var i = 0; i < fileCount; i++)
        {
            var start = checked((int)BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(entryBase + i * 8, 4)));
            var end = checked((int)BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(entryBase + i * 8 + 4, 4)));
            if (start < 0 || end < start)
            {
                error = "Invalid NARC entry offsets.";
                return false;
            }

            var absoluteStart = dataBase + start;
            var absoluteEnd = dataBase + end;
            if (absoluteStart < dataBase || absoluteEnd > dataEnd)
            {
                error = "NARC entry exceeds FIMG bounds.";
                return false;
            }

            parsed.Add(new NarcEntry(absoluteStart, end - start, start, end));
        }

        entries = parsed;
        return true;
    }

    private static string GetMagic(byte[] bytes, int offset)
    {
        return string.Create(4, bytes.AsSpan(offset, 4), static (span, source) =>
        {
            span[0] = (char)source[0];
            span[1] = (char)source[1];
            span[2] = (char)source[2];
            span[3] = (char)source[3];
        });
    }

    private readonly record struct NarcEntry(int AbsoluteOffset, int Length, int RelativeStart, int RelativeEnd);

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true
    };
}
