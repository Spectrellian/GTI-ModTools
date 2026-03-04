using System.Buffers.Binary;
using System.Text;
using System.Text.Json;

namespace GTI.ModTools.FARC;

public sealed class ExeFsArchiveHandler : IArchiveHandler
{
    private const int HeaderSize = 0x200;
    private const int EntryCount = 10;
    private const int EntrySize = 0x10;

    public string TypeId => "exefs";
    public string TypeDisplayName => "ExeFS";

    public bool CanHandle(ReadOnlySpan<byte> bytes, string extension)
    {
        return TryParse(bytes, out _, out _);
    }

    public IReadOnlyList<ArchiveOptionDefinition> GetOptions() => [];

    public ArchiveFileAnalysis Analyze(string filePath, byte[] bytes)
    {
        if (!TryParse(bytes, out var entries, out var error))
        {
            return ArchiveService.CreateUnknownAnalysis(filePath, bytes, $"ExeFS parse failed: {error}");
        }

        var archiveEntries = entries
            .Select(entry => new ArchiveEntryInfo(
                Name: entry.Name,
                Offset: $"0x{entry.AbsoluteOffset:X8}",
                Length: entry.Length.ToString(),
                Kind: ".bin",
                Details: $"rel=0x{entry.RelativeOffset:X8}"))
            .ToArray();

        return new ArchiveFileAnalysis(
            InputPath: Path.GetFullPath(filePath),
            FileSize: bytes.Length,
            TypeId: TypeId,
            TypeDisplayName: TypeDisplayName,
            IsKnownType: true,
            IsExtractable: true,
            Entries: archiveEntries,
            ReferencedNames: entries.Select(entry => entry.Name).ToArray(),
            Summary: $"entries={entries.Count}");
    }

    public ArchiveExtractResult Extract(string filePath, byte[] bytes, string outputRoot, IReadOnlyDictionary<string, bool> options)
    {
        if (!TryParse(bytes, out var entries, out var error))
        {
            return new ArchiveExtractResult(false, null, $"ExeFS parse failed: {error}");
        }

        var outDir = Path.Combine(outputRoot, Path.GetFileNameWithoutExtension(filePath));
        Directory.CreateDirectory(outDir);

        var manifestEntries = new List<object>(entries.Count);
        for (var i = 0; i < entries.Count; i++)
        {
            var entry = entries[i];
            var fileName = $"{SanitizeName(entry.Name)}.bin";
            var outPath = Path.Combine(outDir, fileName);
            File.WriteAllBytes(outPath, bytes.AsSpan(entry.AbsoluteOffset, entry.Length).ToArray());

            manifestEntries.Add(new
            {
                index = i,
                entry.Name,
                entry.RelativeOffset,
                entry.AbsoluteOffset,
                entry.Length,
                outputFile = fileName
            });
        }

        var manifest = new
        {
            inputPath = Path.GetFullPath(filePath),
            fileSize = bytes.Length,
            type = "ExeFS",
            headerSize = HeaderSize,
            fileCount = entries.Count,
            files = manifestEntries
        };
        File.WriteAllText(Path.Combine(outDir, "exefs_manifest.json"), JsonSerializer.Serialize(manifest, JsonOptions));

        return new ArchiveExtractResult(true, outDir, $"Extracted {entries.Count} ExeFS entries.");
    }

    private static bool TryParse(ReadOnlySpan<byte> bytes, out List<ExeFsEntry> entries, out string error)
    {
        entries = [];
        error = string.Empty;

        if (bytes.Length < HeaderSize)
        {
            error = "File too small for ExeFS header.";
            return false;
        }

        for (var i = 0; i < EntryCount; i++)
        {
            var entryOffset = i * EntrySize;
            var nameBytes = bytes.Slice(entryOffset, 8);
            var relativeOffset = BinaryPrimitives.ReadUInt32LittleEndian(bytes.Slice(entryOffset + 8, 4));
            var length = BinaryPrimitives.ReadUInt32LittleEndian(bytes.Slice(entryOffset + 12, 4));

            var isEmptyName = nameBytes.IndexOfAnyExcept((byte)0) < 0;
            if (isEmptyName && relativeOffset == 0 && length == 0)
            {
                continue;
            }

            if (length == 0)
            {
                error = $"Entry {i} has invalid length.";
                return false;
            }

            var name = DecodeName(nameBytes);
            if (string.IsNullOrWhiteSpace(name))
            {
                error = $"Entry {i} has invalid name.";
                return false;
            }

            var absoluteOffsetLong = HeaderSize + (long)relativeOffset;
            var absoluteEndLong = absoluteOffsetLong + length;
            if (absoluteOffsetLong < HeaderSize || absoluteOffsetLong > bytes.Length || absoluteEndLong > bytes.Length)
            {
                error = $"Entry {i} exceeds file bounds.";
                return false;
            }

            entries.Add(new ExeFsEntry(name, (int)relativeOffset, (int)absoluteOffsetLong, (int)length));
        }

        if (entries.Count == 0)
        {
            error = "No valid ExeFS entries found.";
            return false;
        }

        return true;
    }

    private static string DecodeName(ReadOnlySpan<byte> bytes)
    {
        var zero = bytes.IndexOf((byte)0);
        var length = zero >= 0 ? zero : bytes.Length;
        if (length == 0)
        {
            return string.Empty;
        }

        var raw = Encoding.ASCII.GetString(bytes.Slice(0, length));
        return raw.Trim();
    }

    private static string SanitizeName(string name)
    {
        var trimmed = name.Trim();
        if (trimmed.StartsWith(".", StringComparison.Ordinal))
        {
            trimmed = trimmed[1..];
        }

        if (string.IsNullOrWhiteSpace(trimmed))
        {
            return "entry";
        }

        var invalid = Path.GetInvalidFileNameChars();
        var chars = trimmed
            .Select(ch => invalid.Contains(ch) ? '_' : ch)
            .ToArray();
        return new string(chars);
    }

    private readonly record struct ExeFsEntry(string Name, int RelativeOffset, int AbsoluteOffset, int Length);

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true
    };
}
