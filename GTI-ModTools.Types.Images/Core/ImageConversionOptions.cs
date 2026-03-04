namespace GTI.ModTools.Images;

public sealed class ImageConversionOptions
{
    public required string InputPath { get; init; }
    public required string OutputDirectory { get; init; }
    public string BaseDirectory { get; init; } =
        ImageConversionDefaults.GetDefaultBaseDirectory(Directory.GetCurrentDirectory());

    public ConversionMode Mode { get; init; } = ConversionMode.Auto;
    public ImgPixelFormat ImgOutputFormat { get; init; } = ImgPixelFormat.Rgba8888;
    public bool InferImgFormatWhenMissingSuffix { get; init; } = false;
    public bool FlipVertical { get; init; } = true;
    public bool UseSwizzle { get; init; } = true;
    public ChannelOrder24 RgbOrder24 { get; init; } = ChannelOrder24.Rgb;
    public ChannelOrder32 RgbaOrder32 { get; init; } = ChannelOrder32.Abgr;
}
