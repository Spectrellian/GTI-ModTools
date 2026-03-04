using System.Buffers.Binary;

namespace GTI.ModTools.Images.Tests;

public class BsjiTests
{
    [Fact]
    public void BsjiDocument_ParsesPointerBasedReferenceAndUpdatesSize()
    {
        var bytes = BuildMinimalBsjiWithPointerReference("icon", 64, 64);
        var document = BsjiDocument.Parse(bytes);

        var reference = Assert.Single(document.References, r => r.Name == "icon");
        Assert.Equal(0x28, reference.WidthOffset);
        Assert.Equal(0x2C, reference.HeightOffset);

        var changed = document.ApplyImageSize("icon", 64, 64, 128, 128);
        Assert.True(changed);

        var output = document.ToBytes();
        Assert.Equal((uint)128, BinaryPrimitives.ReadUInt32LittleEndian(output.AsSpan(0x28, 4)));
        Assert.Equal((uint)128, BinaryPrimitives.ReadUInt32LittleEndian(output.AsSpan(0x2C, 4)));
    }

    [Fact]
    public void BsjiDocument_UpdatesInlineReferenceUsingHeuristic()
    {
        var bytes = BuildMinimalBsjiWithInlineReference("icon_inline", 64, 64);
        var document = BsjiDocument.Parse(bytes);

        var reference = Assert.Single(document.References, r => r.Name == "icon_inline");
        Assert.Null(reference.WidthOffset);
        Assert.Null(reference.HeightOffset);

        var changed = document.ApplyImageSize("icon_inline", 64, 64, 96, 96);
        Assert.True(changed);

        var output = document.ToBytes();
        Assert.Equal((uint)96, BinaryPrimitives.ReadUInt32LittleEndian(output.AsSpan(0x80, 4)));
        Assert.Equal((uint)96, BinaryPrimitives.ReadUInt32LittleEndian(output.AsSpan(0x84, 4)));
    }

    [Fact]
    public void Converter_ToImg_UsesBaseImgFormatAndEmitsAdjustedBsji()
    {
        var root = Path.Combine(Path.GetTempPath(), "imgloader-bsji-tests-" + Guid.NewGuid());
        Directory.CreateDirectory(root);
        try
        {
            var baseDir = Path.Combine(root, "Base");
            var inputDir = Path.Combine(root, "Image_In");
            var outputDir = Path.Combine(root, "Image_Out");
            Directory.CreateDirectory(baseDir);
            Directory.CreateDirectory(inputDir);

            var baseImage = new DecodedImage
            {
                Width = 64,
                Height = 64,
                RgbaPixels = BuildSolidRgba(64, 64, 20, 30, 40, 255)
            };
            var baseImgBytes = ImgEncoder.Encode(baseImage, ImgPixelFormat.Rgb8, new DecodeOptions
            {
                FlipVertical = false,
                UseSwizzle = false,
                RgbOrder24 = ChannelOrder24.Rgb
            });
            File.WriteAllBytes(Path.Combine(baseDir, "icon.img"), baseImgBytes);
            File.WriteAllBytes(Path.Combine(baseDir, "icon_layout.bsji"), BuildMinimalBsjiWithPointerReference("icon", 64, 64));

            var newPngPixels = BuildSolidRgba(128, 128, 200, 120, 40, 255);
            PngWriter.WriteRgbaToFile(Path.Combine(inputDir, "icon.png"), 128, 128, newPngPixels);

            var report = ImgConverter.ConvertWithReport(new ImageConversionOptions
            {
                InputPath = inputDir,
                OutputDirectory = outputDir,
                BaseDirectory = baseDir,
                Mode = ConversionMode.ToImg,
                ImgOutputFormat = ImgPixelFormat.Rgba8888,
                InferImgFormatWhenMissingSuffix = true,
                FlipVertical = false,
                UseSwizzle = false,
                RgbOrder24 = ChannelOrder24.Rgb,
                RgbaOrder32 = ChannelOrder32.Abgr
            });

            Assert.Empty(report.Failed);

            var outputImg = Path.Combine(outputDir, "icon.img");
            Assert.True(File.Exists(outputImg));
            var header = ImgHeader.Parse(File.ReadAllBytes(outputImg));
            Assert.Equal(ImgPixelFormat.Rgb8, header.Format); // pulled from base IMG format

            var outputBsji = Path.Combine(outputDir, "icon_layout.bsji");
            Assert.True(File.Exists(outputBsji));
            var bsjiBytes = File.ReadAllBytes(outputBsji);
            Assert.Equal((uint)128, BinaryPrimitives.ReadUInt32LittleEndian(bsjiBytes.AsSpan(0x28, 4)));
            Assert.Equal((uint)128, BinaryPrimitives.ReadUInt32LittleEndian(bsjiBytes.AsSpan(0x2C, 4)));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void Converter_ToImg_ResolvesBaseFormatByRelativePathWhenNamesDuplicate()
    {
        var root = Path.Combine(Path.GetTempPath(), "imgloader-bsji-tests-" + Guid.NewGuid());
        Directory.CreateDirectory(root);
        try
        {
            var baseDir = Path.Combine(root, "Base");
            var inputDir = Path.Combine(root, "Image_In");
            var outputDir = Path.Combine(root, "Image_Out");
            Directory.CreateDirectory(Path.Combine(baseDir, "A"));
            Directory.CreateDirectory(Path.Combine(baseDir, "B"));
            Directory.CreateDirectory(Path.Combine(inputDir, "B"));

            var opaqueImage = new DecodedImage
            {
                Width = 64,
                Height = 64,
                RgbaPixels = BuildSolidRgba(64, 64, 10, 20, 30, 255)
            };
            File.WriteAllBytes(
                Path.Combine(baseDir, "A", "icon.img"),
                ImgEncoder.Encode(opaqueImage, ImgPixelFormat.Rgb8, new DecodeOptions
                {
                    FlipVertical = false,
                    UseSwizzle = false,
                    RgbOrder24 = ChannelOrder24.Rgb
                }));

            File.WriteAllBytes(
                Path.Combine(baseDir, "B", "icon.img"),
                ImgEncoder.Encode(opaqueImage, ImgPixelFormat.Rgba8888, new DecodeOptions
                {
                    FlipVertical = false,
                    UseSwizzle = false,
                    RgbaOrder32 = ChannelOrder32.Abgr
                }));

            PngWriter.WriteRgbaToFile(Path.Combine(inputDir, "B", "icon.png"), 64, 64, opaqueImage.RgbaPixels);

            var report = ImgConverter.ConvertWithReport(new ImageConversionOptions
            {
                InputPath = inputDir,
                OutputDirectory = outputDir,
                BaseDirectory = baseDir,
                Mode = ConversionMode.ToImg,
                InferImgFormatWhenMissingSuffix = true,
                ImgOutputFormat = ImgPixelFormat.Rgb8,
                FlipVertical = false,
                UseSwizzle = false,
                RgbOrder24 = ChannelOrder24.Rgb,
                RgbaOrder32 = ChannelOrder32.Abgr
            });

            Assert.DoesNotContain(report.Failed, failure => failure.Error.Contains("not supported", StringComparison.OrdinalIgnoreCase));
            var outputImg = Path.Combine(outputDir, "B", "icon.img");
            Assert.True(File.Exists(outputImg));
            var header = ImgHeader.Parse(File.ReadAllBytes(outputImg));
            Assert.Equal(ImgPixelFormat.Rgba8888, header.Format);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void Converter_ToImg_SkipsBsjiUpdateWhenBaseNameIsAmbiguous()
    {
        var root = Path.Combine(Path.GetTempPath(), "imgloader-bsji-tests-" + Guid.NewGuid());
        Directory.CreateDirectory(root);
        try
        {
            var baseDir = Path.Combine(root, "Base");
            var inputDir = Path.Combine(root, "Image_In");
            var outputDir = Path.Combine(root, "Image_Out");
            Directory.CreateDirectory(Path.Combine(baseDir, "A"));
            Directory.CreateDirectory(Path.Combine(baseDir, "B"));
            Directory.CreateDirectory(Path.Combine(inputDir, "B"));

            var opaqueImage = new DecodedImage
            {
                Width = 64,
                Height = 64,
                RgbaPixels = BuildSolidRgba(64, 64, 10, 20, 30, 255)
            };
            File.WriteAllBytes(
                Path.Combine(baseDir, "A", "icon.img"),
                ImgEncoder.Encode(opaqueImage, ImgPixelFormat.Rgb8, new DecodeOptions
                {
                    FlipVertical = false,
                    UseSwizzle = false,
                    RgbOrder24 = ChannelOrder24.Rgb
                }));

            File.WriteAllBytes(
                Path.Combine(baseDir, "B", "icon.img"),
                ImgEncoder.Encode(opaqueImage, ImgPixelFormat.Rgba8888, new DecodeOptions
                {
                    FlipVertical = false,
                    UseSwizzle = false,
                    RgbaOrder32 = ChannelOrder32.Abgr
                }));

            File.WriteAllBytes(
                Path.Combine(baseDir, "icon_layout.bsji"),
                BuildMinimalBsjiWithPointerReference("icon", 64, 64));

            var resizedPixels = BuildSolidRgba(128, 128, 200, 120, 40, 255);
            PngWriter.WriteRgbaToFile(Path.Combine(inputDir, "B", "icon.png"), 128, 128, resizedPixels);

            var report = ImgConverter.ConvertWithReport(new ImageConversionOptions
            {
                InputPath = inputDir,
                OutputDirectory = outputDir,
                BaseDirectory = baseDir,
                Mode = ConversionMode.ToImg,
                InferImgFormatWhenMissingSuffix = true,
                ImgOutputFormat = ImgPixelFormat.Rgb8,
                FlipVertical = false,
                UseSwizzle = false,
                RgbOrder24 = ChannelOrder24.Rgb,
                RgbaOrder32 = ChannelOrder32.Abgr
            });

            Assert.Contains(report.Failed, failure => failure.Error.Contains("ambiguous in Base", StringComparison.OrdinalIgnoreCase));
            Assert.True(File.Exists(Path.Combine(outputDir, "B", "icon.img")));
            Assert.False(File.Exists(Path.Combine(outputDir, "icon_layout.bsji")));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    private static byte[] BuildMinimalBsjiWithPointerReference(string imageName, int width, int height)
    {
        var bytes = new byte[0x50];
        bytes[0] = (byte)'S';
        bytes[1] = (byte)'I';
        bytes[2] = (byte)'R';
        bytes[3] = (byte)'0';
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(0x04, 4), 0x30);
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(0x08, 4), 0x40);

        WriteUtf16String(bytes, 0x10, imageName);
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(0x20, 4), 0x10);
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(0x28, 4), (uint)width);
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(0x2C, 4), (uint)height);

        // SIR0 pointer offsets: 0x04, 0x08, 0x20 (delta encoded with 7-bit groups, high-bit clear terminator)
        bytes[0x40] = 0x04;
        bytes[0x41] = 0x04;
        bytes[0x42] = 0x18;
        bytes[0x43] = 0x00;
        return bytes;
    }

    private static byte[] BuildMinimalBsjiWithInlineReference(string imageName, int width, int height)
    {
        var bytes = new byte[0xA0];
        bytes[0] = (byte)'S';
        bytes[1] = (byte)'I';
        bytes[2] = (byte)'R';
        bytes[3] = (byte)'0';
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(0x04, 4), 0x30);
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(0x08, 4), 0x90);

        WriteUtf16String(bytes, 0x30, imageName);
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(0x80, 4), (uint)width);
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(0x84, 4), (uint)height);

        // SIR0 pointer offsets: 0x04, 0x08 only
        bytes[0x90] = 0x04;
        bytes[0x91] = 0x04;
        bytes[0x92] = 0x00;
        return bytes;
    }

    private static void WriteUtf16String(byte[] destination, int offset, string value)
    {
        for (var i = 0; i < value.Length; i++)
        {
            BinaryPrimitives.WriteUInt16LittleEndian(destination.AsSpan(offset + i * 2, 2), value[i]);
        }

        BinaryPrimitives.WriteUInt16LittleEndian(destination.AsSpan(offset + value.Length * 2, 2), 0);
    }

    private static byte[] BuildSolidRgba(int width, int height, byte r, byte g, byte b, byte a)
    {
        var result = new byte[width * height * 4];
        for (var i = 0; i < result.Length; i += 4)
        {
            result[i] = r;
            result[i + 1] = g;
            result[i + 2] = b;
            result[i + 3] = a;
        }

        return result;
    }
}
