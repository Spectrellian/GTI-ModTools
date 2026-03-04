using System.Collections.Concurrent;

namespace GTI.ModTools.Images;

public readonly record struct BaseOrganizeReport(
    IReadOnlyList<ConversionResult> Moved,
    IReadOnlyList<ConversionFailure> Failed,
    int TotalImagesScanned,
    int ReferencedImages,
    int UnmappedCandidates);

public static class BaseOrganizer
{
    public static BaseOrganizeReport MoveUnmappedImages(string baseDirectory)
    {
        var root = Path.GetFullPath(baseDirectory);
        Directory.CreateDirectory(root);

        var unmappedRoot = Path.Combine(root, "Unmapped");
        Directory.CreateDirectory(unmappedRoot);

        var failures = new ConcurrentBag<ConversionFailure>();
        var moved = new ConcurrentBag<ConversionResult>();
        var baseIndex = BaseAssetIndex.Build(root);

        var totalScanned = 0;
        var referenced = 0;
        var candidates = 0;

        var imagePaths = Directory.EnumerateFiles(root, "*.img", SearchOption.AllDirectories)
            .Select(Path.GetFullPath)
            .Where(path => !IsInsideDirectory(path, unmappedRoot))
            .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
            .ToList();

        foreach (var imagePath in imagePaths)
        {
            totalScanned++;
            var imageName = Path.GetFileNameWithoutExtension(imagePath);
            if (baseIndex.GetBsjiPathsForImage(imageName).Count > 0)
            {
                referenced++;
                continue;
            }

            candidates++;
            var relative = Path.GetRelativePath(root, imagePath);
            var outputPath = Path.Combine(unmappedRoot, relative);
            var outputDirectory = Path.GetDirectoryName(outputPath);
            if (!string.IsNullOrWhiteSpace(outputDirectory))
            {
                Directory.CreateDirectory(outputDirectory);
            }

            if (File.Exists(outputPath))
            {
                failures.Add(new ConversionFailure(imagePath, $"Unmapped target already exists: {outputPath}"));
                continue;
            }

            File.Move(imagePath, outputPath);
            moved.Add(new ConversionResult(imagePath, outputPath));
        }

        return new BaseOrganizeReport(
            Moved: moved.OrderBy(entry => entry.InputPath, StringComparer.OrdinalIgnoreCase).ToList(),
            Failed: failures.OrderBy(entry => entry.InputPath, StringComparer.OrdinalIgnoreCase).ToList(),
            TotalImagesScanned: totalScanned,
            ReferencedImages: referenced,
            UnmappedCandidates: candidates);
    }

    private static bool IsInsideDirectory(string fullPath, string directoryPath)
    {
        var path = Path.GetFullPath(fullPath)
            .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        var directory = Path.GetFullPath(directoryPath)
            .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);

        if (path.Equals(directory, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        var withSeparator = directory + Path.DirectorySeparatorChar;
        return path.StartsWith(withSeparator, StringComparison.OrdinalIgnoreCase);
    }
}
