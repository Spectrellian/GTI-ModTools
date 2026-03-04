namespace GTI.ModTools.Images;

public enum ImgPixelFormat : uint
{
    Unknown1 = 0x01,
    Rgb8 = 0x02,
    Rgba8888 = 0x03,
    Etc1 = 0x04,
    Unknown5 = 0x05,
    Xbgr1555 = 0x06,
    Unknown7 = 0x07,
    Unknown8 = 0x08
}

public enum ConversionMode
{
    Auto,
    ToPng,
    ToImg
}

public enum ChannelOrder24
{
    Rgb,
    Bgr
}

public enum ChannelOrder32
{
    Rgba,
    Argb,
    Abgr,
    Bgra
}
