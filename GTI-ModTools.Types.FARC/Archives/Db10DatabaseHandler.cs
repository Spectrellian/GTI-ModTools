using System.Text.Json;
using GTI.ModTools.Databases;

namespace GTI.ModTools.FARC;

public sealed class Db10DatabaseHandler : IArchiveHandler
{
    public string TypeId => "gti-db10";
    public string TypeDisplayName => "GTI DB";

    public bool CanHandle(ReadOnlySpan<byte> bytes, string extension)
    {
        return Db10Parser.IsDb10(bytes);
    }

    public IReadOnlyList<ArchiveOptionDefinition> GetOptions() => [];

    public ArchiveFileAnalysis Analyze(string filePath, byte[] bytes)
    {
        if (!Db10Parser.TryParse(bytes, out var database, out _))
        {
            return ArchiveService.CreateUnknownAnalysis(filePath, bytes, "db_1.0 parse failed");
        }

        var entries = new List<ArchiveEntryInfo>(database.Header.Count);
        var referenced = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var record in database.Records)
        {
            var strings = record.Strings;
            var displayName = strings.FirstOrDefault(value => value.Length > 0) ?? $"record_{record.Index:D4}";
            foreach (var value in strings)
            {
                referenced.Add(value);
            }

            var detail = strings.Count > 0
                ? string.Join(" | ", strings.Take(3))
                : "no strings";

            entries.Add(new ArchiveEntryInfo(
                Name: $"#{record.Index:D4} {displayName}",
                Offset: $"0x{record.Offset:X8}",
                Length: record.Length.ToString(),
                Kind: ".dbrec",
                Details: detail));
        }

        var header = database.Header;
        return new ArchiveFileAnalysis(
            InputPath: Path.GetFullPath(filePath),
            FileSize: bytes.Length,
            TypeId: TypeId,
            TypeDisplayName: TypeDisplayName,
            IsKnownType: true,
            IsExtractable: true,
            Entries: entries,
            ReferencedNames: referenced.OrderBy(name => name, StringComparer.OrdinalIgnoreCase).ToArray(),
            Summary: $"{header.Magic}, records={header.Count}, rec-size=0x{header.RecordSize:X}, u0C=0x{header.Unknown0C:X8}, u14=0x{header.Unknown14:X8}, u18=0x{header.Unknown18:X8}");
    }

    public ArchiveExtractResult Extract(string filePath, byte[] bytes, string outputRoot, IReadOnlyDictionary<string, bool> options)
    {
        if (!Db10Parser.TryParse(bytes, out var database, out _))
        {
            return new ArchiveExtractResult(false, null, "db_1.0 parse failed");
        }

        var outDir = Path.Combine(outputRoot, Path.GetFileNameWithoutExtension(filePath));
        Directory.CreateDirectory(outDir);

        var recordItems = new List<object>(database.Header.Count);
        foreach (var record in database.Records)
        {
            recordItems.Add(new
            {
                index = record.Index,
                offset = record.Offset,
                length = record.Length,
                strings = record.Strings
            });
        }

        var header = database.Header;
        var manifest = new
        {
            inputPath = Path.GetFullPath(filePath),
            fileSize = bytes.Length,
            type = "gti-db10",
            magic = header.Magic,
            header = new
            {
                unknown0C = header.Unknown0C,
                count = header.Count,
                unknown14 = header.Unknown14,
                unknown18 = header.Unknown18
            },
            recordSize = header.RecordSize,
            records = recordItems
        };

        File.WriteAllText(Path.Combine(outDir, "db_manifest.json"), JsonSerializer.Serialize(manifest, JsonOptions));
        File.WriteAllBytes(Path.Combine(outDir, Path.GetFileName(filePath)), bytes);

        return new ArchiveExtractResult(true, outDir, $"Extracted db_1.0 metadata for {header.Count} records.");
    }

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true
    };
}
