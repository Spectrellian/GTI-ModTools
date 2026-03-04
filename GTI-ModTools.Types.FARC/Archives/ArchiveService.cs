using System.Collections.Concurrent;

namespace GTI.ModTools.FARC;

public static class ArchiveService
{
    private static readonly IReadOnlyList<IArchiveHandler> Handlers =
    [
        new FarcArchiveHandler(),
        new Db10DatabaseHandler(),
        new ExeFsArchiveHandler(),
        new NarcArchiveHandler(),
        new Sir0ArchiveHandler()
    ];

    private static readonly IReadOnlyDictionary<string, IArchiveHandler> HandlersByTypeId =
        Handlers.ToDictionary(handler => handler.TypeId, StringComparer.OrdinalIgnoreCase);

    public static IReadOnlyList<ArchiveOptionDefinition> GetOptionDefinitionsForType(string typeId)
    {
        if (HandlersByTypeId.TryGetValue(typeId, out var handler))
        {
            return handler.GetOptions();
        }

        return [];
    }

    public static IReadOnlyList<ArchiveOptionDefinition> GetAllOptionDefinitions()
    {
        return Handlers
            .SelectMany(handler => handler.GetOptions())
            .GroupBy(option => option.Key, StringComparer.OrdinalIgnoreCase)
            .Select(group => group.First())
            .OrderBy(option => option.DisplayName, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    public static IReadOnlyList<ArchiveOptionDefinition> GetOptionDefinitionsForFiles(IEnumerable<ArchiveFileAnalysis> files)
    {
        var typeIds = files
            .Where(file => file.IsKnownType)
            .Select(file => file.TypeId)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        var merged = new Dictionary<string, ArchiveOptionDefinition>(StringComparer.OrdinalIgnoreCase);
        foreach (var typeId in typeIds)
        {
            foreach (var option in GetOptionDefinitionsForType(typeId))
            {
                merged[option.Key] = option;
            }
        }

        return merged.Values
            .OrderBy(option => option.DisplayName, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    public static ArchiveScanReport Scan(string inputPath, bool recursive)
    {
        var files = ResolveBinFiles(inputPath, recursive);
        var analyses = new ConcurrentBag<ArchiveFileAnalysis>();
        var failures = new ConcurrentBag<ArchiveScanFailure>();

        Parallel.ForEach(files, file =>
        {
            try
            {
                var bytes = File.ReadAllBytes(file);
                var handler = FindHandler(file, bytes);
                if (handler is null)
                {
                    analyses.Add(CreateUnknownAnalysis(file, bytes));
                    return;
                }

                analyses.Add(handler.Analyze(file, bytes));
            }
            catch (Exception ex)
            {
                failures.Add(new ArchiveScanFailure(Path.GetFullPath(file), ex.Message));
                analyses.Add(new ArchiveFileAnalysis(
                    InputPath: Path.GetFullPath(file),
                    FileSize: 0,
                    TypeId: "unknown",
                    TypeDisplayName: "Unknown",
                    IsKnownType: false,
                    IsExtractable: false,
                    Entries: [],
                    ReferencedNames: [],
                    Summary: "Scan failure",
                    Error: ex.Message));
            }
        });

        var ordered = analyses
            .OrderBy(item => item.InputPath, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        var known = ordered.Count(item => item.IsKnownType);
        var unknown = ordered.Length - known;

        return new ArchiveScanReport(
            Scanned: ordered.Length,
            Known: known,
            Unknown: unknown,
            Files: ordered,
            Failures: failures.OrderBy(item => item.Path, StringComparer.OrdinalIgnoreCase).ToArray());
    }

    public static ArchiveExtractReport ExtractAll(
        string inputPath,
        string outputRoot,
        bool recursive,
        IReadOnlyDictionary<string, bool>? options = null)
    {
        var scan = Scan(inputPath, recursive);
        var inputRoot = GetInputRoot(inputPath);
        return ExtractFiles(scan.Files, inputRoot, outputRoot, options);
    }

    public static ArchiveExtractReport ExtractFiles(
        IEnumerable<ArchiveFileAnalysis> files,
        string inputRoot,
        string outputRoot,
        IReadOnlyDictionary<string, bool>? options = null)
    {
        Directory.CreateDirectory(outputRoot);
        var fileArray = files.ToArray();
        var failureBag = new ConcurrentBag<ArchiveExtractFailure>();
        var outputDirs = new ConcurrentBag<string>();
        var scanned = 0;
        var extracted = 0;
        var skippedUnknown = 0;

        var optionMap = options ?? new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase);
        var fullInputRoot = Path.GetFullPath(inputRoot);

        Parallel.ForEach(fileArray, file =>
        {
            Interlocked.Increment(ref scanned);

            if (!file.IsKnownType || !file.IsExtractable)
            {
                Interlocked.Increment(ref skippedUnknown);
                return;
            }

            if (!HandlersByTypeId.TryGetValue(file.TypeId, out var handler))
            {
                Interlocked.Increment(ref skippedUnknown);
                return;
            }

            try
            {
                var bytes = File.ReadAllBytes(file.InputPath);
                var relative = Path.GetRelativePath(fullInputRoot, file.InputPath);
                var relativeDirectory = Path.GetDirectoryName(relative);
                var perFileOutputRoot = string.IsNullOrWhiteSpace(relativeDirectory)
                    ? outputRoot
                    : Path.Combine(outputRoot, relativeDirectory);

                var result = handler.Extract(file.InputPath, bytes, perFileOutputRoot, optionMap);
                if (result.Extracted)
                {
                    Interlocked.Increment(ref extracted);
                    if (!string.IsNullOrWhiteSpace(result.OutputDirectory))
                    {
                        outputDirs.Add(result.OutputDirectory);
                    }
                }
            }
            catch (Exception ex)
            {
                failureBag.Add(new ArchiveExtractFailure(file.InputPath, ex.Message));
            }
        });

        return new ArchiveExtractReport(
            Scanned: scanned,
            Extracted: extracted,
            SkippedUnknown: skippedUnknown,
            Failed: failureBag.OrderBy(item => item.Path, StringComparer.OrdinalIgnoreCase).ToArray(),
            OutputDirectories: outputDirs
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
                .ToArray());
    }

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

    public static string GetInputRoot(string inputPath)
    {
        if (Directory.Exists(inputPath))
        {
            return Path.GetFullPath(inputPath);
        }

        var full = Path.GetFullPath(inputPath);
        return Path.GetDirectoryName(full) ?? Directory.GetCurrentDirectory();
    }

    internal static ArchiveFileAnalysis CreateUnknownAnalysis(string filePath, byte[] bytes, string? summaryOverride = null)
    {
        var magic = bytes.Length >= 4
            ? BitConverter.ToString(bytes.AsSpan(0, 4).ToArray()).Replace("-", string.Empty)
            : BitConverter.ToString(bytes).Replace("-", string.Empty);

        return new ArchiveFileAnalysis(
            InputPath: Path.GetFullPath(filePath),
            FileSize: bytes.Length,
            TypeId: "unknown",
            TypeDisplayName: "Unknown",
            IsKnownType: false,
            IsExtractable: false,
            Entries: [],
            ReferencedNames: [],
            Summary: summaryOverride ?? $"magic={magic}");
    }

    private static IArchiveHandler? FindHandler(string filePath, byte[] bytes)
    {
        var extension = Path.GetExtension(filePath);
        foreach (var handler in Handlers)
        {
            if (handler.CanHandle(bytes, extension))
            {
                return handler;
            }
        }

        return null;
    }
}
