namespace GTI.ModTools.Images;

public sealed class DecodeOptions
{
    public bool FlipVertical { get; init; } = true;
    public bool UseSwizzle { get; init; } = true;
    public ChannelOrder24 RgbOrder24 { get; init; } = ChannelOrder24.Rgb;
    public ChannelOrder32 RgbaOrder32 { get; init; } = ChannelOrder32.Abgr;
}
