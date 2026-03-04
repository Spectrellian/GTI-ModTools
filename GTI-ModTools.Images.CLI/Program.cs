using GTI.ModTools.Images;

namespace GTI.ModTools.Images.CLI;

public static class Program
{
    public static int Main(string[] args)
    {
        EnsureDefaultWorkingDirectories();

        if (TryHandleBaseOrganizeCommand(args, out var organizeExitCode))
        {
            return organizeExitCode;
        }

        if (args.Any(arg => string.Equals(arg, "--help", StringComparison.OrdinalIgnoreCase) || string.Equals(arg, "-h", StringComparison.OrdinalIgnoreCase)))
        {
            Console.WriteLine(CliOptions.HelpText);
            return 0;
        }

        try
        {
            var options = CliOptions.Parse(args);
            var report = ImgConverter.ConvertWithReport(options);

            Console.WriteLine($"Converted {report.Converted.Count} file(s).");
            foreach (var item in report.Converted)
            {
                Console.WriteLine($"{item.InputPath} -> {item.OutputPath}");
            }

            if (report.Failed.Count > 0)
            {
                Console.Error.WriteLine();
                Console.Error.WriteLine($"Skipped {report.Failed.Count} file(s) due to errors:");
                foreach (var failure in report.Failed.Take(20))
                {
                    Console.Error.WriteLine($"{failure.InputPath}: {failure.Error}");
                }

                if (report.Failed.Count > 20)
                {
                    Console.Error.WriteLine($"... and {report.Failed.Count - 20} more");
                }
            }

            return report.Failed.Count == 0 ? 0 : 2;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine(ex.Message);
            Console.Error.WriteLine();
            Console.Error.WriteLine(CliOptions.HelpText);
            return 1;
        }
    }

    private static void EnsureDefaultWorkingDirectories()
    {
        var cwd = Directory.GetCurrentDirectory();
        ImageConversionDefaults.EnsureWorkspaceFolders(cwd);
    }

    private static bool TryHandleBaseOrganizeCommand(string[] args, out int exitCode)
    {
        exitCode = 0;
        if (!args.Any(arg => string.Equals(arg, "--organize-base-unmapped", StringComparison.OrdinalIgnoreCase)))
        {
            return false;
        }

        try
        {
            var basePath = TryGetOptionValue(args, "--base") ??
                ImageConversionDefaults.GetDefaultBaseDirectory(Directory.GetCurrentDirectory());
            var report = BaseOrganizer.MoveUnmappedImages(basePath);

            Console.WriteLine($"Scanned {report.TotalImagesScanned} IMG file(s) in base.");
            Console.WriteLine($"Referenced by BSJI: {report.ReferencedImages}");
            Console.WriteLine($"Unmapped candidates: {report.UnmappedCandidates}");
            Console.WriteLine($"Moved to Unmapped: {report.Moved.Count}");
            foreach (var moved in report.Moved.Take(50))
            {
                Console.WriteLine($"{moved.InputPath} -> {moved.OutputPath}");
            }

            if (report.Moved.Count > 50)
            {
                Console.WriteLine($"... and {report.Moved.Count - 50} more");
            }

            if (report.Failed.Count > 0)
            {
                Console.Error.WriteLine();
                Console.Error.WriteLine($"Organizer warnings/errors: {report.Failed.Count}");
                foreach (var failure in report.Failed.Take(20))
                {
                    Console.Error.WriteLine($"{failure.InputPath}: {failure.Error}");
                }

                if (report.Failed.Count > 20)
                {
                    Console.Error.WriteLine($"... and {report.Failed.Count - 20} more");
                }
            }

            exitCode = report.Failed.Count == 0 ? 0 : 2;
            return true;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine(ex.Message);
            exitCode = 1;
            return true;
        }
    }

    private static string? TryGetOptionValue(string[] args, string optionName)
    {
        for (var i = 0; i < args.Length - 1; i++)
        {
            if (string.Equals(args[i], optionName, StringComparison.OrdinalIgnoreCase))
            {
                return args[i + 1];
            }
        }

        return null;
    }
}
