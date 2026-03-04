using System.Buffers.Binary;
using System.IO.Compression;
using System.Text;

namespace GTI.ModTools.Images.Tests;

public class UnitTests
{
    private static readonly (int XStart, int YStart, int XStep, int YStep)[] Adam7Passes =
    [
        (0, 0, 8, 8),
        (4, 0, 8, 8),
        (0, 4, 4, 8),
        (2, 0, 4, 4),
        (0, 2, 2, 4),
        (1, 0, 2, 2),
        (0, 1, 1, 2)
    ];

    [Fact]
    public void Header_Parse_ReadsExpectedFields()
    {
        var payload = new byte[0x80];
        WriteHeader(payload, ImgPixelFormat.Rgb8, 16, 8, 0x18, 0x80);

        var header = ImgHeader.Parse(payload);

        Assert.Equal(ImgPixelFormat.Rgb8, header.Format);
        Assert.Equal(16, header.Width);
        Assert.Equal(8, header.Height);
        Assert.Equal(0x18, header.PixelLengthBits);
        Assert.Equal(0x80, header.DataOffset);
    }

    [Fact]
    public void Decode_Rgb8_Swizzled_RestoresOriginalPixels()
    {
        const int width = 8;
        const int height = 8;
        var expected = BuildExpectedRgba(width, height, (x, y) => ((byte)x, (byte)y, (byte)(x + y), (byte)255));
        var raw = EncodeRgb24(width, height, expected, ChannelOrder24.Rgb, swizzled: true);
        var img = BuildImgFile(ImgPixelFormat.Rgb8, width, height, 0x18, raw);

        var decoded = ImgDecoder.DecodeBytes(img, new DecodeOptions
        {
            FlipVertical = false,
            UseSwizzle = true,
            RgbOrder24 = ChannelOrder24.Rgb
        });

        Assert.Equal(expected, decoded.RgbaPixels);
    }

    [Fact]
    public void Swizzle_UsesCtr8x8Lookup()
    {
        var expectedFirst16 = new (int x, int y)[]
        {
            (0, 0), (1, 0), (0, 1), (1, 1),
            (2, 0), (3, 0), (2, 1), (3, 1),
            (0, 2), (1, 2), (0, 3), (1, 3),
            (2, 2), (3, 2), (2, 3), (3, 3)
        };

        for (var i = 0; i < expectedFirst16.Length; i++)
        {
            var actual = Swizzle.GetPixelCoordinates(i, 8, 8);
            Assert.Equal(expectedFirst16[i], actual);
        }
    }

    [Fact]
    public void Decode_Rgba8888_AbgrOrder_RestoresOriginalPixels()
    {
        const int width = 8;
        const int height = 8;
        var expected = BuildExpectedRgba(width, height, (x, y) => ((byte)(x * 4), (byte)(y * 7), (byte)(200 - x), (byte)(x + y)));
        var raw = EncodeRgba32(width, height, expected, ChannelOrder32.Abgr, swizzled: true);
        var img = BuildImgFile(ImgPixelFormat.Rgba8888, width, height, 0x20, raw);

        var decoded = ImgDecoder.DecodeBytes(img, new DecodeOptions
        {
            FlipVertical = false,
            UseSwizzle = true,
            RgbaOrder32 = ChannelOrder32.Abgr
        });

        Assert.Equal(expected, decoded.RgbaPixels);
    }

    [Fact]
    public void Decode_Unknown1_BgraOrder_RestoresOriginalPixels()
    {
        const int width = 8;
        const int height = 8;
        var expected = BuildExpectedRgba(width, height, (x, y) => ((byte)(x * 6), (byte)(y * 5), (byte)(180 - x), (byte)(x + y + 20)));
        var raw = EncodeRgba32(width, height, expected, ChannelOrder32.Bgra, swizzled: true);
        var img = BuildImgFile(ImgPixelFormat.Unknown1, width, height, 0x20, raw);

        var decoded = ImgDecoder.DecodeBytes(img, new DecodeOptions
        {
            FlipVertical = false,
            UseSwizzle = true
        });

        Assert.Equal(expected, decoded.RgbaPixels);
    }

    [Fact]
    public void Decode_Unknown8_La4_DecodesLuminanceAndAlpha()
    {
        const int width = 8;
        const int height = 8;
        var packed = new byte[width * height];
        var expected = new byte[width * height * 4];

        for (var i = 0; i < packed.Length; i++)
        {
            var luminance4 = i % 16;
            var alpha4 = (15 - i) & 0x0F;
            packed[i] = (byte)((luminance4 << 4) | alpha4);

            var lum = (byte)((luminance4 << 4) | luminance4);
            var alpha = (byte)((alpha4 << 4) | alpha4);
            var dst = i * 4;
            expected[dst] = lum;
            expected[dst + 1] = lum;
            expected[dst + 2] = lum;
            expected[dst + 3] = alpha;
        }

        var img8 = BuildImgFile(ImgPixelFormat.Unknown8, width, height, 0x08, packed);
        var decoded8 = ImgDecoder.DecodeBytes(img8, new DecodeOptions
        {
            FlipVertical = false,
            UseSwizzle = false
        });
        Assert.Equal(expected, decoded8.RgbaPixels);

        var img7 = BuildImgFile(ImgPixelFormat.Unknown7, width, height, 0x08, packed);
        var decoded7 = ImgDecoder.DecodeBytes(img7, new DecodeOptions
        {
            FlipVertical = false,
            UseSwizzle = false
        });
        Assert.Equal(expected, decoded7.RgbaPixels);
    }

    [Fact]
    public void Decode_Unknown7_UsesLinearLayoutEvenWhenSwizzleEnabled()
    {
        const int width = 8;
        const int height = 8;
        var packed = new byte[width * height];
        var expectedLinear = new byte[width * height * 4];

        for (var i = 0; i < packed.Length; i++)
        {
            packed[i] = (byte)(0xF0 | (i & 0x0F)); // luminance=15, alpha varies by linear pixel index
            var alpha = (byte)(((i & 0x0F) << 4) | (i & 0x0F));
            var dst = i * 4;
            expectedLinear[dst] = 255;
            expectedLinear[dst + 1] = 255;
            expectedLinear[dst + 2] = 255;
            expectedLinear[dst + 3] = alpha;
        }

        var img7 = BuildImgFile(ImgPixelFormat.Unknown7, width, height, 0x08, packed);
        var decoded7 = ImgDecoder.DecodeBytes(img7, new DecodeOptions
        {
            FlipVertical = false,
            UseSwizzle = true
        });
        Assert.Equal(expectedLinear, decoded7.RgbaPixels);
    }

    [Fact]
    public void Decode_Xbgr1555_BigEndian_RestoresOriginalPixels()
    {
        const int width = 8;
        const int height = 8;
        var source = BuildExpectedRgba(width, height, (x, y) => ((byte)(x * 12), (byte)(y * 10), (byte)(200 - x * 8), (byte)255));
        var expected = QuantizeTo5BitChannels(source);
        var raw = EncodeXbgr1555BigEndian(width, height, source, swizzled: true);
        var img = BuildImgFile(ImgPixelFormat.Xbgr1555, width, height, 0x10, raw);

        var decoded = ImgDecoder.DecodeBytes(img, new DecodeOptions
        {
            FlipVertical = false,
            UseSwizzle = true
        });

        Assert.Equal(expected, decoded.RgbaPixels);
    }

    [Fact]
    public void Decode_FlipVertical_FlipsRows()
    {
        const int width = 4;
        const int height = 4;
        var expectedNoFlip = BuildExpectedRgba(width, height, (_, y) => y < 2 ? ((byte)255, (byte)0, (byte)0, (byte)255) : ((byte)0, (byte)0, (byte)255, (byte)255));
        var raw = EncodeRgb24(width, height, expectedNoFlip, ChannelOrder24.Rgb, swizzled: false);
        var img = BuildImgFile(ImgPixelFormat.Rgb8, width, height, 0x18, raw);

        var decoded = ImgDecoder.DecodeBytes(img, new DecodeOptions
        {
            FlipVertical = true,
            UseSwizzle = false,
            RgbOrder24 = ChannelOrder24.Rgb
        });

        for (var y = 0; y < height; y++)
        {
            for (var x = 0; x < width; x++)
            {
                var index = (y * width + x) * 4;
                if (y < 2)
                {
                    Assert.Equal((byte)0, decoded.RgbaPixels[index]);
                    Assert.Equal((byte)0, decoded.RgbaPixels[index + 1]);
                    Assert.Equal((byte)255, decoded.RgbaPixels[index + 2]);
                }
                else
                {
                    Assert.Equal((byte)255, decoded.RgbaPixels[index]);
                    Assert.Equal((byte)0, decoded.RgbaPixels[index + 1]);
                    Assert.Equal((byte)0, decoded.RgbaPixels[index + 2]);
                }
            }
        }
    }

    [Fact]
    public void PngWriter_WritesValidPngSignatureAndIhdr()
    {
        var rgba = new byte[] { 1, 2, 3, 4 };

        using var ms = new MemoryStream();
        PngWriter.WriteRgbaToStream(ms, 1, 1, rgba);
        var bytes = ms.ToArray();

        Assert.True(bytes.AsSpan(0, 8).SequenceEqual(new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 }));
        Assert.Equal("IHDR", System.Text.Encoding.ASCII.GetString(bytes, 12, 4));
        Assert.Equal((uint)1, BinaryPrimitives.ReadUInt32BigEndian(bytes.AsSpan(16, 4)));
        Assert.Equal((uint)1, BinaryPrimitives.ReadUInt32BigEndian(bytes.AsSpan(20, 4)));
        Assert.Equal((byte)6, bytes[25]); // RGBA color type
    }

    [Fact]
    public void PngReader_ReadsPngWrittenByPngWriter()
    {
        const int width = 4;
        const int height = 4;
        var expected = BuildExpectedRgba(width, height, (x, y) => ((byte)(x * 20), (byte)(y * 30), (byte)(x + y), (byte)(255 - x * 10)));

        using var ms = new MemoryStream();
        PngWriter.WriteRgbaToStream(ms, width, height, expected);
        ms.Position = 0;

        var decoded = PngReader.ReadFromStream(ms);

        Assert.Equal(width, decoded.Width);
        Assert.Equal(height, decoded.Height);
        Assert.Equal(expected, decoded.RgbaPixels);
    }

    [Fact]
    public void PngReader_ReadsInterlacedPng()
    {
        const int width = 8;
        const int height = 8;
        var expected = BuildExpectedRgba(width, height, (x, y) => ((byte)(x * 30), (byte)(y * 20), (byte)(x + y), (byte)(255 - y * 10)));
        var interlacedPng = BuildInterlacedPngRgba(width, height, expected);

        using var ms = new MemoryStream(interlacedPng);
        var decoded = PngReader.ReadFromStream(ms);

        Assert.Equal(width, decoded.Width);
        Assert.Equal(height, decoded.Height);
        Assert.Equal(expected, decoded.RgbaPixels);
    }

    [Fact]
    public void PngReader_Reads16BitRgbaPng()
    {
        const int width = 4;
        const int height = 4;
        var expected = BuildExpectedRgba(width, height, (x, y) => ((byte)(x * 40), (byte)(y * 50), (byte)(x + y + 10), (byte)(255 - x * 20)));
        var png16 = BuildPngRgba16(width, height, expected);

        using var ms = new MemoryStream(png16);
        var decoded = PngReader.ReadFromStream(ms);

        Assert.Equal(width, decoded.Width);
        Assert.Equal(height, decoded.Height);
        Assert.Equal(expected, decoded.RgbaPixels);
    }

    [Fact]
    public void Etc1Decoder_UsesCtrWordLayoutAndModifierSet()
    {
        // One ETC1 4x4 block: low/selectors first (little-endian), then high/color word (little-endian).
        const uint lowWord = 0x00000000; // all selectors choose code 0
        const uint highWord =
            (10u << 27) | // r base (5-bit)
            (0u << 24) |  // dr
            (20u << 19) | // g base (5-bit)
            (0u << 16) |  // dg
            (5u << 11) |  // b base (5-bit)
            (0u << 8) |   // db
            (0u << 5) |   // table1
            (0u << 2) |   // table2
            (1u << 1) |   // differential mode
            0u;           // non-flipped

        var block = new byte[8];
        BinaryPrimitives.WriteUInt32LittleEndian(block.AsSpan(0, 4), lowWord);
        BinaryPrimitives.WriteUInt32LittleEndian(block.AsSpan(4, 4), highWord);

        var decoded = Etc1Decoder.DecodeEtc1(block, 4, 4);

        const byte expectedR = 84;  // Expand5(10) + modifier(+2)
        const byte expectedG = 167; // Expand5(20) + modifier(+2)
        const byte expectedB = 43;  // Expand5(5) + modifier(+2)

        for (var i = 0; i < decoded.Length; i += 4)
        {
            Assert.Equal(expectedR, decoded[i]);
            Assert.Equal(expectedG, decoded[i + 1]);
            Assert.Equal(expectedB, decoded[i + 2]);
            Assert.Equal((byte)255, decoded[i + 3]);
        }
    }

    [Fact]
    public void ImgEncoder_AndDecoder_RoundtripRgba8888()
    {
        const int width = 8;
        const int height = 8;
        var expected = BuildExpectedRgba(width, height, (x, y) => ((byte)(x * 11), (byte)(y * 13), (byte)(200 - y), (byte)(x + y)));

        var image = new DecodedImage { Width = width, Height = height, RgbaPixels = expected };
        var options = new DecodeOptions
        {
            FlipVertical = true,
            UseSwizzle = true,
            RgbaOrder32 = ChannelOrder32.Abgr
        };

        var img = ImgEncoder.Encode(image, ImgPixelFormat.Rgba8888, options);
        var decoded = ImgDecoder.DecodeBytes(img, options);

        Assert.Equal(expected, decoded.RgbaPixels);
    }

    [Fact]
    public void ImgEncoder_AndDecoder_RoundtripUnknown1()
    {
        const int width = 8;
        const int height = 8;
        var expected = BuildExpectedRgba(width, height, (x, y) => ((byte)(x * 17), (byte)(y * 11), (byte)(150 - y), (byte)(255 - x * 5)));

        var image = new DecodedImage { Width = width, Height = height, RgbaPixels = expected };
        var options = new DecodeOptions
        {
            FlipVertical = true,
            UseSwizzle = true
        };

        var img = ImgEncoder.Encode(image, ImgPixelFormat.Unknown1, options);
        var decoded = ImgDecoder.DecodeBytes(img, options);

        Assert.Equal(expected, decoded.RgbaPixels);
    }

    [Fact]
    public void ImgEncoder_AndDecoder_RoundtripUnknown7_AndUnknown8()
    {
        const int width = 8;
        const int height = 8;
        var expected = BuildExpectedRgba(width, height, (x, y) =>
        {
            var alpha4 = (x + y) & 0x0F;
            var alpha = (byte)((alpha4 << 4) | alpha4);
            return ((byte)255, (byte)255, (byte)255, alpha);
        });

        var image = new DecodedImage { Width = width, Height = height, RgbaPixels = expected };
        var options = new DecodeOptions
        {
            FlipVertical = true,
            UseSwizzle = true
        };

        var img7 = ImgEncoder.Encode(image, ImgPixelFormat.Unknown7, options);
        var decoded7 = ImgDecoder.DecodeBytes(img7, options);
        Assert.Equal(expected, decoded7.RgbaPixels);

        var img8 = ImgEncoder.Encode(image, ImgPixelFormat.Unknown8, options);
        var decoded8 = ImgDecoder.DecodeBytes(img8, options);
        Assert.Equal(expected, decoded8.RgbaPixels);
    }

    [Fact]
    public void Converter_CreatesPngOutput()
    {
        var root = Path.Combine(Path.GetTempPath(), "imgloader-tests-" + Guid.NewGuid());
        Directory.CreateDirectory(root);
        try
        {
            var input = Path.Combine(root, "sample.img");
            var outputDir = Path.Combine(root, "out");

            const int width = 4;
            const int height = 4;
            var expected = BuildExpectedRgba(width, height, (x, y) => ((byte)(x * 40), (byte)(y * 40), (byte)100, (byte)255));
            var raw = EncodeRgb24(width, height, expected, ChannelOrder24.Rgb, swizzled: false);
            var imgBytes = BuildImgFile(ImgPixelFormat.Rgb8, width, height, 0x18, raw);
            File.WriteAllBytes(input, imgBytes);

            var converted = ImgConverter.Convert(new ImageConversionOptions
            {
                InputPath = input,
                OutputDirectory = outputDir,
                FlipVertical = false,
                UseSwizzle = false,
                RgbOrder24 = ChannelOrder24.Rgb,
                RgbaOrder32 = ChannelOrder32.Abgr
            });

            Assert.Single(converted);
            var outputPath = Path.Combine(outputDir, "sample_0x02.png");
            Assert.True(File.Exists(outputPath));

            var png = File.ReadAllBytes(outputPath);
            Assert.True(png.AsSpan(0, 8).SequenceEqual(new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 }));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void Converter_ToImg_UsesImageInFolder()
    {
        var root = Path.Combine(Path.GetTempPath(), "imgloader-tests-" + Guid.NewGuid());
        Directory.CreateDirectory(root);
        try
        {
            var imageIn = Path.Combine(root, "Image_In");
            var imageOut = Path.Combine(root, "Image_Out");
            Directory.CreateDirectory(imageIn);

            const int width = 8;
            const int height = 8;
            var rgba = BuildExpectedRgba(width, height, (x, y) => ((byte)(x * 16), (byte)(y * 12), (byte)100, (byte)255));
            var pngPath = Path.Combine(imageIn, "sample.png");
            PngWriter.WriteRgbaToFile(pngPath, width, height, rgba);

            var converted = ImgConverter.Convert(new ImageConversionOptions
            {
                InputPath = imageIn,
                OutputDirectory = imageOut,
                Mode = ConversionMode.ToImg,
                ImgOutputFormat = ImgPixelFormat.Rgb8,
                FlipVertical = true,
                UseSwizzle = true,
                RgbaOrder32 = ChannelOrder32.Abgr
            });

            Assert.Single(converted);
            var imgPath = Path.Combine(imageOut, "sample.img");
            Assert.True(File.Exists(imgPath));
            var header = ImgHeader.Parse(File.ReadAllBytes(imgPath));
            Assert.Equal(ImgPixelFormat.Rgb8, header.Format);

            var decoded = ImgDecoder.DecodeFile(imgPath, new DecodeOptions
            {
                FlipVertical = true,
                UseSwizzle = true,
                RgbOrder24 = ChannelOrder24.Rgb
            });

            Assert.Equal(rgba, decoded.RgbaPixels);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void Converter_ToImg_SingleFile_RespectsRequestedFormat()
    {
        var root = Path.Combine(Path.GetTempPath(), "imgloader-tests-" + Guid.NewGuid());
        Directory.CreateDirectory(root);
        try
        {
            var pngPath = Path.Combine(root, "single.png");
            var outputDir = Path.Combine(root, "out");
            const int width = 8;
            const int height = 8;
            var rgba = BuildExpectedRgba(width, height, (x, y) => ((byte)(x * 10), (byte)(y * 10), (byte)42, (byte)255));
            PngWriter.WriteRgbaToFile(pngPath, width, height, rgba);

            var converted = ImgConverter.Convert(new ImageConversionOptions
            {
                InputPath = pngPath,
                OutputDirectory = outputDir,
                Mode = ConversionMode.ToImg,
                ImgOutputFormat = ImgPixelFormat.Rgba8888,
                FlipVertical = true,
                UseSwizzle = true,
                RgbaOrder32 = ChannelOrder32.Abgr
            });

            Assert.Single(converted);
            var imgPath = Path.Combine(outputDir, "single.img");
            Assert.True(File.Exists(imgPath));
            var header = ImgHeader.Parse(File.ReadAllBytes(imgPath));
            Assert.Equal(ImgPixelFormat.Rgba8888, header.Format);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void Converter_ToImg_UsesSuffixFormatAndStripsSuffixFromOutputName()
    {
        var root = Path.Combine(Path.GetTempPath(), "imgloader-tests-" + Guid.NewGuid());
        Directory.CreateDirectory(root);
        try
        {
            var inputRoot = Path.Combine(root, "input");
            var outputRoot = Path.Combine(root, "output");
            Directory.CreateDirectory(inputRoot);

            const int width = 8;
            const int height = 8;
            var rgba = BuildExpectedRgba(width, height, (x, y) => ((byte)(x * 9), (byte)(y * 9), (byte)60, (byte)255));
            var pngPath = Path.Combine(inputRoot, "icon_start_0x02.png");
            PngWriter.WriteRgbaToFile(pngPath, width, height, rgba);

            ImgConverter.Convert(new ImageConversionOptions
            {
                InputPath = inputRoot,
                OutputDirectory = outputRoot,
                Mode = ConversionMode.ToImg,
                ImgOutputFormat = ImgPixelFormat.Rgba8888,
                FlipVertical = true,
                UseSwizzle = true,
                RgbOrder24 = ChannelOrder24.Rgb,
                RgbaOrder32 = ChannelOrder32.Abgr
            });

            var expectedOutput = Path.Combine(outputRoot, "icon_start.img");
            Assert.True(File.Exists(expectedOutput));
            var header = ImgHeader.Parse(File.ReadAllBytes(expectedOutput));
            Assert.Equal(ImgPixelFormat.Rgb8, header.Format);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void Converter_ToImg_NoSuffix_InfersRgb8ForOpaquePng()
    {
        var root = Path.Combine(Path.GetTempPath(), "imgloader-tests-" + Guid.NewGuid());
        Directory.CreateDirectory(root);
        try
        {
            var inputRoot = Path.Combine(root, "input");
            var outputRoot = Path.Combine(root, "output");
            Directory.CreateDirectory(inputRoot);

            const int width = 8;
            const int height = 8;
            var rgba = BuildExpectedRgba(width, height, (x, y) => ((byte)(x * 9), (byte)(y * 9), (byte)60, (byte)255));
            var pngPath = Path.Combine(inputRoot, "icon_start.png");
            PngWriter.WriteRgbaToFile(pngPath, width, height, rgba);

            var options = new ImageConversionOptions
            {
                InputPath = inputRoot,
                OutputDirectory = outputRoot,
                Mode = ConversionMode.ToImg,
                InferImgFormatWhenMissingSuffix = true,
                FlipVertical = true,
                UseSwizzle = true,
                RgbaOrder32 = ChannelOrder32.Abgr
            };
            ImgConverter.Convert(options);

            var expectedOutput = Path.Combine(outputRoot, "icon_start.img");
            Assert.True(File.Exists(expectedOutput));
            var header = ImgHeader.Parse(File.ReadAllBytes(expectedOutput));
            Assert.Equal(ImgPixelFormat.Rgb8, header.Format);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void Converter_ToPng_PreservesDirectoryStructure()
    {
        var root = Path.Combine(Path.GetTempPath(), "imgloader-tests-" + Guid.NewGuid());
        Directory.CreateDirectory(root);
        try
        {
            var inputRoot = Path.Combine(root, "input");
            var nested = Path.Combine(inputRoot, "bg", "effects");
            var outputRoot = Path.Combine(root, "output");
            Directory.CreateDirectory(nested);

            const int width = 8;
            const int height = 8;
            var rgba = BuildExpectedRgba(width, height, (x, y) => ((byte)(x * 12), (byte)(y * 9), (byte)50, (byte)255));
            var raw = EncodeRgb24(width, height, rgba, ChannelOrder24.Rgb, swizzled: true);
            var imgBytes = BuildImgFile(ImgPixelFormat.Rgb8, width, height, 0x18, raw);
            var inputImg = Path.Combine(nested, "spark.img");
            File.WriteAllBytes(inputImg, imgBytes);

            ImgConverter.Convert(new ImageConversionOptions
            {
                InputPath = inputRoot,
                OutputDirectory = outputRoot,
                Mode = ConversionMode.ToPng,
                FlipVertical = true,
                UseSwizzle = true,
                RgbOrder24 = ChannelOrder24.Rgb
            });

            var expectedPng = Path.Combine(outputRoot, "bg", "effects", "spark_0x02.png");
            Assert.True(File.Exists(expectedPng));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void Converter_ToImg_PreservesDirectoryStructure()
    {
        var root = Path.Combine(Path.GetTempPath(), "imgloader-tests-" + Guid.NewGuid());
        Directory.CreateDirectory(root);
        try
        {
            var inputRoot = Path.Combine(root, "input");
            var nested = Path.Combine(inputRoot, "ui", "icons");
            var outputRoot = Path.Combine(root, "output");
            Directory.CreateDirectory(nested);

            const int width = 8;
            const int height = 8;
            var rgba = BuildExpectedRgba(width, height, (x, y) => ((byte)(x * 7), (byte)(y * 14), (byte)80, (byte)255));
            var inputPng = Path.Combine(nested, "cursor.png");
            PngWriter.WriteRgbaToFile(inputPng, width, height, rgba);

            ImgConverter.Convert(new ImageConversionOptions
            {
                InputPath = inputRoot,
                OutputDirectory = outputRoot,
                Mode = ConversionMode.ToImg,
                ImgOutputFormat = ImgPixelFormat.Rgba8888,
                FlipVertical = true,
                UseSwizzle = true,
                RgbaOrder32 = ChannelOrder32.Abgr
            });

            var expectedImg = Path.Combine(outputRoot, "ui", "icons", "cursor.img");
            Assert.True(File.Exists(expectedImg));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void Converter_AutoMode_ConvertsImgAndPng()
    {
        var root = Path.Combine(Path.GetTempPath(), "imgloader-tests-" + Guid.NewGuid());
        Directory.CreateDirectory(root);
        try
        {
            var imageIn = Path.Combine(root, "Image_In");
            var imageOut = Path.Combine(root, "Image_Out");
            Directory.CreateDirectory(imageIn);

            const int width = 8;
            const int height = 8;
            var rgba = BuildExpectedRgba(width, height, (x, y) => ((byte)(x * 8), (byte)(y * 8), (byte)120, (byte)255));

            var pngPath = Path.Combine(imageIn, "from_png.png");
            PngWriter.WriteRgbaToFile(pngPath, width, height, rgba);

            var imgRaw = EncodeRgb24(width, height, rgba, ChannelOrder24.Rgb, swizzled: true);
            var imgBytes = BuildImgFile(ImgPixelFormat.Rgb8, width, height, 0x18, imgRaw);
            var imgPath = Path.Combine(imageIn, "from_img.img");
            File.WriteAllBytes(imgPath, imgBytes);

            var converted = ImgConverter.Convert(new ImageConversionOptions
            {
                InputPath = imageIn,
                OutputDirectory = imageOut,
                Mode = ConversionMode.Auto,
                ImgOutputFormat = ImgPixelFormat.Rgba8888,
                FlipVertical = true,
                UseSwizzle = true,
                RgbOrder24 = ChannelOrder24.Rgb,
                RgbaOrder32 = ChannelOrder32.Abgr
            });

            Assert.Equal(2, converted.Count);
            Assert.True(File.Exists(Path.Combine(imageOut, "from_png.img")));
            Assert.True(File.Exists(Path.Combine(imageOut, "from_img_0x02.png")));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void Converter_WithUnsupportedFile_ContinuesAndReportsFailure()
    {
        var root = Path.Combine(Path.GetTempPath(), "imgloader-tests-" + Guid.NewGuid());
        Directory.CreateDirectory(root);
        try
        {
            var inputRoot = Path.Combine(root, "input");
            var outputRoot = Path.Combine(root, "output");
            Directory.CreateDirectory(inputRoot);

            const int width = 8;
            const int height = 8;
            var rgba = BuildExpectedRgba(width, height, (x, y) => ((byte)(x * 5), (byte)(y * 6), (byte)77, (byte)255));
            var validRaw = EncodeRgb24(width, height, rgba, ChannelOrder24.Rgb, swizzled: true);
            File.WriteAllBytes(Path.Combine(inputRoot, "valid.img"), BuildImgFile(ImgPixelFormat.Rgb8, width, height, 0x18, validRaw));

            var unsupportedRaw = new byte[width * height * 4];
            File.WriteAllBytes(Path.Combine(inputRoot, "unsupported.img"), BuildImgFile((ImgPixelFormat)0x09, width, height, 0x20, unsupportedRaw));

            var report = ImgConverter.ConvertWithReport(new ImageConversionOptions
            {
                InputPath = inputRoot,
                OutputDirectory = outputRoot,
                Mode = ConversionMode.ToPng,
                FlipVertical = true,
                UseSwizzle = true,
                RgbOrder24 = ChannelOrder24.Rgb
            });

            Assert.Single(report.Converted);
            Assert.Single(report.Failed);
            Assert.True(File.Exists(Path.Combine(outputRoot, "valid_0x02.png")));
            Assert.Contains("unsupported.img", report.Failed[0].InputPath, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    private static byte[] BuildImgFile(ImgPixelFormat format, int width, int height, int pixelBits, byte[] pixelData)
    {
        var fileBytes = new byte[0x80 + pixelData.Length];
        WriteHeader(fileBytes, format, width, height, pixelBits, 0x80);
        Buffer.BlockCopy(pixelData, 0, fileBytes, 0x80, pixelData.Length);
        return fileBytes;
    }

    private static void WriteHeader(byte[] destination, ImgPixelFormat format, int width, int height, int pixelBits, int dataOffset)
    {
        destination[0] = 0x00;
        destination[1] = 0x63;
        destination[2] = 0x74;
        destination[3] = 0x65;
        BinaryPrimitives.WriteUInt32LittleEndian(destination.AsSpan(0x04, 4), (uint)format);
        BinaryPrimitives.WriteUInt32LittleEndian(destination.AsSpan(0x08, 4), (uint)width);
        BinaryPrimitives.WriteUInt32LittleEndian(destination.AsSpan(0x0C, 4), (uint)height);
        BinaryPrimitives.WriteUInt32LittleEndian(destination.AsSpan(0x10, 4), (uint)pixelBits);
        BinaryPrimitives.WriteUInt32LittleEndian(destination.AsSpan(0x18, 4), (uint)dataOffset);
    }

    private static byte[] BuildExpectedRgba(int width, int height, Func<int, int, (byte r, byte g, byte b, byte a)> generator)
    {
        var rgba = new byte[width * height * 4];
        for (var y = 0; y < height; y++)
        {
            for (var x = 0; x < width; x++)
            {
                var (r, g, b, a) = generator(x, y);
                var offset = (y * width + x) * 4;
                rgba[offset] = r;
                rgba[offset + 1] = g;
                rgba[offset + 2] = b;
                rgba[offset + 3] = a;
            }
        }

        return rgba;
    }

    private static byte[] EncodeRgb24(int width, int height, byte[] expectedRgba, ChannelOrder24 order, bool swizzled)
    {
        var raw = new byte[width * height * 3];
        for (var y = 0; y < height; y++)
        {
            for (var x = 0; x < width; x++)
            {
                var src = (y * width + x) * 4;
                var dstPixel = swizzled ? Swizzle.GetLinearPixelIndexFromCoordinates(x, y, width) : (y * width + x);
                var dst = dstPixel * 3;

                var r = expectedRgba[src];
                var g = expectedRgba[src + 1];
                var b = expectedRgba[src + 2];

                if (order == ChannelOrder24.Rgb)
                {
                    raw[dst] = r;
                    raw[dst + 1] = g;
                    raw[dst + 2] = b;
                }
                else
                {
                    raw[dst] = b;
                    raw[dst + 1] = g;
                    raw[dst + 2] = r;
                }
            }
        }

        return raw;
    }

    private static byte[] EncodeRgba32(int width, int height, byte[] expectedRgba, ChannelOrder32 order, bool swizzled)
    {
        var raw = new byte[width * height * 4];
        for (var y = 0; y < height; y++)
        {
            for (var x = 0; x < width; x++)
            {
                var src = (y * width + x) * 4;
                var dstPixel = swizzled ? Swizzle.GetLinearPixelIndexFromCoordinates(x, y, width) : (y * width + x);
                var dst = dstPixel * 4;

                var r = expectedRgba[src];
                var g = expectedRgba[src + 1];
                var b = expectedRgba[src + 2];
                var a = expectedRgba[src + 3];

                switch (order)
                {
                    case ChannelOrder32.Rgba:
                        raw[dst] = r;
                        raw[dst + 1] = g;
                        raw[dst + 2] = b;
                        raw[dst + 3] = a;
                        break;
                    case ChannelOrder32.Argb:
                        raw[dst] = a;
                        raw[dst + 1] = r;
                        raw[dst + 2] = g;
                        raw[dst + 3] = b;
                        break;
                    case ChannelOrder32.Abgr:
                        raw[dst] = a;
                        raw[dst + 1] = b;
                        raw[dst + 2] = g;
                        raw[dst + 3] = r;
                        break;
                    case ChannelOrder32.Bgra:
                        raw[dst] = b;
                        raw[dst + 1] = g;
                        raw[dst + 2] = r;
                        raw[dst + 3] = a;
                        break;
                    default:
                        throw new ArgumentOutOfRangeException(nameof(order), order, null);
                }
            }
        }

        return raw;
    }

    private static byte[] EncodeXbgr1555BigEndian(int width, int height, byte[] expectedRgba, bool swizzled)
    {
        var raw = new byte[width * height * 2];
        for (var y = 0; y < height; y++)
        {
            for (var x = 0; x < width; x++)
            {
                var src = (y * width + x) * 4;
                var dstPixel = swizzled ? Swizzle.GetLinearPixelIndexFromCoordinates(x, y, width) : (y * width + x);
                var dst = dstPixel * 2;

                var r5 = expectedRgba[src] >> 3;
                var g5 = expectedRgba[src + 1] >> 3;
                var b5 = expectedRgba[src + 2] >> 3;

                var word = (ushort)((b5 << 10) | (g5 << 5) | r5);
                raw[dst] = (byte)(word >> 8);
                raw[dst + 1] = (byte)(word & 0xFF);
            }
        }

        return raw;
    }

    private static byte[] QuantizeTo5BitChannels(byte[] rgba)
    {
        var result = new byte[rgba.Length];
        for (var i = 0; i < rgba.Length; i += 4)
        {
            result[i] = Expand5(rgba[i] >> 3);
            result[i + 1] = Expand5(rgba[i + 1] >> 3);
            result[i + 2] = Expand5(rgba[i + 2] >> 3);
            result[i + 3] = 255;
        }

        return result;
    }

    private static byte[] BuildInterlacedPngRgba(int width, int height, byte[] rgba)
    {
        using var rawStream = new MemoryStream();
        foreach (var pass in Adam7Passes)
        {
            var passWidth = GetPassSize(width, pass.XStart, pass.XStep);
            var passHeight = GetPassSize(height, pass.YStart, pass.YStep);
            if (passWidth == 0 || passHeight == 0)
            {
                continue;
            }

            for (var passY = 0; passY < passHeight; passY++)
            {
                rawStream.WriteByte(0); // filter type: None
                var y = pass.YStart + passY * pass.YStep;
                for (var passX = 0; passX < passWidth; passX++)
                {
                    var x = pass.XStart + passX * pass.XStep;
                    var src = (y * width + x) * 4;
                    rawStream.Write(rgba, src, 4);
                }
            }
        }

        byte[] compressed;
        using (var compressedStream = new MemoryStream())
        {
            using (var z = new ZLibStream(compressedStream, CompressionLevel.SmallestSize, leaveOpen: true))
            {
                rawStream.Position = 0;
                rawStream.CopyTo(z);
            }

            compressed = compressedStream.ToArray();
        }

        using var pngStream = new MemoryStream();
        pngStream.Write([137, 80, 78, 71, 13, 10, 26, 10]);

        Span<byte> ihdr = stackalloc byte[13];
        BinaryPrimitives.WriteUInt32BigEndian(ihdr[..4], (uint)width);
        BinaryPrimitives.WriteUInt32BigEndian(ihdr.Slice(4, 4), (uint)height);
        ihdr[8] = 8; // bit depth
        ihdr[9] = 6; // RGBA
        ihdr[10] = 0;
        ihdr[11] = 0;
        ihdr[12] = 1; // interlaced (Adam7)
        WritePngChunk(pngStream, "IHDR", ihdr);
        WritePngChunk(pngStream, "IDAT", compressed);
        WritePngChunk(pngStream, "IEND", ReadOnlySpan<byte>.Empty);

        return pngStream.ToArray();
    }

    private static byte[] BuildPngRgba16(int width, int height, byte[] rgba8)
    {
        using var rawStream = new MemoryStream();
        for (var y = 0; y < height; y++)
        {
            rawStream.WriteByte(0); // filter None
            for (var x = 0; x < width; x++)
            {
                var src = (y * width + x) * 4;
                // 16-bit sample: duplicate byte (v * 257), so high byte matches original v.
                for (var c = 0; c < 4; c++)
                {
                    var v = rgba8[src + c];
                    rawStream.WriteByte(v);
                    rawStream.WriteByte(v);
                }
            }
        }

        byte[] compressed;
        using (var compressedStream = new MemoryStream())
        {
            using (var z = new ZLibStream(compressedStream, CompressionLevel.SmallestSize, leaveOpen: true))
            {
                rawStream.Position = 0;
                rawStream.CopyTo(z);
            }

            compressed = compressedStream.ToArray();
        }

        using var pngStream = new MemoryStream();
        pngStream.Write([137, 80, 78, 71, 13, 10, 26, 10]);

        Span<byte> ihdr = stackalloc byte[13];
        BinaryPrimitives.WriteUInt32BigEndian(ihdr[..4], (uint)width);
        BinaryPrimitives.WriteUInt32BigEndian(ihdr.Slice(4, 4), (uint)height);
        ihdr[8] = 16; // bit depth
        ihdr[9] = 6; // RGBA
        ihdr[10] = 0;
        ihdr[11] = 0;
        ihdr[12] = 0; // non-interlaced
        WritePngChunk(pngStream, "IHDR", ihdr);
        WritePngChunk(pngStream, "IDAT", compressed);
        WritePngChunk(pngStream, "IEND", ReadOnlySpan<byte>.Empty);

        return pngStream.ToArray();
    }

    private static int GetPassSize(int fullSize, int start, int step)
    {
        return fullSize <= start ? 0 : (fullSize - start + step - 1) / step;
    }

    private static void WritePngChunk(Stream output, string type, ReadOnlySpan<byte> data)
    {
        Span<byte> length = stackalloc byte[4];
        BinaryPrimitives.WriteUInt32BigEndian(length, (uint)data.Length);
        output.Write(length);

        var typeBytes = Encoding.ASCII.GetBytes(type);
        output.Write(typeBytes);
        output.Write(data);

        var crc = Crc32(typeBytes, data);
        Span<byte> crcBytes = stackalloc byte[4];
        BinaryPrimitives.WriteUInt32BigEndian(crcBytes, crc);
        output.Write(crcBytes);
    }

    private static uint Crc32(ReadOnlySpan<byte> type, ReadOnlySpan<byte> data)
    {
        uint crc = 0xFFFFFFFF;
        foreach (var b in type)
        {
            crc = UpdateCrc(crc, b);
        }

        foreach (var b in data)
        {
            crc = UpdateCrc(crc, b);
        }

        return crc ^ 0xFFFFFFFF;
    }

    private static uint UpdateCrc(uint crc, byte value)
    {
        crc ^= value;
        for (var i = 0; i < 8; i++)
        {
            var mask = (crc & 1) != 0 ? 0xEDB88320u : 0u;
            crc = (crc >> 1) ^ mask;
        }

        return crc;
    }

    private static byte Expand5(int value) => (byte)((value << 3) | (value >> 2));
}
