using GTI.ModTools.Images;

namespace GTI.ModTools.Images.CLI;

public static class CliOptions
{
    public static string HelpText =>
        """
        Usage:
          GTI-ModTools.Images.CLI [inputPath] [outputDirectory] [options]

        Defaults:
          auto mode: inputPath=./Image_In, outputDirectory=./Image_Out
          base folder: ./Base (original .img/.bsji metadata set)
          auto mode converts .img -> .png and .png -> .img
          in one run
          to-png mode: inputPath=current directory, outputDirectory=./png_out
          to-img mode: inputPath=./Image_In, outputDirectory=./Image_Out
          channel order for format 0x03: ABGR

        Options:
          --auto                    Convert based on extension (.img -> .png, .png -> .img)
          --to-png                  Convert .img files to .png files
          --to-img                  Convert .png files to .img files
          --base path               Base folder for original .img/.bsji files
                                    Used to preserve source IMG format and to emit corrected BSJI
                                    when converting .png -> .img.
          --organize-base-unmapped  Maintenance mode:
                                    move IMG files that have no BSJI reference into
                                    Base/Unmapped while preserving relative folders.
          --format png|rgb8|rgba8888
                                    Optional output format parameter.
                                    Use rgb8/rgba8888 with --to-img (single file or folders).
                                    In --auto, it applies to .png -> .img outputs.
                                    Use png with --to-png.
          note on encoder support   .png -> .img currently supports encoding formats
                                    0x01/0x02/0x03/0x07/0x08.
                                    0x04/0x05/0x06 are decode-only for .img -> .png right now.
          file naming for format    .img -> .png appends format suffix (example: icon_0x02.png).
                                    .png -> .img uses that suffix when present and strips it from output name.
                                    If suffix is missing, format is inferred from alpha (opaque=rgb8, else rgba8888),
                                    unless --format is provided.
          --img-format rgb8|rgba8888
                                    Legacy alias for --format when using --to-img
          --no-flip                 Disable vertical flip
          --no-swizzle              Treat uncompressed pixels as linear row-major
          --rgb-order rgb|bgr       Byte order for format 0x02 (default: rgb)
          --rgba-order rgba|argb|abgr|bgra
                                    Byte order for format 0x03 (default: abgr)
        """;

    public static ImageConversionOptions Parse(string[] args)
    {
        var positional = new List<string>();
        var mode = ConversionMode.Auto;
        var imgOutputFormat = ImgPixelFormat.Rgba8888;
        var inferImgFormatWhenMissingSuffix = true;
        string? explicitFormat = null;
        string? explicitBaseDirectory = null;
        var flip = true;
        var swizzle = true;
        var rgbOrder = ChannelOrder24.Rgb;
        var rgbaOrder = ChannelOrder32.Abgr;

        for (var i = 0; i < args.Length; i++)
        {
            var arg = args[i];
            if (!arg.StartsWith("--", StringComparison.Ordinal))
            {
                positional.Add(arg);
                continue;
            }

            switch (arg)
            {
                case "--auto":
                    mode = ConversionMode.Auto;
                    break;
                case "--to-png":
                    mode = ConversionMode.ToPng;
                    break;
                case "--to-img":
                    mode = ConversionMode.ToImg;
                    break;
                case "--format":
                case "--img-format":
                    i++;
                    explicitFormat = GetOptionValue(args, i, arg);
                    break;
                case "--base":
                    i++;
                    explicitBaseDirectory = GetOptionValue(args, i, "--base");
                    break;
                case "--no-flip":
                    flip = false;
                    break;
                case "--no-swizzle":
                    swizzle = false;
                    break;
                case "--rgb-order":
                    i++;
                    rgbOrder = ParseRgb24(GetOptionValue(args, i, "--rgb-order"));
                    break;
                case "--rgba-order":
                    i++;
                    rgbaOrder = ParseRgba32(GetOptionValue(args, i, "--rgba-order"));
                    break;
                default:
                    throw new ArgumentException($"Unknown option: {arg}");
            }
        }

        if (!string.IsNullOrWhiteSpace(explicitFormat))
        {
            if (mode is ConversionMode.ToImg or ConversionMode.Auto)
            {
                imgOutputFormat = ParseImgOutputFormat(explicitFormat);
                inferImgFormatWhenMissingSuffix = false;
            }
            else if (!string.Equals(explicitFormat, "png", StringComparison.OrdinalIgnoreCase))
            {
                throw new ArgumentException(
                    $"Format '{explicitFormat}' is invalid for --to-png. Use --format png, or switch to --to-img/--auto.");
            }
        }

        var cwd = Directory.GetCurrentDirectory();
        var input = positional.Count > 0 ? positional[0] : ImageConversionDefaults.GetDefaultInputPath(cwd, mode);
        var output = positional.Count > 1 ? positional[1] : ImageConversionDefaults.GetDefaultOutputDirectory(cwd, mode);
        var baseDirectory = explicitBaseDirectory ?? ImageConversionDefaults.GetDefaultBaseDirectory(cwd);

        return new ImageConversionOptions
        {
            InputPath = Path.GetFullPath(input),
            OutputDirectory = Path.GetFullPath(output),
            BaseDirectory = Path.GetFullPath(baseDirectory),
            Mode = mode,
            ImgOutputFormat = imgOutputFormat,
            InferImgFormatWhenMissingSuffix = inferImgFormatWhenMissingSuffix,
            FlipVertical = flip,
            UseSwizzle = swizzle,
            RgbOrder24 = rgbOrder,
            RgbaOrder32 = rgbaOrder
        };
    }

    private static string GetOptionValue(string[] args, int index, string optionName)
    {
        if (index >= args.Length)
        {
            throw new ArgumentException($"Missing value for {optionName}");
        }

        return args[index];
    }

    private static ChannelOrder24 ParseRgb24(string value)
    {
        return value.ToLowerInvariant() switch
        {
            "rgb" => ChannelOrder24.Rgb,
            "bgr" => ChannelOrder24.Bgr,
            _ => throw new ArgumentException($"Invalid value for --rgb-order: {value}")
        };
    }

    private static ChannelOrder32 ParseRgba32(string value)
    {
        return value.ToLowerInvariant() switch
        {
            "rgba" => ChannelOrder32.Rgba,
            "argb" => ChannelOrder32.Argb,
            "abgr" => ChannelOrder32.Abgr,
            "bgra" => ChannelOrder32.Bgra,
            _ => throw new ArgumentException($"Invalid value for --rgba-order: {value}")
        };
    }

    private static ImgPixelFormat ParseImgOutputFormat(string value)
    {
        return value.ToLowerInvariant() switch
        {
            "rgb8" => ImgPixelFormat.Rgb8,
            "rgba8888" => ImgPixelFormat.Rgba8888,
            _ => throw new ArgumentException($"Invalid value for --img-format: {value}")
        };
    }
}
