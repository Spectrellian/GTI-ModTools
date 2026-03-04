using System.Buffers.Binary;

namespace GTI.ModTools.Images;

public sealed record BsjiImageReference(
    string Name,
    int NameOffset,
    int? WidthOffset,
    int? HeightOffset);

public sealed class BsjiDocument
{
    private readonly byte[] _data;
    private readonly List<BsjiImageReference> _references;

    private BsjiDocument(byte[] data, List<BsjiImageReference> references)
    {
        _data = data;
        _references = references;
    }

    public IReadOnlyList<BsjiImageReference> References => _references;

    public IReadOnlyCollection<string> ReferencedImageNames =>
        _references
            .Select(reference => reference.Name)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

    public static BsjiDocument Load(string path)
    {
        var bytes = File.ReadAllBytes(path);
        return Parse(bytes);
    }

    public static BsjiDocument Parse(byte[] data)
    {
        if (data.Length < 0x10)
        {
            throw new InvalidDataException("BSJI file too small.");
        }

        if (!(data[0] == (byte)'S' && data[1] == (byte)'I' && data[2] == (byte)'R' && data[3] == (byte)'0'))
        {
            throw new InvalidDataException("BSJI is not a SIR0 file.");
        }

        var pointerTableOffsetRaw = BinaryPrimitives.ReadUInt32LittleEndian(data.AsSpan(0x08, 4));
        if (pointerTableOffsetRaw > int.MaxValue)
        {
            throw new InvalidDataException("BSJI has invalid SIR0 pointer table offset.");
        }

        var pointerTableOffset = (int)pointerTableOffsetRaw;
        if (pointerTableOffset <= 0 || pointerTableOffset >= data.Length)
        {
            throw new InvalidDataException("BSJI has invalid SIR0 pointer table offset.");
        }

        var references = new List<BsjiImageReference>();
        var knownNameOffsets = new HashSet<int>();

        foreach (var pointerOffset in DecodeSir0PointerOffsets(data, pointerTableOffset))
        {
            if (pointerOffset < 0 || pointerOffset + 4 > data.Length)
            {
                continue;
            }

            var targetOffsetRaw = BinaryPrimitives.ReadUInt32LittleEndian(data.AsSpan(pointerOffset, 4));
            if (targetOffsetRaw > int.MaxValue)
            {
                continue;
            }

            var targetOffset = (int)targetOffsetRaw;
            if (!TryReadUtf16String(data, targetOffset, out var rawName))
            {
                continue;
            }

            var normalizedName = NormalizeImageName(rawName);
            if (string.IsNullOrWhiteSpace(normalizedName))
            {
                continue;
            }

            knownNameOffsets.Add(targetOffset);

            int? widthOffset = null;
            int? heightOffset = null;
            if (TryReadDimensionPair(data, pointerOffset + 8, out var widthValue, out var heightValue) &&
                IsLikelyDimensionPair(widthValue, heightValue))
            {
                widthOffset = pointerOffset + 8;
                heightOffset = pointerOffset + 12;
            }

            references.Add(new BsjiImageReference(
                Name: normalizedName,
                NameOffset: targetOffset,
                WidthOffset: widthOffset,
                HeightOffset: heightOffset));
        }

        foreach (var (offset, name) in ScanUtf16Strings(data))
        {
            var normalizedName = NormalizeImageName(name);
            if (string.IsNullOrWhiteSpace(normalizedName) || knownNameOffsets.Contains(offset))
            {
                continue;
            }

            references.Add(new BsjiImageReference(
                Name: normalizedName,
                NameOffset: offset,
                WidthOffset: null,
                HeightOffset: null));
        }

        foreach (var (offset, name) in ScanAsciiStrings(data))
        {
            var normalizedName = NormalizeImageName(name);
            if (string.IsNullOrWhiteSpace(normalizedName))
            {
                continue;
            }

            if (references.Any(reference =>
                    reference.NameOffset == offset &&
                    string.Equals(reference.Name, normalizedName, StringComparison.OrdinalIgnoreCase)))
            {
                continue;
            }

            references.Add(new BsjiImageReference(
                Name: normalizedName,
                NameOffset: offset,
                WidthOffset: null,
                HeightOffset: null));
        }

        return new BsjiDocument(data, references);
    }

    public bool ApplyImageSize(string imageName, int baseWidth, int baseHeight, int newWidth, int newHeight)
    {
        var changed = false;
        var patchedOffsets = new HashSet<int>();
        var matches = _references
            .Where(reference => string.Equals(reference.Name, imageName, StringComparison.OrdinalIgnoreCase))
            .ToList();

        foreach (var match in matches)
        {
            if (match.WidthOffset is not int widthOffset || match.HeightOffset is not int heightOffset)
            {
                continue;
            }

            if (widthOffset + 4 > _data.Length || heightOffset + 4 > _data.Length)
            {
                continue;
            }

            var currentWidth = BinaryPrimitives.ReadUInt32LittleEndian(_data.AsSpan(widthOffset, 4));
            var currentHeight = BinaryPrimitives.ReadUInt32LittleEndian(_data.AsSpan(heightOffset, 4));

            if (currentWidth != (uint)baseWidth || currentHeight != (uint)baseHeight)
            {
                continue;
            }

            BinaryPrimitives.WriteUInt32LittleEndian(_data.AsSpan(widthOffset, 4), (uint)newWidth);
            BinaryPrimitives.WriteUInt32LittleEndian(_data.AsSpan(heightOffset, 4), (uint)newHeight);
            patchedOffsets.Add(widthOffset);
            changed = true;
        }

        foreach (var match in matches)
        {
            if (TryFindDimensionOffsetsNearName(match.NameOffset, baseWidth, baseHeight, out var widthOffset) &&
                patchedOffsets.Add(widthOffset))
            {
                BinaryPrimitives.WriteUInt32LittleEndian(_data.AsSpan(widthOffset, 4), (uint)newWidth);
                BinaryPrimitives.WriteUInt32LittleEndian(_data.AsSpan(widthOffset + 4, 4), (uint)newHeight);
                changed = true;
            }
        }

        return changed;
    }

    public byte[] ToBytes()
    {
        return _data.ToArray();
    }

    private bool TryFindDimensionOffsetsNearName(int nameOffset, int baseWidth, int baseHeight, out int widthOffset)
    {
        widthOffset = 0;

        var start = Math.Max(0, nameOffset - 0x80);
        var end = Math.Min(_data.Length - 8, nameOffset + 0x200);
        var candidates = new List<int>();

        for (var offset = start; offset <= end; offset += 4)
        {
            var width = BinaryPrimitives.ReadUInt32LittleEndian(_data.AsSpan(offset, 4));
            var height = BinaryPrimitives.ReadUInt32LittleEndian(_data.AsSpan(offset + 4, 4));
            if (width == (uint)baseWidth && height == (uint)baseHeight)
            {
                candidates.Add(offset);
            }
        }

        if (candidates.Count == 0)
        {
            return false;
        }

        widthOffset = candidates
            .OrderBy(offset => Math.Abs(offset - nameOffset))
            .First();
        return true;
    }

    private static IEnumerable<(int Offset, string Value)> ScanUtf16Strings(byte[] data)
    {
        for (var i = 0; i + 3 < data.Length; i++)
        {
            if (data[i + 1] != 0 || !IsPrintableAscii(data[i]))
            {
                continue;
            }

            var j = i;
            var chars = new List<char>();
            while (j + 1 < data.Length)
            {
                if (data[j] == 0 && data[j + 1] == 0)
                {
                    break;
                }

                if (data[j + 1] != 0 || !IsPrintableAscii(data[j]))
                {
                    chars.Clear();
                    break;
                }

                chars.Add((char)data[j]);
                j += 2;
            }

            if (chars.Count >= 3 && j + 1 < data.Length && data[j] == 0 && data[j + 1] == 0)
            {
                yield return (i, new string(chars.ToArray()));
                i = j + 1;
            }
        }
    }

    private static IEnumerable<(int Offset, string Value)> ScanAsciiStrings(byte[] data)
    {
        for (var i = 0; i < data.Length; i++)
        {
            if (!IsPrintableAscii(data[i]))
            {
                continue;
            }

            var j = i;
            var chars = new List<char>();
            while (j < data.Length && IsPrintableAscii(data[j]))
            {
                chars.Add((char)data[j]);
                j++;
            }

            if (chars.Count >= 3 && j < data.Length && data[j] == 0)
            {
                yield return (i, new string(chars.ToArray()));
                i = j;
            }
        }
    }

    private static List<int> DecodeSir0PointerOffsets(byte[] data, int pointerTableOffset)
    {
        var offsets = new List<int>();
        var runningOffset = 0;
        var value = 0;

        for (var i = pointerTableOffset; i < data.Length; i++)
        {
            var b = data[i];
            if (b == 0)
            {
                break;
            }

            value = (value << 7) | (b & 0x7F);
            if ((b & 0x80) != 0)
            {
                continue;
            }

            runningOffset += value;
            offsets.Add(runningOffset);
            value = 0;
        }

        return offsets;
    }

    private static bool TryReadUtf16String(byte[] data, int offset, out string value)
    {
        value = string.Empty;
        if (offset < 0 || offset + 1 >= data.Length)
        {
            return false;
        }

        var chars = new List<char>();
        for (var i = offset; i + 1 < data.Length; i += 2)
        {
            var code = BinaryPrimitives.ReadUInt16LittleEndian(data.AsSpan(i, 2));
            if (code == 0)
            {
                if (chars.Count < 3)
                {
                    return false;
                }

                value = new string(chars.ToArray());
                return true;
            }

            if (code is < 32 or > 126)
            {
                return false;
            }

            chars.Add((char)code);
        }

        return false;
    }

    private static string NormalizeImageName(string raw)
    {
        var trimmed = raw.Trim().Trim('"', '\'');
        if (string.IsNullOrWhiteSpace(trimmed))
        {
            return string.Empty;
        }

        var leaf = Path.GetFileName(trimmed);
        if (string.IsNullOrWhiteSpace(leaf))
        {
            return string.Empty;
        }

        var extension = Path.GetExtension(leaf);
        if (!string.IsNullOrWhiteSpace(extension))
        {
            leaf = Path.GetFileNameWithoutExtension(leaf);
        }

        if (string.IsNullOrWhiteSpace(leaf) || leaf.Equals("none", StringComparison.OrdinalIgnoreCase))
        {
            return string.Empty;
        }

        return leaf;
    }

    private static bool IsPrintableAscii(byte value)
    {
        return value is >= 32 and <= 126;
    }

    private static bool TryReadDimensionPair(byte[] data, int widthOffset, out uint width, out uint height)
    {
        width = 0;
        height = 0;
        if (widthOffset < 0 || widthOffset + 8 > data.Length)
        {
            return false;
        }

        width = BinaryPrimitives.ReadUInt32LittleEndian(data.AsSpan(widthOffset, 4));
        height = BinaryPrimitives.ReadUInt32LittleEndian(data.AsSpan(widthOffset + 4, 4));
        return true;
    }

    private static bool IsLikelyDimensionPair(uint width, uint height)
    {
        const uint MaxDimension = 16384;
        return width is > 0 and <= MaxDimension && height is > 0 and <= MaxDimension;
    }
}
