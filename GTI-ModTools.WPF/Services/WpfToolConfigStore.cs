using System.IO;
using System.Text.Json;
using GTI.ModTools.Images;

namespace GTI.ModTools.WPF.Services;

public sealed class WpfToolConfigStore
{
    private const string ConfigFileName = "GTI-ModTools.WPF.config.json";

    public WpfToolConfig Load(string workingDirectory)
    {
        var defaults = CreateDefault(workingDirectory);

        if (!File.Exists(ConfigPath))
        {
            return defaults;
        }

        try
        {
            var json = File.ReadAllText(ConfigPath);
            var loaded = JsonSerializer.Deserialize<WpfToolConfig>(json, JsonOptions);
            if (loaded is null)
            {
                return defaults;
            }

            var normalized = Normalize(loaded, defaults, workingDirectory);
            return normalized;
        }
        catch
        {
            return defaults;
        }
    }

    public void Save(WpfToolConfig config)
    {
        try
        {
            var normalized = Normalize(config, CreateDefault(Directory.GetCurrentDirectory()), Directory.GetCurrentDirectory());
            Directory.CreateDirectory(Path.GetDirectoryName(ConfigPath) ?? AppContext.BaseDirectory);
            File.WriteAllText(ConfigPath, JsonSerializer.Serialize(normalized, JsonOptions));
        }
        catch
        {
            // Keep UI functional even if config path is read-only.
        }
    }

    private static WpfToolConfig CreateDefault(string cwd)
    {
        return new WpfToolConfig
        {
            ArchiveInputPath = Path.Combine(cwd, "GameSource"),
            ArchiveOutputPath = Path.Combine(cwd, "ExportedFiles"),
            ArchiveRecursive = true,
            ImageInputPath = ImageConversionDefaults.GetDefaultInputPath(cwd, ConversionMode.Auto),
            ImageOutputPath = ImageConversionDefaults.GetDefaultOutputDirectory(cwd, ConversionMode.Auto),
            BasePath = ImageConversionDefaults.GetDefaultBaseDirectory(cwd)
        };
    }

    private static WpfToolConfig Normalize(WpfToolConfig config, WpfToolConfig defaults, string cwd)
    {
        var archiveOutput = NormalizePath(config.ArchiveOutputPath, defaults.ArchiveOutputPath);
        var legacyArchiveOutput = Path.GetFullPath(Path.Combine(cwd, "Farc_Out"));
        if (string.Equals(Path.GetFullPath(archiveOutput), legacyArchiveOutput, StringComparison.OrdinalIgnoreCase))
        {
            archiveOutput = defaults.ArchiveOutputPath;
        }

        return new WpfToolConfig
        {
            ArchiveInputPath = NormalizePath(config.ArchiveInputPath, defaults.ArchiveInputPath),
            ArchiveOutputPath = archiveOutput,
            ArchiveRecursive = config.ArchiveRecursive,
            ImageInputPath = NormalizePath(config.ImageInputPath, defaults.ImageInputPath),
            ImageOutputPath = NormalizePath(config.ImageOutputPath, defaults.ImageOutputPath),
            BasePath = NormalizePath(config.BasePath, defaults.BasePath)
        };
    }

    private static string NormalizePath(string? value, string fallback)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return fallback;
        }

        return Path.GetFullPath(value.Trim());
    }

    private static string ConfigPath => Path.Combine(AppContext.BaseDirectory, ConfigFileName);

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true
    };
}
