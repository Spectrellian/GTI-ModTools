using System.Buffers.Binary;

namespace GTI.ModTools.Images.Tests;

public class BaseOrganizerTests
{
    [Fact]
    public void MoveUnmappedImages_MovesOnlyUnreferencedImgs_AndPreservesRelativeFolders()
    {
        var root = Path.Combine(Path.GetTempPath(), "imgloader-base-org-tests-" + Guid.NewGuid());
        Directory.CreateDirectory(root);
        try
        {
            var baseDir = Path.Combine(root, "Base");
            Directory.CreateDirectory(Path.Combine(baseDir, "ui"));
            Directory.CreateDirectory(Path.Combine(baseDir, "fx", "spark"));

            File.WriteAllBytes(Path.Combine(baseDir, "ui", "icon.img"), BuildImg(32, 32));
            File.WriteAllBytes(Path.Combine(baseDir, "ui", "layout.bsji"), BuildMinimalBsjiWithPointerReference("icon", 32, 32));
            File.WriteAllBytes(Path.Combine(baseDir, "fx", "spark", "unused.img"), BuildImg(16, 16));

            var report = BaseOrganizer.MoveUnmappedImages(baseDir);

            Assert.Equal(2, report.TotalImagesScanned);
            Assert.Equal(1, report.ReferencedImages);
            Assert.Equal(1, report.UnmappedCandidates);
            Assert.Single(report.Moved);
            Assert.Empty(report.Failed);

            Assert.True(File.Exists(Path.Combine(baseDir, "ui", "icon.img")));
            Assert.False(File.Exists(Path.Combine(baseDir, "fx", "spark", "unused.img")));
            Assert.True(File.Exists(Path.Combine(baseDir, "Unmapped", "fx", "spark", "unused.img")));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    private static byte[] BuildImg(int width, int height)
    {
        var pixels = new byte[width * height * 4];
        for (var i = 0; i < pixels.Length; i += 4)
        {
            pixels[i] = 60;
            pixels[i + 1] = 120;
            pixels[i + 2] = 180;
            pixels[i + 3] = 255;
        }

        return ImgEncoder.Encode(new DecodedImage
        {
            Width = width,
            Height = height,
            RgbaPixels = pixels
        }, ImgPixelFormat.Rgba8888, new DecodeOptions
        {
            FlipVertical = false,
            UseSwizzle = false,
            RgbaOrder32 = ChannelOrder32.Abgr
        });
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

        bytes[0x40] = 0x04;
        bytes[0x41] = 0x04;
        bytes[0x42] = 0x18;
        bytes[0x43] = 0x00;
        return bytes;
    }

    private static void WriteUtf16String(byte[] buffer, int offset, string value)
    {
        var pos = offset;
        foreach (var ch in value)
        {
            buffer[pos++] = (byte)ch;
            buffer[pos++] = 0;
        }

        buffer[pos++] = 0;
        buffer[pos] = 0;
    }
}
