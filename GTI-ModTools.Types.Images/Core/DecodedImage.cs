namespace GTI.ModTools.Images;

public sealed class DecodedImage
{
    public required int Width { get; init; }
    public required int Height { get; init; }
    public required byte[] RgbaPixels { get; init; }
}
