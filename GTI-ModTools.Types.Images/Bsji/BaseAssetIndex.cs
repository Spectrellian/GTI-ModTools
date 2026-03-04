namespace GTI.ModTools.Images;

internal readonly record struct BaseImageInfo(
    string Path,
    string RelativeStem,
    string Name,
    ImgPixelFormat Format,
    int Width,
    int Height);

internal sealed class BaseAssetIndex
{
    private readonly Dictionary<string, BaseImageInfo> _imagesByRelativeStem;
    private readonly Dictionary<string, BaseImageInfo> _imagesByName;
    private readonly HashSet<string> _ambiguousImageNames;
    private readonly Dictionary<string, HashSet<string>> _bsjiByImageName;

    private BaseAssetIndex(
        string baseDirectory,
        Dictionary<string, BaseImageInfo> imagesByRelativeStem,
        Dictionary<string, BaseImageInfo> imagesByName,
        HashSet<string> ambiguousImageNames,
        Dictionary<string, HashSet<string>> bsjiByImageName)
    {
        BaseDirectory = baseDirectory;
        _imagesByRelativeStem = imagesByRelativeStem;
        _imagesByName = imagesByName;
        _ambiguousImageNames = ambiguousImageNames;
        _bsjiByImageName = bsjiByImageName;
    }

    public string BaseDirectory { get; }

    public IEnumerable<string> BsjiPaths => _bsjiByImageName
        .SelectMany(pair => pair.Value)
        .Distinct(StringComparer.OrdinalIgnoreCase);

    public static BaseAssetIndex Build(string baseDirectory, System.Collections.Concurrent.ConcurrentBag<ConversionFailure>? failures = null)
    {
        var root = Path.GetFullPath(baseDirectory);
        if (!Directory.Exists(root))
        {
            return new BaseAssetIndex(
                root,
                new Dictionary<string, BaseImageInfo>(StringComparer.OrdinalIgnoreCase),
                new Dictionary<string, BaseImageInfo>(StringComparer.OrdinalIgnoreCase),
                new HashSet<string>(StringComparer.OrdinalIgnoreCase),
                new Dictionary<string, HashSet<string>>(StringComparer.OrdinalIgnoreCase));
        }

        var imagesByRelativeStem = new Dictionary<string, BaseImageInfo>(StringComparer.OrdinalIgnoreCase);
        var imagesByName = new Dictionary<string, BaseImageInfo>(StringComparer.OrdinalIgnoreCase);
        var ambiguousImageNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var imagePath in Directory.EnumerateFiles(root, "*.img", SearchOption.AllDirectories))
        {
            try
            {
                var bytes = File.ReadAllBytes(imagePath);
                var header = ImgHeader.Parse(bytes);
                var name = Path.GetFileNameWithoutExtension(imagePath);
                var relativeStem = NormalizeRelativeStem(root, imagePath);
                if (imagesByRelativeStem.ContainsKey(relativeStem))
                {
                    continue;
                }

                if (imagesByName.ContainsKey(name))
                {
                    ambiguousImageNames.Add(name);
                }

                var info = new BaseImageInfo(
                    Path: Path.GetFullPath(imagePath),
                    RelativeStem: relativeStem,
                    Name: name,
                    Format: header.Format,
                    Width: header.Width,
                    Height: header.Height);
                imagesByRelativeStem[relativeStem] = info;

                if (!imagesByName.ContainsKey(name))
                {
                    imagesByName[name] = info;
                }
            }
            catch (Exception ex)
            {
                failures?.Add(new ConversionFailure(imagePath, $"Failed to read base IMG: {ex.Message}"));
            }
        }

        var bsjiByImageName = new Dictionary<string, HashSet<string>>(StringComparer.OrdinalIgnoreCase);
        foreach (var bsjiPath in Directory.EnumerateFiles(root, "*.bsji", SearchOption.AllDirectories))
        {
            try
            {
                var document = BsjiDocument.Load(bsjiPath);
                var fullBsjiPath = Path.GetFullPath(bsjiPath);
                foreach (var imageName in document.ReferencedImageNames)
                {
                    if (!imagesByName.ContainsKey(imageName))
                    {
                        continue;
                    }

                    if (!bsjiByImageName.TryGetValue(imageName, out var set))
                    {
                        set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                        bsjiByImageName[imageName] = set;
                    }

                    set.Add(fullBsjiPath);
                }
            }
            catch (Exception ex)
            {
                failures?.Add(new ConversionFailure(bsjiPath, $"Failed to parse BSJI: {ex.Message}"));
            }
        }

        return new BaseAssetIndex(root, imagesByRelativeStem, imagesByName, ambiguousImageNames, bsjiByImageName);
    }

    public bool TryGetImageInfo(string imageName, out BaseImageInfo info)
    {
        return _imagesByName.TryGetValue(imageName, out info);
    }

    public bool TryResolveImageInfo(string imageName, string? relativeStem, out BaseImageInfo info)
    {
        if (!string.IsNullOrWhiteSpace(relativeStem))
        {
            var normalizedRelativeStem = NormalizeRelativeStem(relativeStem);
            if (_imagesByRelativeStem.TryGetValue(normalizedRelativeStem, out info))
            {
                return true;
            }
        }

        return _imagesByName.TryGetValue(imageName, out info);
    }

    public IReadOnlyCollection<string> GetBsjiPathsForImage(string imageName)
    {
        if (_bsjiByImageName.TryGetValue(imageName, out var set))
        {
            return set;
        }

        return Array.Empty<string>();
    }

    public bool IsAmbiguousImageName(string imageName)
    {
        return _ambiguousImageNames.Contains(imageName);
    }

    private static string NormalizeRelativeStem(string baseDirectory, string path)
    {
        var relative = Path.GetRelativePath(baseDirectory, Path.GetFullPath(path));
        return NormalizeRelativeStem(relative);
    }

    private static string NormalizeRelativeStem(string relativePath)
    {
        var withoutExtension = Path.ChangeExtension(relativePath, null) ?? relativePath;
        return withoutExtension.Replace('\\', '/').TrimStart('/');
    }
}
