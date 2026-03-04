using System.Collections.Concurrent;
using System.Globalization;

namespace GTI.ModTools.Images;

public readonly record struct ConversionResult(string InputPath, string OutputPath);
public readonly record struct ConversionFailure(string InputPath, string Error);
public readonly record struct ConversionReport(IReadOnlyList<ConversionResult> Converted, IReadOnlyList<ConversionFailure> Failed);
internal readonly record struct PngConversionContext(string ImageName, int Width, int Height, BaseImageInfo? BaseImage);
internal readonly record struct SizeOverride(int Width, int Height, BaseImageInfo? BaseImage);

public static class ImgConverter
{
    public static IReadOnlyList<ConversionResult> Convert(ImageConversionOptions options)
    {
        return ConvertWithReport(options).Converted;
    }

    public static ConversionReport ConvertWithReport(ImageConversionOptions options)
    {
        return options.Mode switch
        {
            ConversionMode.Auto => ConvertAuto(options),
            ConversionMode.ToPng => ConvertToPng(options),
            ConversionMode.ToImg => ConvertToImg(options),
            _ => throw new ArgumentOutOfRangeException(nameof(options.Mode), options.Mode, null)
        };
    }

    private static ConversionReport ConvertAuto(ImageConversionOptions options)
    {
        EnsureInputPathExists(options.InputPath);
        Directory.CreateDirectory(options.OutputDirectory);

        var inputRoot = GetInputRoot(options.InputPath);
        var inputs = ResolveAutoInputFiles(options.InputPath);
        EnsureInputsFound(inputs, $"No .img or .png files found in: {options.InputPath}");

        var codecOptions = BuildCodecOptions(options);
        var results = new ConcurrentBag<ConversionResult>();
        var failures = new ConcurrentBag<ConversionFailure>();
        var pngContexts = new ConcurrentBag<PngConversionContext>();
        var baseIndex = BaseAssetIndex.Build(options.BaseDirectory, failures);

        RunParallel(inputs, failures, input =>
            ConvertAutoInput(input, inputRoot, options.OutputDirectory, codecOptions, options, baseIndex, results, pngContexts));

        AppendAdjustedBsjiOutputs(options, baseIndex, pngContexts, failures, results);
        return BuildReport(results, failures);
    }

    private static ConversionReport ConvertToPng(ImageConversionOptions options)
    {
        EnsureInputPathExists(options.InputPath);
        Directory.CreateDirectory(options.OutputDirectory);

        var inputRoot = GetInputRoot(options.InputPath);
        var inputs = ResolveInputFiles(options.InputPath, ".img");
        EnsureInputsFound(inputs, $"No .img files found in: {options.InputPath}");

        var codecOptions = BuildCodecOptions(options);
        var results = new ConcurrentBag<ConversionResult>();
        var failures = new ConcurrentBag<ConversionFailure>();

        RunParallel(inputs, failures, input =>
            ConvertImgInputToPng(input, inputRoot, options.OutputDirectory, codecOptions, results));

        return BuildReport(results, failures);
    }

    private static ConversionReport ConvertToImg(ImageConversionOptions options)
    {
        EnsureInputPathExists(options.InputPath);
        Directory.CreateDirectory(options.OutputDirectory);

        var inputRoot = GetInputRoot(options.InputPath);
        var inputs = ResolveInputFiles(options.InputPath, ".png");
        EnsureInputsFound(inputs, $"No .png files found in: {options.InputPath}");

        var codecOptions = BuildCodecOptions(options);
        var results = new ConcurrentBag<ConversionResult>();
        var failures = new ConcurrentBag<ConversionFailure>();
        var pngContexts = new ConcurrentBag<PngConversionContext>();
        var baseIndex = BaseAssetIndex.Build(options.BaseDirectory, failures);

        RunParallel(inputs, failures, input =>
            ConvertPngInputToImg(input, inputRoot, options.OutputDirectory, codecOptions, options, baseIndex, results, pngContexts));

        AppendAdjustedBsjiOutputs(options, baseIndex, pngContexts, failures, results);
        return BuildReport(results, failures);
    }

    private static void ConvertAutoInput(
        string input,
        string inputRoot,
        string outputRoot,
        DecodeOptions codecOptions,
        ImageConversionOptions options,
        BaseAssetIndex baseIndex,
        ConcurrentBag<ConversionResult> results,
        ConcurrentBag<PngConversionContext> pngContexts)
    {
        var extension = Path.GetExtension(input);
        if (extension.Equals(".img", StringComparison.OrdinalIgnoreCase))
        {
            ConvertImgInputToPng(input, inputRoot, outputRoot, codecOptions, results);
            return;
        }

        if (extension.Equals(".png", StringComparison.OrdinalIgnoreCase))
        {
            ConvertPngInputToImg(input, inputRoot, outputRoot, codecOptions, options, baseIndex, results, pngContexts);
            return;
        }

        throw new InvalidOperationException($"Unsupported input type in auto mode: {input}");
    }

    private static void ConvertImgInputToPng(
        string input,
        string inputRoot,
        string outputRoot,
        DecodeOptions codecOptions,
        ConcurrentBag<ConversionResult> results)
    {
        var (image, sourceFormat) = DecodeImgWithFormat(input, codecOptions);
        var outputPath = GetOutputPngPathWithFormatSuffix(input, inputRoot, outputRoot, sourceFormat);
        PngWriter.WriteRgbaToFile(outputPath, image.Width, image.Height, image.RgbaPixels);
        results.Add(new ConversionResult(input, outputPath));
    }

    private static void ConvertPngInputToImg(
        string input,
        string inputRoot,
        string outputRoot,
        DecodeOptions codecOptions,
        ImageConversionOptions options,
        BaseAssetIndex baseIndex,
        ConcurrentBag<ConversionResult> results,
        ConcurrentBag<PngConversionContext> pngContexts)
    {
        var (namePath, suffixFormat) = RemoveFormatSuffixFromPngPath(input);
        var image = PngReader.ReadFromFile(input);
        var imageName = Path.GetFileNameWithoutExtension(namePath);
        var baseImage = ResolveBaseImageInfo(baseIndex, inputRoot, namePath, imageName);
        var outputFormat = DetermineOutputFormat(image, suffixFormat, baseImage, options);
        var imgBytes = ImgEncoder.Encode(image, outputFormat, codecOptions);

        var outputPath = GetOutputPath(namePath, inputRoot, outputRoot, ".img");
        EnsureOutputDirectoryExists(outputPath);
        File.WriteAllBytes(outputPath, imgBytes);

        results.Add(new ConversionResult(input, outputPath));
        pngContexts.Add(new PngConversionContext(imageName, image.Width, image.Height, baseImage));
    }

    private static BaseImageInfo? ResolveBaseImageInfo(BaseAssetIndex baseIndex, string inputRoot, string namePath, string imageName)
    {
        var relativeStem = GetRelativeStem(inputRoot, namePath);
        return baseIndex.TryResolveImageInfo(imageName, relativeStem, out var baseInfo)
            ? baseInfo
            : (BaseImageInfo?)null;
    }

    private static void AppendAdjustedBsjiOutputs(
        ImageConversionOptions options,
        BaseAssetIndex baseIndex,
        IEnumerable<PngConversionContext> pngContexts,
        ConcurrentBag<ConversionFailure> failures,
        ConcurrentBag<ConversionResult> results)
    {
        foreach (var bsjiResult in EmitAdjustedBsjiFiles(options, baseIndex, pngContexts, failures))
        {
            results.Add(bsjiResult);
        }
    }

    private static ConversionReport BuildReport(
        IEnumerable<ConversionResult> results,
        IEnumerable<ConversionFailure> failures)
    {
        return new ConversionReport(OrderResults(results), OrderFailures(failures));
    }

    private static void RunParallel(
        IEnumerable<string> inputs,
        ConcurrentBag<ConversionFailure> failures,
        Action<string> action)
    {
        Parallel.ForEach(inputs, input =>
        {
            try
            {
                action(input);
            }
            catch (Exception ex)
            {
                failures.Add(new ConversionFailure(input, ex.Message));
            }
        });
    }

    private static void EnsureInputsFound(IReadOnlyCollection<string> inputs, string errorMessage)
    {
        if (inputs.Count == 0)
        {
            throw new InvalidOperationException(errorMessage);
        }
    }

    private static void EnsureInputPathExists(string inputPath)
    {
        if (!File.Exists(inputPath) && !Directory.Exists(inputPath))
        {
            throw new FileNotFoundException($"Input path not found: {inputPath}");
        }
    }

    private static List<string> ResolveInputFiles(string inputPath, string extension)
    {
        if (File.Exists(inputPath))
        {
            if (!inputPath.EndsWith(extension, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException($"Input file is not a {extension} file: {inputPath}");
            }

            return [Path.GetFullPath(inputPath)];
        }

        return
        [
            .. Directory.EnumerateFiles(inputPath, "*", SearchOption.AllDirectories)
                .Where(path => path.EndsWith(extension, StringComparison.OrdinalIgnoreCase))
                .Select(Path.GetFullPath)
                .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
        ];
    }

    private static List<string> ResolveAutoInputFiles(string inputPath)
    {
        if (File.Exists(inputPath))
        {
            var ext = Path.GetExtension(inputPath);
            if (!ext.Equals(".img", StringComparison.OrdinalIgnoreCase) &&
                !ext.Equals(".png", StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException($"Input file must be .img or .png in auto mode: {inputPath}");
            }

            return [Path.GetFullPath(inputPath)];
        }

        return
        [
            .. Directory.EnumerateFiles(inputPath, "*", SearchOption.AllDirectories)
                .Where(path =>
                    path.EndsWith(".img", StringComparison.OrdinalIgnoreCase) ||
                    path.EndsWith(".png", StringComparison.OrdinalIgnoreCase))
                .Select(Path.GetFullPath)
                .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
        ];
    }

    private static string GetInputRoot(string inputPath)
    {
        if (Directory.Exists(inputPath))
        {
            return Path.GetFullPath(inputPath);
        }

        var fullFilePath = Path.GetFullPath(inputPath);
        return Path.GetDirectoryName(fullFilePath) ?? Directory.GetCurrentDirectory();
    }

    private static string GetOutputPath(string inputPath, string inputRoot, string outputRoot, string outputExtension)
    {
        var relative = Path.GetRelativePath(inputRoot, Path.GetFullPath(inputPath));
        var outputRelative = Path.ChangeExtension(relative, outputExtension);
        return Path.Combine(outputRoot, outputRelative);
    }

    private static string GetOutputPngPathWithFormatSuffix(string inputPath, string inputRoot, string outputRoot, ImgPixelFormat format)
    {
        var outputPath = GetOutputPath(inputPath, inputRoot, outputRoot, ".png");
        var outputDirectory = Path.GetDirectoryName(outputPath);
        var outputName = Path.GetFileNameWithoutExtension(outputPath);
        var formatToken = $"0x{(uint)format:X2}";
        var suffixedFileName = $"{outputName}_{formatToken}.png";
        return string.IsNullOrWhiteSpace(outputDirectory)
            ? suffixedFileName
            : Path.Combine(outputDirectory, suffixedFileName);
    }

    private static string GetRelativeStem(string inputRoot, string path)
    {
        var relative = Path.GetRelativePath(inputRoot, Path.GetFullPath(path));
        var withoutExtension = Path.ChangeExtension(relative, null) ?? relative;
        return withoutExtension.Replace('\\', '/');
    }

    private static (string NamePath, ImgPixelFormat? SuffixFormat) RemoveFormatSuffixFromPngPath(string path)
    {
        var extension = Path.GetExtension(path);
        var fileName = Path.GetFileNameWithoutExtension(path);
        var split = fileName.LastIndexOf('_');
        if (split <= 0 || split >= fileName.Length - 1)
        {
            return (path, null);
        }

        var suffix = fileName[(split + 1)..];
        if (!TryParseFormatSuffix(suffix, out var parsedFormat))
        {
            return (path, null);
        }

        var strippedName = fileName[..split] + extension;
        var directory = Path.GetDirectoryName(path);
        var strippedPath = string.IsNullOrWhiteSpace(directory)
            ? strippedName
            : Path.Combine(directory, strippedName);

        return (strippedPath, parsedFormat);
    }

    private static bool TryParseFormatSuffix(string value, out ImgPixelFormat format)
    {
        var normalized = value.Trim().ToLowerInvariant();
        switch (normalized)
        {
            case "rgb8":
                format = ImgPixelFormat.Rgb8;
                return true;
            case "rgba8888":
                format = ImgPixelFormat.Rgba8888;
                return true;
            case "etc1":
                format = ImgPixelFormat.Etc1;
                return true;
            case "unknown5":
            case "etc1a4":
                format = ImgPixelFormat.Unknown5;
                return true;
            case "unknown1":
                format = ImgPixelFormat.Unknown1;
                return true;
            case "xbgr1555":
                format = ImgPixelFormat.Xbgr1555;
                return true;
            case "unknown7":
                format = ImgPixelFormat.Unknown7;
                return true;
            case "unknown8":
                format = ImgPixelFormat.Unknown8;
                return true;
        }

        uint rawValue;
        if (normalized.StartsWith("0x", StringComparison.Ordinal))
        {
            if (!uint.TryParse(normalized.AsSpan(2), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out rawValue))
            {
                format = default;
                return false;
            }
        }
        else if (!uint.TryParse(normalized, NumberStyles.Integer, CultureInfo.InvariantCulture, out rawValue))
        {
            format = default;
            return false;
        }

        format = (ImgPixelFormat)rawValue;
        return Enum.IsDefined(format);
    }

    private static (DecodedImage Image, ImgPixelFormat Format) DecodeImgWithFormat(string path, DecodeOptions options)
    {
        var bytes = File.ReadAllBytes(path);
        var header = ImgHeader.Parse(bytes);
        var image = ImgDecoder.DecodeBytes(bytes, options);
        return (image, header.Format);
    }

    private static ImgPixelFormat DetermineOutputFormat(
        DecodedImage image,
        ImgPixelFormat? suffixFormat,
        BaseImageInfo? baseImage,
        ImageConversionOptions options)
    {
        if (suffixFormat.HasValue)
        {
            return suffixFormat.Value;
        }

        if (baseImage.HasValue)
        {
            return baseImage.Value.Format;
        }

        if (!options.InferImgFormatWhenMissingSuffix)
        {
            return options.ImgOutputFormat;
        }

        for (var i = 3; i < image.RgbaPixels.Length; i += 4)
        {
            if (image.RgbaPixels[i] != 255)
            {
                return ImgPixelFormat.Rgba8888;
            }
        }

        return ImgPixelFormat.Rgb8;
    }

    private static IReadOnlyList<ConversionResult> EmitAdjustedBsjiFiles(
        ImageConversionOptions options,
        BaseAssetIndex baseIndex,
        IEnumerable<PngConversionContext> pngContexts,
        ConcurrentBag<ConversionFailure> failures)
    {
        var (sizeOverrides, ambiguousNames) = BuildSizeOverrides(pngContexts, failures);
        if (sizeOverrides.Count == 0)
        {
            return [];
        }

        var bsjiToImageNames = BuildBsjiTargetMap(baseIndex, sizeOverrides, ambiguousNames, failures);
        if (bsjiToImageNames.Count == 0)
        {
            return [];
        }

        var results = new List<ConversionResult>();
        foreach (var (bsjiPath, imageNames) in bsjiToImageNames)
        {
            try
            {
                if (!TryApplyOverridesAndWriteBsji(options, bsjiPath, imageNames, sizeOverrides, out var result))
                {
                    continue;
                }

                results.Add(result);
            }
            catch (Exception ex)
            {
                failures.Add(new ConversionFailure(bsjiPath, $"Failed to emit adjusted BSJI: {ex.Message}"));
            }
        }

        return results;
    }

    private static (
        Dictionary<string, SizeOverride> SizeOverrides,
        HashSet<string> AmbiguousNames) BuildSizeOverrides(
            IEnumerable<PngConversionContext> pngContexts,
            ConcurrentBag<ConversionFailure> failures)
    {
        var sizeOverrides = new Dictionary<string, SizeOverride>(StringComparer.OrdinalIgnoreCase);
        var ambiguousNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var context in pngContexts)
        {
            if (sizeOverrides.TryGetValue(context.ImageName, out var existing) &&
                (existing.Width != context.Width || existing.Height != context.Height))
            {
                failures.Add(new ConversionFailure(
                    context.ImageName,
                    "Conflicting PNG dimensions for the same image name in one run."));
                ambiguousNames.Add(context.ImageName);
                continue;
            }

            if (!sizeOverrides.TryGetValue(context.ImageName, out existing))
            {
                sizeOverrides[context.ImageName] = new SizeOverride(context.Width, context.Height, context.BaseImage);
                continue;
            }

            if (existing.BaseImage.HasValue &&
                context.BaseImage.HasValue &&
                !string.Equals(existing.BaseImage.Value.Path, context.BaseImage.Value.Path, StringComparison.OrdinalIgnoreCase))
            {
                failures.Add(new ConversionFailure(
                    context.ImageName,
                    "BSJI update skipped because multiple base IMG files matched this image name."));
                ambiguousNames.Add(context.ImageName);
                continue;
            }

            var selectedBase = existing.BaseImage ?? context.BaseImage;
            sizeOverrides[context.ImageName] = new SizeOverride(context.Width, context.Height, selectedBase);
        }

        return (sizeOverrides, ambiguousNames);
    }

    private static Dictionary<string, HashSet<string>> BuildBsjiTargetMap(
        BaseAssetIndex baseIndex,
        IReadOnlyDictionary<string, SizeOverride> sizeOverrides,
        IReadOnlySet<string> ambiguousNames,
        ConcurrentBag<ConversionFailure> failures)
    {
        var bsjiToImageNames = new Dictionary<string, HashSet<string>>(StringComparer.OrdinalIgnoreCase);
        foreach (var imageName in sizeOverrides.Keys)
        {
            if (ambiguousNames.Contains(imageName) || baseIndex.IsAmbiguousImageName(imageName))
            {
                if (baseIndex.IsAmbiguousImageName(imageName))
                {
                    failures.Add(new ConversionFailure(
                        imageName,
                        "BSJI update skipped because this image name is ambiguous in Base (multiple IMG files share the same name)."));
                }

                continue;
            }

            var overrideEntry = sizeOverrides[imageName];
            if (!overrideEntry.BaseImage.HasValue)
            {
                continue;
            }

            foreach (var bsjiPath in baseIndex.GetBsjiPathsForImage(imageName))
            {
                if (!bsjiToImageNames.TryGetValue(bsjiPath, out var imageNames))
                {
                    imageNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                    bsjiToImageNames[bsjiPath] = imageNames;
                }

                imageNames.Add(imageName);
            }
        }

        return bsjiToImageNames;
    }

    private static bool TryApplyOverridesAndWriteBsji(
        ImageConversionOptions options,
        string bsjiPath,
        IReadOnlyCollection<string> imageNames,
        IReadOnlyDictionary<string, SizeOverride> sizeOverrides,
        out ConversionResult result)
    {
        result = default;
        var document = BsjiDocument.Load(bsjiPath);
        var changed = false;
        foreach (var imageName in imageNames)
        {
            if (!sizeOverrides.TryGetValue(imageName, out var sizeOverride) ||
                !sizeOverride.BaseImage.HasValue)
            {
                continue;
            }

            var baseInfo = sizeOverride.BaseImage.Value;
            changed |= document.ApplyImageSize(imageName, baseInfo.Width, baseInfo.Height, sizeOverride.Width, sizeOverride.Height);
        }

        if (!changed)
        {
            return false;
        }

        var relative = Path.GetRelativePath(options.BaseDirectory, bsjiPath);
        var outputPath = Path.Combine(options.OutputDirectory, relative);
        EnsureOutputDirectoryExists(outputPath);
        File.WriteAllBytes(outputPath, document.ToBytes());
        result = new ConversionResult(bsjiPath, outputPath);
        return true;
    }

    private static void EnsureOutputDirectoryExists(string outputPath)
    {
        var directory = Path.GetDirectoryName(outputPath);
        if (!string.IsNullOrWhiteSpace(directory))
        {
            Directory.CreateDirectory(directory);
        }
    }

    private static DecodeOptions BuildCodecOptions(ImageConversionOptions options)
    {
        return new DecodeOptions
        {
            FlipVertical = options.FlipVertical,
            UseSwizzle = options.UseSwizzle,
            RgbOrder24 = options.RgbOrder24,
            RgbaOrder32 = options.RgbaOrder32
        };
    }

    private static IReadOnlyList<ConversionResult> OrderResults(IEnumerable<ConversionResult> results)
    {
        return results
            .OrderBy(result => result.InputPath, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static IReadOnlyList<ConversionFailure> OrderFailures(IEnumerable<ConversionFailure> failures)
    {
        return failures
            .OrderBy(failure => failure.InputPath, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }
}
