using GTI.ModTools.WPF;

namespace GTI.ModTools.WPF.Tests;

public class BaseDataGuardTests
{
    [Fact]
    public void HasValidBaseData_ReturnsTrue_WhenImgAndBsjiExist()
    {
        var root = Path.Combine(Path.GetTempPath(), "gti-wpf-tests-" + Guid.NewGuid());
        Directory.CreateDirectory(root);
        try
        {
            File.WriteAllBytes(Path.Combine(root, "a.img"), [1, 2, 3]);
            File.WriteAllBytes(Path.Combine(root, "a.bsji"), [4, 5, 6]);

            Assert.True(BaseDataGuard.HasValidBaseData(root));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void HasValidBaseData_ReturnsFalse_WhenMissingBsji()
    {
        var root = Path.Combine(Path.GetTempPath(), "gti-wpf-tests-" + Guid.NewGuid());
        Directory.CreateDirectory(root);
        try
        {
            File.WriteAllBytes(Path.Combine(root, "a.img"), [1, 2, 3]);
            Assert.False(BaseDataGuard.HasValidBaseData(root));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void AutoModeNeedsBaseData_ReturnsTrue_ForSinglePngFile()
    {
        var root = Path.Combine(Path.GetTempPath(), "gti-wpf-tests-" + Guid.NewGuid());
        Directory.CreateDirectory(root);
        try
        {
            var pngPath = Path.Combine(root, "icon.png");
            File.WriteAllBytes(pngPath, [0x89, 0x50, 0x4E, 0x47]);

            Assert.True(BaseDataGuard.AutoModeNeedsBaseData(pngPath));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void AutoModeNeedsBaseData_ReturnsFalse_ForFolderWithoutPng()
    {
        var root = Path.Combine(Path.GetTempPath(), "gti-wpf-tests-" + Guid.NewGuid());
        Directory.CreateDirectory(root);
        try
        {
            File.WriteAllBytes(Path.Combine(root, "icon.img"), [0, 1, 2]);
            Assert.False(BaseDataGuard.AutoModeNeedsBaseData(root));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }
}
