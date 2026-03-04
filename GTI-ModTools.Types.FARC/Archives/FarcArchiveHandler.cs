namespace GTI.ModTools.FARC;

public sealed class FarcArchiveHandler : IArchiveHandler
{
    public const string CarveBchOptionKey = "carve-bch";

    public string TypeId => "farc";
    public string TypeDisplayName => "FARC";

    public bool CanHandle(ReadOnlySpan<byte> bytes, string extension)
    {
        return bytes.Length >= 4 &&
               bytes[0] == (byte)'F' &&
               bytes[1] == (byte)'A' &&
               bytes[2] == (byte)'R' &&
               bytes[3] == (byte)'C';
    }

    public IReadOnlyList<ArchiveOptionDefinition> GetOptions()
    {
        return
        [
            new ArchiveOptionDefinition(
                CarveBchOptionKey,
                "Carve BCH",
                "Scan extracted sections for embedded BCH and write carved .bch files.")
        ];
    }

    public ArchiveFileAnalysis Analyze(string filePath, byte[] bytes)
    {
        var analysis = FarcService.AnalyzeBytes(filePath, bytes);
        if (!analysis.IsFarc)
        {
            return ArchiveService.CreateUnknownAnalysis(filePath, bytes, "Failed to parse FARC header/table.");
        }

        var entries = analysis.Sections
            .Select(section => new ArchiveEntryInfo(
                Name: $"section_{section.Index:D4}",
                Offset: $"0x{section.Offset:X8}",
                Length: section.Length.ToString(),
                Kind: section.SuggestedExtension,
                Details: $"magic={section.MagicHex}, names={section.ReferencedNameCount}"))
            .ToArray();

        return new ArchiveFileAnalysis(
            InputPath: analysis.InputPath,
            FileSize: analysis.FileSize,
            TypeId: TypeId,
            TypeDisplayName: TypeDisplayName,
            IsKnownType: true,
            IsExtractable: true,
            Entries: entries,
            ReferencedNames: analysis.ReferencedNames,
            Summary: $"sections={analysis.Sections.Count}, referenced-names={analysis.ReferencedNames.Count}");
    }

    public ArchiveExtractResult Extract(string filePath, byte[] bytes, string outputRoot, IReadOnlyDictionary<string, bool> options)
    {
        var carveBch = options.TryGetValue(CarveBchOptionKey, out var enabled) && enabled;
        var report = FarcService.ExtractSelectedFiles([filePath], outputRoot, carveBch);
        var extracted = report.Extracted > 0;
        var outputDir = report.OutputDirectories.FirstOrDefault();

        return new ArchiveExtractResult(
            Extracted: extracted,
            OutputDirectory: outputDir,
            Summary: extracted
                ? $"Extracted {report.Extracted} FARC file(s)."
                : "No extractable FARC content found.");
    }
}
