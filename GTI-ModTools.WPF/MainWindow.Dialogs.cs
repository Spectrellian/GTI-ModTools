using System.IO;
using System.Windows;
using WinForms = System.Windows.Forms;

namespace GTI.ModTools.WPF;

public partial class MainWindow
{
    private void BrowseFarcInputFile_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new Microsoft.Win32.OpenFileDialog
        {
            Filter = "BIN files (*.bin)|*.bin|All files (*.*)|*.*",
            CheckFileExists = true
        };

        if (dialog.ShowDialog(this) == true)
        {
            FarcInputPathTextBox.Text = dialog.FileName;
        }
    }

    private void BrowseFarcInputFolder_Click(object sender, RoutedEventArgs e)
    {
        if (TryBrowseFolder(FarcInputPathTextBox.Text, out var selected))
        {
            FarcInputPathTextBox.Text = selected;
        }
    }

    private void BrowseFarcOutput_Click(object sender, RoutedEventArgs e)
    {
        if (TryBrowseFolder(FarcOutputPathTextBox.Text, out var selected))
        {
            FarcOutputPathTextBox.Text = selected;
        }
    }

    private void BrowseImageInput_Click(object sender, RoutedEventArgs e)
    {
        if (TryBrowseFolder(ImageInputPathTextBox.Text, out var selected))
        {
            ImageInputPathTextBox.Text = selected;
        }
    }

    private void BrowseImageOutput_Click(object sender, RoutedEventArgs e)
    {
        if (TryBrowseFolder(ImageOutputPathTextBox.Text, out var selected))
        {
            ImageOutputPathTextBox.Text = selected;
        }
    }

    private void BrowseBasePath_Click(object sender, RoutedEventArgs e)
    {
        if (TryBrowseFolder(BasePathTextBox.Text, out var selected))
        {
            BasePathTextBox.Text = selected;
        }
    }

    private static bool TryBrowseFolder(string initialPath, out string selectedPath)
    {
        using var dialog = new WinForms.FolderBrowserDialog
        {
            Description = "Select folder",
            ShowNewFolderButton = true,
            InitialDirectory = Directory.Exists(initialPath) ? initialPath : Directory.GetCurrentDirectory()
        };

        var result = dialog.ShowDialog();
        selectedPath = dialog.SelectedPath;
        return result == WinForms.DialogResult.OK && !string.IsNullOrWhiteSpace(selectedPath);
    }
}
