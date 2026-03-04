using GTI.ModTools.FARC;

namespace GTI.ModTools.FARC.CLI;

internal static class Program
{
    public static int Main(string[] args)
    {
        if (args.Length == 0 || args.Any(IsHelpFlag))
        {
            Console.WriteLine(HelpText);
            return 0;
        }

        var command = args[0];
        try
        {
            return command.ToLowerInvariant() switch
            {
                "scan" => RunScan(args),
                "extract" => RunExtract(args),
                _ => HandleUnknownCommand(command)
            };
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine(ex.Message);
            return 1;
        }
    }

    private static int RunScan(string[] args)
    {
        if (args.Length < 2)
        {
            Console.Error.WriteLine("Missing required arguments for scan.");
            Console.Error.WriteLine();
            Console.Error.WriteLine(HelpText);
            return 1;
        }

        var inputPath = Path.GetFullPath(args[1]);
        var recursive = args.Any(arg => string.Equals(arg, "--recursive", StringComparison.OrdinalIgnoreCase));

        var report = ArchiveService.Scan(inputPath, recursive);
        Console.WriteLine($"Scanned BIN files: {report.Scanned}");
        Console.WriteLine($"Known archive types: {report.Known}");
        Console.WriteLine($"Unknown files: {report.Unknown}");
        Console.WriteLine($"Scan failures: {report.Failures.Count}");

        var grouped = report.Files
            .GroupBy(file => file.TypeDisplayName)
            .OrderBy(group => group.Key, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        if (grouped.Length > 0)
        {
            Console.WriteLine();
            Console.WriteLine("Types:");
            foreach (var group in grouped)
            {
                Console.WriteLine($"  {group.Key}: {group.Count()}");
            }
        }

        if (report.Failures.Count > 0)
        {
            Console.Error.WriteLine();
            foreach (var failure in report.Failures.Take(20))
            {
                Console.Error.WriteLine($"{failure.Path}: {failure.Error}");
            }

            if (report.Failures.Count > 20)
            {
                Console.Error.WriteLine($"... and {report.Failures.Count - 20} more");
            }
        }

        return report.Failures.Count == 0 ? 0 : 2;
    }

    private static int RunExtract(string[] args)
    {
        if (args.Length < 3)
        {
            Console.Error.WriteLine("Missing required arguments for extract.");
            Console.Error.WriteLine();
            Console.Error.WriteLine(HelpText);
            return 1;
        }

        var inputPath = Path.GetFullPath(args[1]);
        var outputRoot = Path.GetFullPath(args[2]);
        var recursive = args.Any(arg => string.Equals(arg, "--recursive", StringComparison.OrdinalIgnoreCase));
        var options = ParseOptions(args.Skip(3));

        var report = ArchiveService.ExtractAll(inputPath, outputRoot, recursive, options);

        Console.WriteLine($"Scanned BIN files: {report.Scanned}");
        Console.WriteLine($"Extracted archives: {report.Extracted}");
        Console.WriteLine($"Skipped unknown/non-extractable: {report.SkippedUnknown}");
        Console.WriteLine($"Failed files: {report.Failed.Count}");

        if (report.Failed.Count > 0)
        {
            Console.Error.WriteLine();
            foreach (var failure in report.Failed.Take(20))
            {
                Console.Error.WriteLine($"{failure.Path}: {failure.Error}");
            }

            if (report.Failed.Count > 20)
            {
                Console.Error.WriteLine($"... and {report.Failed.Count - 20} more");
            }
        }

        return report.Failed.Count == 0 ? 0 : 2;
    }

    private static Dictionary<string, bool> ParseOptions(IEnumerable<string> args)
    {
        var knownOptionFlags = ArchiveService
            .GetAllOptionDefinitions()
            .ToDictionary(option => option.Key, option => option, StringComparer.OrdinalIgnoreCase);

        var map = new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase);
        foreach (var option in knownOptionFlags.Values)
        {
            map[option.Key] = option.DefaultValue;
        }

        foreach (var arg in args)
        {
            if (!arg.StartsWith("--", StringComparison.Ordinal))
            {
                continue;
            }

            var key = arg[2..].Trim();
            if (knownOptionFlags.ContainsKey(key))
            {
                map[key] = true;
            }
        }

        return map;
    }

    private static int HandleUnknownCommand(string command)
    {
        Console.Error.WriteLine($"Unknown command: {command}");
        Console.Error.WriteLine();
        Console.Error.WriteLine(HelpText);
        return 1;
    }

    private static bool IsHelpFlag(string arg) =>
        string.Equals(arg, "--help", StringComparison.OrdinalIgnoreCase) ||
        string.Equals(arg, "-h", StringComparison.OrdinalIgnoreCase) ||
        string.Equals(arg, "/?", StringComparison.OrdinalIgnoreCase);

    private const string HelpText =
        """
        GTI-ModTools.FARC.CLI

        Usage:
          GTI-ModTools.FARC.CLI scan <inputPath> [--recursive]
          GTI-ModTools.FARC.CLI extract <inputPath> <outputDirectory> [--recursive] [--carve-bch]

        Notes:
          - inputPath can be a single .bin file or a folder.
          - scan auto-detects known binary types (archives/databases) and reports unknown files.
          - extract auto-detects per file and runs the matching extractor/manifest exporter.
          - unknown/non-extractable files are listed and skipped.
          - --carve-bch applies to FARC extraction.
        """;
}
