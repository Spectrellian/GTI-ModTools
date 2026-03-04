namespace GTI.ModTools.Images;

public static class ImageConversionDefaults
{
    public const string BaseFolderName = "Base";
    public const string ImageInFolderName = "Image_In";
    public const string ImageOutFolderName = "Image_Out";
    public const string PngOutFolderName = "png_out";

    public static string GetDefaultBaseDirectory(string workingDirectory)
        => Path.Combine(workingDirectory, BaseFolderName);

    public static string GetDefaultInputPath(string workingDirectory, ConversionMode mode)
        => mode == ConversionMode.ToPng
            ? workingDirectory
            : Path.Combine(workingDirectory, ImageInFolderName);

    public static string GetDefaultOutputDirectory(string workingDirectory, ConversionMode mode)
        => mode == ConversionMode.ToPng
            ? Path.Combine(workingDirectory, PngOutFolderName)
            : Path.Combine(workingDirectory, ImageOutFolderName);

    public static void EnsureWorkspaceFolders(string workingDirectory)
    {
        Directory.CreateDirectory(Path.Combine(workingDirectory, BaseFolderName));
        Directory.CreateDirectory(Path.Combine(workingDirectory, ImageInFolderName));
        Directory.CreateDirectory(Path.Combine(workingDirectory, ImageOutFolderName));
    }
}
