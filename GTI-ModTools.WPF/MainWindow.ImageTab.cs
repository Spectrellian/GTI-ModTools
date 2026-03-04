using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using GTI.ModTools.Images;

namespace GTI.ModTools.WPF;

public partial class MainWindow
{
    private async void ConvertToPng_Click(object sender, RoutedEventArgs e)
    {
        var options = BuildImageOptions(ConversionMode.ToPng);
        await RunImageConversionAsync(options, "IMG -> PNG");
    }

    private async void ConvertToImg_Click(object sender, RoutedEventArgs e)
    {
        if (!BaseDataGuard.HasValidBaseData(BasePathTextBox.Text))
        {
            ShowNoBaseOverlay();
            AppendImageLog("Blocked PNG -> IMG: No base data from GTI.");
            return;
        }

        var options = BuildImageOptions(ConversionMode.ToImg);
        await RunImageConversionAsync(options, "PNG -> IMG");
    }

    private async void AutoConvert_Click(object sender, RoutedEventArgs e)
    {
        if (BaseDataGuard.AutoModeNeedsBaseData(ImageInputPathTextBox.Text) && !BaseDataGuard.HasValidBaseData(BasePathTextBox.Text))
        {
            ShowNoBaseOverlay();
            AppendImageLog("Blocked auto convert: PNG input detected but no GTI base data found.");
            return;
        }

        var options = BuildImageOptions(ConversionMode.Auto);
        await RunImageConversionAsync(options, "Auto convert");
    }

    private async Task RunImageConversionAsync(ImageConversionOptions options, string label)
    {
        try
        {
            AppendImageLog($"{label} started.");
            var report = await Task.Run(() => ImgConverter.ConvertWithReport(options));

            AppendImageLog($"{label} completed. Converted: {report.Converted.Count}, failed: {report.Failed.Count}.");
            foreach (var item in report.Converted.Take(20))
            {
                AppendImageLog($"  {item.InputPath} -> {item.OutputPath}");
            }

            if (report.Converted.Count > 20)
            {
                AppendImageLog($"  ... and {report.Converted.Count - 20} more converted files");
            }

            foreach (var failure in report.Failed.Take(20))
            {
                AppendImageLog($"  ERROR {failure.InputPath}: {failure.Error}");
            }

            if (report.Failed.Count > 20)
            {
                AppendImageLog($"  ... and {report.Failed.Count - 20} more errors");
            }
        }
        catch (Exception ex)
        {
            AppendImageLog($"{label} failed: {ex.Message}");
        }
    }

    private ImageConversionOptions BuildImageOptions(ConversionMode mode)
    {
        var formatTag = (ImageFormatComboBox.SelectedItem as ComboBoxItem)?.Tag?.ToString() ?? "auto";
        var infer = true;
        var outputFormat = ImgPixelFormat.Rgba8888;

        if (string.Equals(formatTag, "rgb8", StringComparison.OrdinalIgnoreCase))
        {
            infer = false;
            outputFormat = ImgPixelFormat.Rgb8;
        }
        else if (string.Equals(formatTag, "rgba8888", StringComparison.OrdinalIgnoreCase))
        {
            infer = false;
            outputFormat = ImgPixelFormat.Rgba8888;
        }

        return new ImageConversionOptions
        {
            InputPath = Path.GetFullPath(ImageInputPathTextBox.Text.Trim()),
            OutputDirectory = Path.GetFullPath(ImageOutputPathTextBox.Text.Trim()),
            BaseDirectory = Path.GetFullPath(BasePathTextBox.Text.Trim()),
            Mode = mode,
            ImgOutputFormat = outputFormat,
            InferImgFormatWhenMissingSuffix = infer,
            FlipVertical = true,
            UseSwizzle = true,
            RgbOrder24 = ChannelOrder24.Rgb,
            RgbaOrder32 = ChannelOrder32.Abgr
        };
    }

    private void UpdateBaseStatusInLog()
    {
        if (BaseDataGuard.HasValidBaseData(BasePathTextBox.Text))
        {
            AppendImageLog("Base check: GTI base data detected.");
        }
        else
        {
            AppendImageLog("Base check: missing GTI base data (.img + .bsji).");
        }
    }

    private void ShowNoBaseOverlay()
    {
        BaseOverlay.Visibility = Visibility.Visible;
    }

    private void CloseOverlay_Click(object sender, RoutedEventArgs e)
    {
        BaseOverlay.Visibility = Visibility.Collapsed;
    }

    private void BasePathTextBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        BaseOverlay.Visibility = Visibility.Collapsed;
        PersistToolConfig();
    }
}
