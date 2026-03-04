namespace GTI.ModTools.WPF.Services;

public sealed class WpfToolConfig
{
    public string ArchiveInputPath { get; set; } = string.Empty;
    public string ArchiveOutputPath { get; set; } = string.Empty;
    public bool ArchiveRecursive { get; set; } = true;
    public string ImageInputPath { get; set; } = string.Empty;
    public string ImageOutputPath { get; set; } = string.Empty;
    public string BasePath { get; set; } = string.Empty;
}
