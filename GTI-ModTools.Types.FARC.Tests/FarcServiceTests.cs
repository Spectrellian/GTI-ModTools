using GTI.ModTools.FARC;

namespace GTI.ModTools.FARC.Tests;

public class FarcServiceTests
{
    [Fact]
    public void AnalyzeFile_DetectsFarcAndSections()
    {
        var root = Path.Combine(Path.GetTempPath(), "gti-farc-tests-" + Guid.NewGuid());
        Directory.CreateDirectory(root);
        try
        {
            var farcPath = Path.Combine(root, "sample.bin");
            File.WriteAllBytes(farcPath, BuildSingleSectionFarc("icon.img"));

            var analysis = FarcService.AnalyzeFile(farcPath);

            Assert.True(analysis.IsFarc);
            Assert.Single(analysis.Sections);
            Assert.Contains("icon.img", analysis.ReferencedNames, StringComparer.OrdinalIgnoreCase);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void AnalyzeFile_NonFarc_ReturnsIsFarcFalse()
    {
        var root = Path.Combine(Path.GetTempPath(), "gti-farc-tests-" + Guid.NewGuid());
        Directory.CreateDirectory(root);
        try
        {
            var path = Path.Combine(root, "not_farc.bin");
            File.WriteAllBytes(path, "notfarc"u8.ToArray());

            var analysis = FarcService.AnalyzeFile(path);
            Assert.False(analysis.IsFarc);
            Assert.Empty(analysis.Sections);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void ExtractSelectedFiles_WritesManifestAndSectionFiles()
    {
        var root = Path.Combine(Path.GetTempPath(), "gti-farc-tests-" + Guid.NewGuid());
        Directory.CreateDirectory(root);
        try
        {
            var farcPath = Path.Combine(root, "sample.bin");
            var outputRoot = Path.Combine(root, "out");
            File.WriteAllBytes(farcPath, BuildSingleSectionFarc("menu_cursor.img"));

            var report = FarcService.ExtractSelectedFiles([farcPath], outputRoot, carveBch: false);

            Assert.Equal(1, report.Scanned);
            Assert.Equal(1, report.Extracted);
            Assert.Equal(0, report.SkippedNonFarc);
            Assert.Empty(report.Failed);
            Assert.NotEmpty(report.OutputDirectories);

            var outputDir = report.OutputDirectories[0];
            Assert.True(File.Exists(Path.Combine(outputDir, "manifest.json")));
            Assert.True(Directory.EnumerateFiles(outputDir, "section_0000_*", SearchOption.TopDirectoryOnly).Any());
            Assert.True(File.Exists(Path.Combine(outputDir, "Extracted", "menu_cursor.img")));
            Assert.True(File.Exists(Path.Combine(outputDir, "Extracted", "named_mapping_report.json")));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    private static byte[] BuildSingleSectionFarc(string referencedName)
    {
        var section = BuildSectionPayload(referencedName);
        var sectionOffset = 0x28;
        var totalLength = sectionOffset + section.Length;
        var bytes = new byte[totalLength];

        bytes[0] = (byte)'F';
        bytes[1] = (byte)'A';
        bytes[2] = (byte)'R';
        bytes[3] = (byte)'C';

        BitConverter.GetBytes((uint)1).CopyTo(bytes, 0x20);
        BitConverter.GetBytes((uint)sectionOffset).CopyTo(bytes, 0x24);
        section.CopyTo(bytes, sectionOffset);

        return bytes;
    }

    private static byte[] BuildSectionPayload(string referencedName)
    {
        var payload = new List<byte>();
        payload.Add((byte)'S');
        payload.Add((byte)'I');
        payload.Add((byte)'R');
        payload.Add((byte)'0');
        payload.AddRange(new byte[8]);
        payload.AddRange(System.Text.Encoding.ASCII.GetBytes(referencedName));
        payload.Add(0);
        payload.AddRange(new byte[8]);
        return payload.ToArray();
    }
}
