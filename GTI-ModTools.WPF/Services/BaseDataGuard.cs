using System;
using System.IO;
using System.Linq;

namespace GTI.ModTools.WPF;

public static class BaseDataGuard
{
    public static bool AutoModeNeedsBaseData(string inputPath)
    {
        try
        {
            if (File.Exists(inputPath))
            {
                return string.Equals(Path.GetExtension(inputPath), ".png", StringComparison.OrdinalIgnoreCase);
            }

            if (!Directory.Exists(inputPath))
            {
                return false;
            }

            return Directory.EnumerateFiles(inputPath, "*.png", SearchOption.AllDirectories).Any();
        }
        catch
        {
            return false;
        }
    }

    public static bool HasValidBaseData(string basePath)
    {
        try
        {
            if (!Directory.Exists(basePath))
            {
                return false;
            }

            var hasImg = Directory.EnumerateFiles(basePath, "*.img", SearchOption.AllDirectories).Any();
            var hasBsji = Directory.EnumerateFiles(basePath, "*.bsji", SearchOption.AllDirectories).Any();
            return hasImg && hasBsji;
        }
        catch
        {
            return false;
        }
    }
}
