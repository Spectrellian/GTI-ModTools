using System.Collections.Concurrent;
using System.Text;
using System.Text.Json;

namespace GTI.ModTools.FARC;

public readonly record struct FarcSectionInfo(
    int Index,
    int Offset,
    int Length,
    string MagicHex,
    string SuggestedExtension,
    int ReferencedNameCount);

public sealed record FarcFileAnalysis(
    string InputPath,
    int FileSize,
    bool IsFarc,
    IReadOnlyList<FarcSectionInfo> Sections,
    IReadOnlyList<string> ReferencedNames,
    string? Error = null);

public readonly record struct FarcExtractFailure(string Path, string Error);

public readonly record struct FarcExtractReport(
    int Scanned,
    int Extracted,
    int SkippedNonFarc,
    IReadOnlyList<FarcExtractFailure> Failed,
    IReadOnlyList<string> OutputDirectories);

public static class FarcService
{
    public static IReadOnlyList<string> ResolveBinFiles(string inputPath, bool recursive)
    {
        if (File.Exists(inputPath))
        {
            if (!inputPath.EndsWith(".bin", StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException($"Input file must be a .bin file: {inputPath}");
            }

            return [Path.GetFullPath(inputPath)];
        }

        if (!Directory.Exists(inputPath))
        {
            throw new DirectoryNotFoundException($"Input path not found: {inputPath}");
        }

        var searchOption = recursive ? SearchOption.AllDirectories : SearchOption.TopDirectoryOnly;
        return
        [
            .. Directory.EnumerateFiles(inputPath, "*.bin", searchOption)
                .Select(Path.GetFullPath)
                .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
        ];
    }

    public static FarcFileAnalysis AnalyzeFile(string binPath)
    {
        var fullPath = Path.GetFullPath(binPath);
        var bytes = File.ReadAllBytes(fullPath);
        return AnalyzeBytes(fullPath, bytes);
    }

    public static FarcFileAnalysis AnalyzeBytes(string inputPath, byte[] bytes)
    {
        if (!FarcArchive.TryParse(bytes, out var archive))
        {
            return new FarcFileAnalysis(
                InputPath: Path.GetFullPath(inputPath),
                FileSize: bytes.Length,
                IsFarc: false,
                Sections: [],
                ReferencedNames: []);
        }

        var sectionInfos = new List<FarcSectionInfo>(archive.Sections.Count);
        var referencedNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        for (var i = 0; i < archive.Sections.Count; i++)
        {
            var section = archive.Sections[i];
            var sectionData = bytes.AsSpan(section.Offset, section.Length).ToArray();
            var magicHex = BitConverter.ToString(sectionData.AsSpan(0, Math.Min(4, sectionData.Length)).ToArray()).Replace("-", string.Empty);
            var names = ScanReferencedNames(sectionData);
            foreach (var name in names)
            {
                referencedNames.Add(name);
            }

            sectionInfos.Add(new FarcSectionInfo(
                Index: i,
                Offset: section.Offset,
                Length: section.Length,
                MagicHex: magicHex,
                SuggestedExtension: GuessExtension(sectionData),
                ReferencedNameCount: names.Count));
        }

        return new FarcFileAnalysis(
            InputPath: Path.GetFullPath(inputPath),
            FileSize: bytes.Length,
            IsFarc: true,
            Sections: sectionInfos,
            ReferencedNames: referencedNames.OrderBy(name => name, StringComparer.OrdinalIgnoreCase).ToArray());
    }

    public static IReadOnlyList<FarcFileAnalysis> AnalyzeMany(string inputPath, bool recursive)
    {
        var files = ResolveBinFiles(inputPath, recursive);
        var analyses = new ConcurrentBag<FarcFileAnalysis>();

        Parallel.ForEach(files, file =>
        {
            try
            {
                analyses.Add(AnalyzeFile(file));
            }
            catch (Exception ex)
            {
                analyses.Add(new FarcFileAnalysis(
                    InputPath: Path.GetFullPath(file),
                    FileSize: 0,
                    IsFarc: false,
                    Sections: [],
                    ReferencedNames: [],
                    Error: ex.Message));
            }
        });

        return analyses.OrderBy(item => item.InputPath, StringComparer.OrdinalIgnoreCase).ToArray();
    }

    public static FarcExtractReport ExtractAll(string inputPath, string outputRoot, bool recursive, bool carveBch)
    {
        var inputFiles = ResolveBinFiles(inputPath, recursive);
        if (inputFiles.Count == 0)
        {
            throw new InvalidOperationException($"No .bin files found at: {inputPath}");
        }

        Directory.CreateDirectory(outputRoot);

        var failures = new ConcurrentBag<FarcExtractFailure>();
        var outputDirectories = new ConcurrentBag<string>();
        var scanned = 0;
        var extracted = 0;
        var skippedNonFarc = 0;

        var inputRoot = Directory.Exists(inputPath)
            ? Path.GetFullPath(inputPath)
            : Path.GetDirectoryName(Path.GetFullPath(inputPath)) ?? Directory.GetCurrentDirectory();

        Parallel.ForEach(inputFiles, path =>
        {
            Interlocked.Increment(ref scanned);
            try
            {
                var bytes = File.ReadAllBytes(path);
                if (!FarcArchive.TryParse(bytes, out var archive))
                {
                    Interlocked.Increment(ref skippedNonFarc);
                    return;
                }

                var outputDirectory = GetOutputDirectory(outputRoot, inputRoot, path);
                ExtractArchive(path, bytes, archive, outputDirectory, carveBch);
                outputDirectories.Add(outputDirectory);
                Interlocked.Increment(ref extracted);
            }
            catch (Exception ex)
            {
                failures.Add(new FarcExtractFailure(path, ex.Message));
            }
        });

        return new FarcExtractReport(
            Scanned: scanned,
            Extracted: extracted,
            SkippedNonFarc: skippedNonFarc,
            Failed: failures.OrderBy(f => f.Path, StringComparer.OrdinalIgnoreCase).ToList(),
            OutputDirectories: outputDirectories.OrderBy(path => path, StringComparer.OrdinalIgnoreCase).Distinct(StringComparer.OrdinalIgnoreCase).ToList());
    }

    public static FarcExtractReport ExtractSelectedFiles(IEnumerable<string> inputFiles, string outputRoot, bool carveBch)
    {
        var files = inputFiles.Select(Path.GetFullPath).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        if (files.Length == 0)
        {
            return new FarcExtractReport(0, 0, 0, [], []);
        }

        Directory.CreateDirectory(outputRoot);

        var failures = new ConcurrentBag<FarcExtractFailure>();
        var outputDirectories = new ConcurrentBag<string>();
        var scanned = 0;
        var extracted = 0;
        var skippedNonFarc = 0;

        var inputRoot = Path.GetDirectoryName(files[0]) ?? Directory.GetCurrentDirectory();

        Parallel.ForEach(files, path =>
        {
            Interlocked.Increment(ref scanned);
            try
            {
                var bytes = File.ReadAllBytes(path);
                if (!FarcArchive.TryParse(bytes, out var archive))
                {
                    Interlocked.Increment(ref skippedNonFarc);
                    return;
                }

                var outputDirectory = GetOutputDirectory(outputRoot, inputRoot, path);
                ExtractArchive(path, bytes, archive, outputDirectory, carveBch);
                outputDirectories.Add(outputDirectory);
                Interlocked.Increment(ref extracted);
            }
            catch (Exception ex)
            {
                failures.Add(new FarcExtractFailure(path, ex.Message));
            }
        });

        return new FarcExtractReport(
            Scanned: scanned,
            Extracted: extracted,
            SkippedNonFarc: skippedNonFarc,
            Failed: failures.OrderBy(f => f.Path, StringComparer.OrdinalIgnoreCase).ToList(),
            OutputDirectories: outputDirectories.OrderBy(path => path, StringComparer.OrdinalIgnoreCase).Distinct(StringComparer.OrdinalIgnoreCase).ToList());
    }

    private static string GetOutputDirectory(string outputRoot, string inputRoot, string inputFilePath)
    {
        var relative = Path.GetRelativePath(inputRoot, Path.GetFullPath(inputFilePath));
        var withoutExtension = Path.ChangeExtension(relative, null) ?? relative;
        return Path.Combine(outputRoot, withoutExtension);
    }

    private static void ExtractArchive(string inputPath, byte[] bytes, FarcArchive archive, string outputDirectory, bool carveBch)
    {
        Directory.CreateDirectory(outputDirectory);
        var sections = new List<FarcSectionManifest>();
        var referencedNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var namedExtractionEntries = new List<NamedExtractionReportEntry>();
        var namedOutputNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var namedOutputDirectory = Path.Combine(outputDirectory, "Extracted");

        for (var i = 0; i < archive.Sections.Count; i++)
        {
            var section = archive.Sections[i];
            var sectionData = bytes.AsSpan(section.Offset, section.Length).ToArray();
            var magicHex = BitConverter.ToString(sectionData.AsSpan(0, Math.Min(4, sectionData.Length)).ToArray()).Replace("-", "");
            var extension = GuessExtension(sectionData);
            var outputName = $"section_{i:D4}_0x{section.Offset:X8}{extension}";
            var outputPath = Path.Combine(outputDirectory, outputName);
            File.WriteAllBytes(outputPath, sectionData);

            var names = ScanReferencedNames(sectionData)
                .OrderBy(name => name, StringComparer.OrdinalIgnoreCase)
                .ToArray();
            foreach (var name in names)
            {
                referencedNames.Add(name);
            }

            if (names.Length > 0)
            {
                var namesPath = Path.Combine(outputDirectory, $"section_{i:D4}_names.txt");
                File.WriteAllLines(namesPath, names);
            }

            EmitNamedExtractedFile(
                sectionIndex: i,
                sectionLength: section.Length,
                sectionData: sectionData,
                sectionExtension: extension,
                names: names,
                namedOutputDirectory: namedOutputDirectory,
                usedNames: namedOutputNames,
                entries: namedExtractionEntries);

            var carvedCount = 0;
            if (carveBch)
            {
                carvedCount = CarveBch(sectionData, section.Offset, Path.Combine(outputDirectory, $"section_{i:D4}_carved_bch"));
            }

            sections.Add(new FarcSectionManifest(
                i,
                section.Offset,
                section.Length,
                magicHex,
                outputName,
                names.Length,
                carvedCount));
        }

        var manifest = new FarcManifest(
            InputPath: Path.GetFullPath(inputPath),
            FileSize: bytes.Length,
            SectionCount: archive.Sections.Count,
            Sections: sections,
            ReferencedNames: referencedNames.OrderBy(name => name, StringComparer.OrdinalIgnoreCase).ToArray());

        var manifestPath = Path.Combine(outputDirectory, "manifest.json");
        File.WriteAllText(manifestPath, JsonSerializer.Serialize(manifest, JsonOptions));

        if (namedExtractionEntries.Count > 0)
        {
            Directory.CreateDirectory(namedOutputDirectory);
            var reportPath = Path.Combine(namedOutputDirectory, "named_mapping_report.json");
            File.WriteAllText(reportPath, JsonSerializer.Serialize(namedExtractionEntries, JsonOptions));
        }
    }

    private static void EmitNamedExtractedFile(
        int sectionIndex,
        int sectionLength,
        byte[] sectionData,
        string sectionExtension,
        IReadOnlyList<string> names,
        string namedOutputDirectory,
        HashSet<string> usedNames,
        List<NamedExtractionReportEntry> entries)
    {
        if (names.Count == 0)
        {
            return;
        }

        var matchingExtensionNames = names
            .Where(name => string.Equals(Path.GetExtension(name), sectionExtension, StringComparison.OrdinalIgnoreCase))
            .ToArray();

        string? selectedName = null;
        string status;
        string reason;

        if (matchingExtensionNames.Length == 1)
        {
            selectedName = matchingExtensionNames[0];
            status = "extracted";
            reason = $"single extension match ({sectionExtension})";
        }
        else if (names.Count == 1)
        {
            selectedName = names[0];
            status = "extracted";
            reason = "single referenced name";
        }
        else
        {
            status = "skipped";
            reason = matchingExtensionNames.Length > 1
                ? $"ambiguous: {matchingExtensionNames.Length} extension matches ({sectionExtension})"
                : $"ambiguous: {names.Count} referenced names";
        }

        string? writtenPath = null;
        if (!string.IsNullOrWhiteSpace(selectedName))
        {
            Directory.CreateDirectory(namedOutputDirectory);
            var outputName = GetUniqueExtractedName(SanitizeFileName(selectedName), usedNames);
            writtenPath = Path.Combine(namedOutputDirectory, outputName);
            File.WriteAllBytes(writtenPath, sectionData);
        }

        entries.Add(new NamedExtractionReportEntry(
            sectionIndex,
            sectionLength,
            sectionExtension,
            selectedName,
            writtenPath,
            status,
            reason,
            names));
    }

    private static string GetUniqueExtractedName(string fileName, HashSet<string> usedNames)
    {
        if (usedNames.Add(fileName))
        {
            return fileName;
        }

        var stem = Path.GetFileNameWithoutExtension(fileName);
        var extension = Path.GetExtension(fileName);
        for (var i = 1; i <= 9999; i++)
        {
            var candidate = $"{stem}_{i:D4}{extension}";
            if (usedNames.Add(candidate))
            {
                return candidate;
            }
        }

        var fallback = $"{stem}_{Guid.NewGuid():N}{extension}";
        usedNames.Add(fallback);
        return fallback;
    }

    private static string SanitizeFileName(string fileName)
    {
        var baseName = Path.GetFileName(fileName.Trim());
        if (string.IsNullOrWhiteSpace(baseName))
        {
            return "unnamed.bin";
        }

        var invalid = Path.GetInvalidFileNameChars();
        var chars = baseName
            .Select(ch => invalid.Contains(ch) ? '_' : ch)
            .ToArray();
        return new string(chars);
    }

    private static int CarveBch(byte[] sectionData, int sectionBaseOffset, string outputDirectory)
    {
        var offsets = new List<int>();
        for (var i = 0; i + 3 < sectionData.Length; i++)
        {
            if (sectionData[i] == (byte)'B' &&
                sectionData[i + 1] == (byte)'C' &&
                sectionData[i + 2] == (byte)'H' &&
                sectionData[i + 3] == 0x00)
            {
                offsets.Add(i);
            }
        }

        if (offsets.Count == 0)
        {
            return 0;
        }

        Directory.CreateDirectory(outputDirectory);
        var written = 0;
        for (var i = 0; i < offsets.Count; i++)
        {
            var start = offsets[i];
            var end = i + 1 < offsets.Count ? offsets[i + 1] : sectionData.Length;
            if (end <= start)
            {
                continue;
            }

            var length = end - start;
            if (length < 0x20)
            {
                continue;
            }

            var outputName = $"bch_{i:D4}_0x{(sectionBaseOffset + start):X8}.bch";
            var outputPath = Path.Combine(outputDirectory, outputName);
            File.WriteAllBytes(outputPath, sectionData.AsSpan(start, length).ToArray());
            written++;
        }

        return written;
    }

    private static IReadOnlyCollection<string> ScanReferencedNames(byte[] data)
    {
        var result = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var value in ScanUtf16Strings(data))
        {
            var normalized = NormalizeCandidateName(value);
            if (!string.IsNullOrWhiteSpace(normalized))
            {
                result.Add(normalized);
            }
        }

        foreach (var value in ScanAsciiStrings(data))
        {
            var normalized = NormalizeCandidateName(value);
            if (!string.IsNullOrWhiteSpace(normalized))
            {
                result.Add(normalized);
            }
        }

        return result;
    }

    private static IEnumerable<string> ScanUtf16Strings(byte[] data)
    {
        for (var i = 0; i + 3 < data.Length; i++)
        {
            if (data[i + 1] != 0 || !IsPrintableAscii(data[i]))
            {
                continue;
            }

            var chars = new List<byte>();
            var j = i;
            while (j + 1 < data.Length)
            {
                if (data[j] == 0 && data[j + 1] == 0)
                {
                    break;
                }

                if (data[j + 1] != 0 || !IsPrintableAscii(data[j]))
                {
                    chars.Clear();
                    break;
                }

                chars.Add(data[j]);
                j += 2;
            }

            if (chars.Count >= 4 && j + 1 < data.Length && data[j] == 0 && data[j + 1] == 0)
            {
                yield return Encoding.ASCII.GetString(chars.ToArray());
                i = j + 1;
            }
        }
    }

    private static IEnumerable<string> ScanAsciiStrings(byte[] data)
    {
        for (var i = 0; i < data.Length; i++)
        {
            if (!IsPrintableAscii(data[i]))
            {
                continue;
            }

            var chars = new List<byte>();
            var j = i;
            while (j < data.Length && IsPrintableAscii(data[j]))
            {
                chars.Add(data[j]);
                j++;
            }

            if (chars.Count >= 4)
            {
                yield return Encoding.ASCII.GetString(chars.ToArray());
                i = j;
            }
        }
    }

    private static string NormalizeCandidateName(string value)
    {
        var trimmed = value.Trim().Trim('"', '\'');
        if (string.IsNullOrWhiteSpace(trimmed) || trimmed.Length > 260)
        {
            return string.Empty;
        }

        var extension = Path.GetExtension(trimmed).ToLowerInvariant();
        if (KnownNameExtensions.Contains(extension))
        {
            return Path.GetFileName(trimmed);
        }

        return string.Empty;
    }

    private static string GuessExtension(ReadOnlySpan<byte> bytes)
    {
        if (bytes.Length >= 4)
        {
            if (bytes[0] == (byte)'S' && bytes[1] == (byte)'I' && bytes[2] == (byte)'R' && bytes[3] == (byte)'0')
            {
                return ".sir0";
            }

            if (bytes[0] == (byte)'F' && bytes[1] == (byte)'A' && bytes[2] == (byte)'R' && bytes[3] == (byte)'C')
            {
                return ".farc";
            }

            if (bytes[0] == (byte)'D' && bytes[1] == (byte)'V' && bytes[2] == (byte)'L' && bytes[3] == (byte)'B')
            {
                return ".dvlb";
            }

            if (bytes[0] == (byte)'B' && bytes[1] == (byte)'C' && bytes[2] == (byte)'H' && bytes[3] == 0)
            {
                return ".bch";
            }

            if (bytes[0] == (byte)'C' && bytes[1] == (byte)'G' && bytes[2] == (byte)'F' && bytes[3] == (byte)'X')
            {
                return ".cgfx";
            }

            if (bytes[0] == 0x00 && bytes[1] == 0x63 && bytes[2] == 0x74 && bytes[3] == 0x65)
            {
                return ".img";
            }
        }

        return ".bin";
    }

    private static bool IsPrintableAscii(byte value) => value is >= 32 and <= 126;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true
    };

    private static readonly HashSet<string> KnownNameExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".bin",
        ".bch",
        ".bgrs",
        ".bchmata",
        ".img",
        ".bsji",
        ".bcres",
        ".bcmdl",
        ".bclim",
        ".bcstm",
        ".bcsar",
        ".cgfx"
    };

    private sealed record FarcArchive(IReadOnlyList<FarcSection> Sections)
    {
        public static bool TryParse(ReadOnlySpan<byte> bytes, out FarcArchive archive)
        {
            archive = default!;
            if (bytes.Length < 0x24)
            {
                return false;
            }

            if (!(bytes[0] == (byte)'F' && bytes[1] == (byte)'A' && bytes[2] == (byte)'R' && bytes[3] == (byte)'C'))
            {
                return false;
            }

            var sectionCount = BitConverter.ToUInt32(bytes.Slice(0x20, 4));
            if (sectionCount == 0 || sectionCount > 1024)
            {
                return false;
            }

            var tableLength = checked((int)sectionCount * 4);
            if (0x24 + tableLength > bytes.Length)
            {
                return false;
            }

            var offsets = new List<int>((int)sectionCount);
            for (var i = 0; i < sectionCount; i++)
            {
                var offset = checked((int)BitConverter.ToUInt32(bytes.Slice(0x24 + i * 4, 4)));
                if (offset < 0 || offset >= bytes.Length)
                {
                    return false;
                }

                offsets.Add(offset);
            }

            for (var i = 1; i < offsets.Count; i++)
            {
                if (offsets[i] < offsets[i - 1])
                {
                    return false;
                }
            }

            var sections = new List<FarcSection>(offsets.Count);
            for (var i = 0; i < offsets.Count; i++)
            {
                var start = offsets[i];
                var end = i + 1 < offsets.Count ? offsets[i + 1] : bytes.Length;
                if (end <= start)
                {
                    return false;
                }

                sections.Add(new FarcSection(start, end - start));
            }

            archive = new FarcArchive(sections);
            return true;
        }
    }

    private readonly record struct FarcSection(int Offset, int Length);
    private readonly record struct FarcSectionManifest(
        int Index,
        int Offset,
        int Length,
        string MagicHex,
        string OutputFile,
        int ReferencedNameCount,
        int CarvedBchCount);
    private readonly record struct NamedExtractionReportEntry(
        int SectionIndex,
        int SectionLength,
        string SectionExtension,
        string? SelectedName,
        string? OutputPath,
        string Status,
        string Reason,
        IReadOnlyList<string> CandidateNames);
    private readonly record struct FarcManifest(
        string InputPath,
        int FileSize,
        int SectionCount,
        IReadOnlyList<FarcSectionManifest> Sections,
        IReadOnlyList<string> ReferencedNames);
}
