using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using GTI.ModTools.FARC;

namespace GTI.ModTools.WPF;

public partial class MainWindow
{
    private async void ScanFarc_Click(object sender, RoutedEventArgs e)
    {
        var input = FarcInputPathTextBox.Text.Trim();
        if (string.IsNullOrWhiteSpace(input))
        {
            SetFarcStatus("Set an input path first.");
            return;
        }

        try
        {
            SetFarcStatus("Scanning binary files...");
            var recursive = FarcRecursiveCheckBox.IsChecked == true;
            var scan = await Task.Run(() => ArchiveService.Scan(input, recursive));

            _archiveScanRoot = ArchiveService.GetInputRoot(input);
            _farcFiles.Clear();
            _farcSections.Clear();
            _farcNames.Clear();

            foreach (var analysis in scan.Files)
            {
                _farcFiles.Add(new FarcFileRow(analysis, _archiveScanRoot));
            }

            RefreshArchiveOptions(ArchiveService.GetOptionDefinitionsForFiles(scan.Files));

            var knownByType = scan.Files
                .Where(file => file.IsKnownType)
                .GroupBy(file => file.TypeDisplayName)
                .OrderBy(group => group.Key, StringComparer.OrdinalIgnoreCase)
                .Select(group => $"{group.Key}:{group.Count()}")
                .ToArray();

            var typeSummary = knownByType.Length > 0 ? string.Join(", ", knownByType) : "none";
            SetFarcStatus($"Scanned {scan.Scanned} .bin files. Known={scan.Known} ({typeSummary}), Unknown={scan.Unknown}, Failures={scan.Failures.Count}.");
        }
        catch (Exception ex)
        {
            SetFarcStatus($"Scan failed: {ex.Message}");
        }
    }

    private async void ExtractSelectedFarc_Click(object sender, RoutedEventArgs e)
    {
        if (FarcFilesDataGrid.SelectedItem is not FarcFileRow row)
        {
            SetFarcStatus("Select a file first.");
            return;
        }

        var output = FarcOutputPathTextBox.Text.Trim();
        if (string.IsNullOrWhiteSpace(output))
        {
            SetFarcStatus("Set an output path first.");
            return;
        }

        try
        {
            SetFarcStatus($"Extracting selected ({row.Analysis.TypeDisplayName})...");
            var report = await Task.Run(() => ArchiveService.ExtractFiles([row.Analysis], _archiveScanRoot, output, BuildArchiveOptionMap()));
            SetFarcStatus(FormatExtractStatus(report));
        }
        catch (Exception ex)
        {
            SetFarcStatus($"Extract selected failed: {ex.Message}");
        }
    }

    private async void ExtractAllFarc_Click(object sender, RoutedEventArgs e)
    {
        var input = FarcInputPathTextBox.Text.Trim();
        var output = FarcOutputPathTextBox.Text.Trim();
        if (string.IsNullOrWhiteSpace(input) || string.IsNullOrWhiteSpace(output))
        {
            SetFarcStatus("Set input and output paths first.");
            return;
        }

        try
        {
            SetFarcStatus("Extracting all known binary types...");
            var report = await Task.Run(() => ArchiveService.ExtractFiles(_farcFiles.Select(file => file.Analysis), _archiveScanRoot, output, BuildArchiveOptionMap()));
            SetFarcStatus(FormatExtractStatus(report));
        }
        catch (Exception ex)
        {
            SetFarcStatus($"Extract all failed: {ex.Message}");
        }
    }

    private void FarcFilesDataGrid_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        _farcSections.Clear();
        _farcNames.Clear();

        if (FarcFilesDataGrid.SelectedItem is not FarcFileRow row)
        {
            return;
        }

        foreach (var entry in row.Analysis.Entries)
        {
            _farcSections.Add(new FarcSectionRow(entry));
        }

        foreach (var name in row.Analysis.ReferencedNames)
        {
            _farcNames.Add(name);
        }

        RefreshArchiveOptions(ArchiveService.GetOptionDefinitionsForType(row.Analysis.TypeId));
    }

    private Dictionary<string, bool> BuildArchiveOptionMap()
    {
        return _archiveOptions.ToDictionary(option => option.Key, option => option.IsEnabled, StringComparer.OrdinalIgnoreCase);
    }

    private void RefreshArchiveOptions(IReadOnlyList<ArchiveOptionDefinition> definitions)
    {
        var existing = _archiveOptions.ToDictionary(option => option.Key, option => option.IsEnabled, StringComparer.OrdinalIgnoreCase);

        _archiveOptions.Clear();
        foreach (var definition in definitions)
        {
            var isEnabled = existing.TryGetValue(definition.Key, out var oldValue)
                ? oldValue
                : definition.DefaultValue;

            _archiveOptions.Add(new ArchiveOptionItem(
                definition.Key,
                definition.DisplayName,
                definition.Description,
                isEnabled));
        }
    }

    private static string FormatExtractStatus(ArchiveExtractReport report)
    {
        return $"Extract completed. scanned={report.Scanned}, extracted={report.Extracted}, skipped-unknown={report.SkippedUnknown}, failed={report.Failed.Count}";
    }

    private sealed record FarcFileRow
    {
        public FarcFileRow(ArchiveFileAnalysis analysis, string scanRoot)
        {
            Analysis = analysis;
            Name = Path.GetFileName(analysis.InputPath);
            Type = analysis.TypeDisplayName;
            FileSize = analysis.FileSize;
            SizeDisplay = FormatByteSize(analysis.FileSize);
            RelativePath = Path.GetRelativePath(scanRoot, analysis.InputPath);
            Summary = analysis.Summary;
        }

        public ArchiveFileAnalysis Analysis { get; }
        public string Name { get; }
        public string Type { get; }
        public int FileSize { get; }
        public string SizeDisplay { get; }
        public string RelativePath { get; }
        public string Summary { get; }

        private static string FormatByteSize(long bytes)
        {
            if (bytes < 1024)
            {
                return $"{bytes} B";
            }

            var units = new[] { "KB", "MB", "GB", "TB" };
            var value = bytes / 1024d;
            var unitIndex = 0;
            while (value >= 1024 && unitIndex < units.Length - 1)
            {
                value /= 1024d;
                unitIndex++;
            }

            return $"{value:0.##} {units[unitIndex]}";
        }
    }

    private sealed record FarcSectionRow
    {
        public FarcSectionRow(ArchiveEntryInfo entry)
        {
            Name = entry.Name;
            OffsetHex = entry.Offset;
            Length = entry.Length;
            Kind = entry.Kind;
            Details = entry.Details;
        }

        public string Name { get; }
        public string OffsetHex { get; }
        public string Length { get; }
        public string Kind { get; }
        public string Details { get; }
    }

    private sealed class ArchiveOptionItem
    {
        public ArchiveOptionItem(string key, string displayName, string description, bool isEnabled)
        {
            Key = key;
            DisplayName = displayName;
            Description = description;
            IsEnabled = isEnabled;
        }

        public string Key { get; }
        public string DisplayName { get; }
        public string Description { get; }
        public bool IsEnabled { get; set; }
    }
}
