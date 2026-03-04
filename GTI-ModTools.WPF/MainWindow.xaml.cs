using System;
using System.Collections.ObjectModel;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using GTI.ModTools.FARC;
using GTI.ModTools.Images;
using GTI.ModTools.WPF.Services;

namespace GTI.ModTools.WPF;

public partial class MainWindow : Window
{
    private readonly ObservableCollection<FarcFileRow> _farcFiles = [];
    private readonly ObservableCollection<FarcSectionRow> _farcSections = [];
    private readonly ObservableCollection<string> _farcNames = [];
    private readonly ObservableCollection<ArchiveOptionItem> _archiveOptions = [];
    private readonly WpfToolConfigStore _configStore = new();
    private WpfToolConfig _config = new();
    private bool _isApplyingConfig;
    private string _archiveScanRoot = Directory.GetCurrentDirectory();

    public MainWindow()
    {
        InitializeComponent();

        var cwd = Directory.GetCurrentDirectory();
        ImageConversionDefaults.EnsureWorkspaceFolders(cwd);
        Directory.CreateDirectory(Path.Combine(cwd, "ExportedFiles"));

        _config = _configStore.Load(cwd);
        ApplyConfigToUi(_config);

        FarcFilesDataGrid.ItemsSource = _farcFiles;
        FarcSectionsDataGrid.ItemsSource = _farcSections;
        FarcNamesListBox.ItemsSource = _farcNames;
        ArchiveOptionsItemsControl.ItemsSource = _archiveOptions;

        PersistToolConfig();
        UpdateBaseStatusInLog();
    }

    private void SetFarcStatus(string message)
    {
        FarcStatusTextBlock.Text = message;
    }

    private void AppendImageLog(string message)
    {
        var line = $"[{DateTime.Now:HH:mm:ss}] {message}";
        if (string.IsNullOrWhiteSpace(ImageLogTextBox.Text))
        {
            ImageLogTextBox.Text = line;
            return;
        }

        ImageLogTextBox.AppendText(Environment.NewLine + line);
        ImageLogTextBox.ScrollToEnd();
    }

    private void ApplyConfigToUi(WpfToolConfig config)
    {
        _isApplyingConfig = true;
        try
        {
            FarcInputPathTextBox.Text = config.ArchiveInputPath;
            FarcOutputPathTextBox.Text = config.ArchiveOutputPath;
            FarcRecursiveCheckBox.IsChecked = config.ArchiveRecursive;
            ImageInputPathTextBox.Text = config.ImageInputPath;
            ImageOutputPathTextBox.Text = config.ImageOutputPath;
            BasePathTextBox.Text = config.BasePath;
        }
        finally
        {
            _isApplyingConfig = false;
        }
    }

    private void PersistToolConfig_TextChanged(object sender, TextChangedEventArgs e)
    {
        PersistToolConfig();
    }

    private void PersistToolConfig_RoutedChanged(object sender, RoutedEventArgs e)
    {
        PersistToolConfig();
    }

    private void PersistToolConfig()
    {
        if (_isApplyingConfig)
        {
            return;
        }

        if (!AreConfigControlsReady())
        {
            return;
        }

        _config = new WpfToolConfig
        {
            ArchiveInputPath = FarcInputPathTextBox.Text.Trim(),
            ArchiveOutputPath = FarcOutputPathTextBox.Text.Trim(),
            ArchiveRecursive = FarcRecursiveCheckBox.IsChecked == true,
            ImageInputPath = ImageInputPathTextBox.Text.Trim(),
            ImageOutputPath = ImageOutputPathTextBox.Text.Trim(),
            BasePath = BasePathTextBox.Text.Trim()
        };

        _configStore.Save(_config);
    }

    private bool AreConfigControlsReady()
    {
        return FarcInputPathTextBox is not null &&
               FarcOutputPathTextBox is not null &&
               FarcRecursiveCheckBox is not null &&
               ImageInputPathTextBox is not null &&
               ImageOutputPathTextBox is not null &&
               BasePathTextBox is not null;
    }
}
