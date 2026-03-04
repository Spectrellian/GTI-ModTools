namespace GTI.ModTools.FARC;

public readonly record struct ArchiveOptionDefinition(
    string Key,
    string DisplayName,
    string Description,
    bool DefaultValue = false);

public readonly record struct ArchiveEntryInfo(
    string Name,
    string Offset,
    string Length,
    string Kind,
    string Details);

public sealed record ArchiveFileAnalysis(
    string InputPath,
    int FileSize,
    string TypeId,
    string TypeDisplayName,
    bool IsKnownType,
    bool IsExtractable,
    IReadOnlyList<ArchiveEntryInfo> Entries,
    IReadOnlyList<string> ReferencedNames,
    string Summary,
    string? Error = null);

public readonly record struct ArchiveScanFailure(string Path, string Error);

public readonly record struct ArchiveScanReport(
    int Scanned,
    int Known,
    int Unknown,
    IReadOnlyList<ArchiveFileAnalysis> Files,
    IReadOnlyList<ArchiveScanFailure> Failures);

public readonly record struct ArchiveExtractFailure(string Path, string Error);

public readonly record struct ArchiveExtractResult(bool Extracted, string? OutputDirectory, string Summary);

public readonly record struct ArchiveExtractReport(
    int Scanned,
    int Extracted,
    int SkippedUnknown,
    IReadOnlyList<ArchiveExtractFailure> Failed,
    IReadOnlyList<string> OutputDirectories);

public interface IArchiveHandler
{
    string TypeId { get; }
    string TypeDisplayName { get; }
    bool CanHandle(ReadOnlySpan<byte> bytes, string extension);
    IReadOnlyList<ArchiveOptionDefinition> GetOptions();
    ArchiveFileAnalysis Analyze(string filePath, byte[] bytes);
    ArchiveExtractResult Extract(string filePath, byte[] bytes, string outputRoot, IReadOnlyDictionary<string, bool> options);
}
