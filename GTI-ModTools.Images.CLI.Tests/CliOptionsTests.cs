using GTI.ModTools.Images;
using GTI.ModTools.Images.CLI;

namespace GTI.ModTools.Images.CLI.Tests;

public class CliOptionsTests
{
    [Fact]
    public void Parse_ToImgFormatRgb8()
    {
        var options = CliOptions.Parse(["--to-img", "--format", "rgb8", "Image_In", "Image_Out"]);

        Assert.Equal(ConversionMode.ToImg, options.Mode);
        Assert.Equal(ImgPixelFormat.Rgb8, options.ImgOutputFormat);
    }

    [Fact]
    public void Parse_DefaultsToAutoImageFolders()
    {
        var options = CliOptions.Parse([]);
        var cwd = Directory.GetCurrentDirectory();
        Assert.Equal(ConversionMode.Auto, options.Mode);
        Assert.Equal(Path.Combine(cwd, "Image_In"), options.InputPath);
        Assert.Equal(Path.Combine(cwd, "Image_Out"), options.OutputDirectory);
    }

    [Fact]
    public void Parse_ToPngFormatPng()
    {
        var options = CliOptions.Parse(["--to-png", "--format", "png"]);
        Assert.Equal(ConversionMode.ToPng, options.Mode);
    }

    [Fact]
    public void Parse_ToPngFormatRgb8_Throws()
    {
        var ex = Assert.Throws<ArgumentException>(() => CliOptions.Parse(["--to-png", "--format", "rgb8"]));
        Assert.Contains("invalid for --to-png", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Parse_AutoFormatRgb8_IsAllowed()
    {
        var options = CliOptions.Parse(["--auto", "--format", "rgb8"]);
        Assert.Equal(ConversionMode.Auto, options.Mode);
        Assert.Equal(ImgPixelFormat.Rgb8, options.ImgOutputFormat);
        Assert.False(options.InferImgFormatWhenMissingSuffix);
    }

    [Fact]
    public void Parse_Default_InfersFormatWhenSuffixMissing()
    {
        var options = CliOptions.Parse(["--to-img"]);
        Assert.True(options.InferImgFormatWhenMissingSuffix);
    }
}
