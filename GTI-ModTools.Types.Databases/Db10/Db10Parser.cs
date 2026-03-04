using System.Buffers.Binary;
using System.Text;

namespace GTI.ModTools.Databases;

public static class Db10Parser
{
    private const int HeaderSize = 0x20;

    public static bool IsDb10(ReadOnlySpan<byte> bytes)
    {
        return TryParseHeader(bytes, out _, out _);
    }

    public static bool TryParse(ReadOnlySpan<byte> bytes, out Db10Document document, out string error)
    {
        document = default!;
        error = string.Empty;

        if (!TryParseHeader(bytes, out var header, out error))
        {
            return false;
        }

        var records = new List<Db10Record>(header.Count);
        for (var i = 0; i < header.Count; i++)
        {
            var start = HeaderSize + i * header.RecordSize;
            var recordBytes = bytes.Slice(start, header.RecordSize).ToArray();
            records.Add(new Db10Record(
                Index: i,
                Offset: start,
                Length: header.RecordSize,
                Strings: CollectStrings(recordBytes)));
        }

        document = new Db10Document(header, records);
        return true;
    }

    private static bool TryParseHeader(ReadOnlySpan<byte> bytes, out Db10Header header, out string error)
    {
        header = default;
        error = string.Empty;

        if (bytes.Length < HeaderSize)
        {
            error = "File too small for db_1.0 header.";
            return false;
        }

        var magic = Encoding.ASCII.GetString(bytes.Slice(0, 12)).TrimEnd('\0');
        if (!magic.EndsWith("_db_1.0", StringComparison.Ordinal))
        {
            error = "Magic does not match *_db_1.0.";
            return false;
        }

        var rawCount = BinaryPrimitives.ReadUInt32LittleEndian(bytes.Slice(0x10, 4));
        if (rawCount == 0 || rawCount > 100_000)
        {
            error = "Invalid record count.";
            return false;
        }

        var payloadLength = bytes.Length - HeaderSize;
        if (payloadLength <= 0 || payloadLength % rawCount != 0)
        {
            error = "Payload size is not divisible by record count.";
            return false;
        }

        var recordSize = payloadLength / checked((int)rawCount);
        if (recordSize <= 0)
        {
            error = "Invalid record size.";
            return false;
        }

        header = new Db10Header(
            Magic: magic,
            Count: (int)rawCount,
            RecordSize: recordSize,
            Unknown0C: BinaryPrimitives.ReadUInt32LittleEndian(bytes.Slice(0x0C, 4)),
            Unknown14: BinaryPrimitives.ReadUInt32LittleEndian(bytes.Slice(0x14, 4)),
            Unknown18: BinaryPrimitives.ReadUInt32LittleEndian(bytes.Slice(0x18, 4)));
        return true;
    }

    private static IReadOnlyList<string> CollectStrings(byte[] bytes)
    {
        var values = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var value in ScanUtf16Strings(bytes))
        {
            values.Add(value);
        }

        foreach (var value in ScanAsciiStrings(bytes))
        {
            values.Add(value);
        }

        return values
            .OrderBy(value => value, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private static IEnumerable<string> ScanAsciiStrings(byte[] data)
    {
        var index = 0;
        while (index < data.Length)
        {
            if (!IsPrintableAscii(data[index]))
            {
                index++;
                continue;
            }

            var start = index;
            while (index < data.Length && IsPrintableAscii(data[index]))
            {
                index++;
            }

            var length = index - start;
            if (length >= 4)
            {
                var value = Encoding.ASCII.GetString(data, start, length);
                if (LooksUseful(value))
                {
                    yield return value;
                }
            }
        }
    }

    private static IEnumerable<string> ScanUtf16Strings(byte[] data)
    {
        for (var i = 0; i + 3 < data.Length; i++)
        {
            if (!IsPrintableAscii(data[i]) || data[i + 1] != 0)
            {
                continue;
            }

            var chars = new List<char>();
            var cursor = i;
            while (cursor + 1 < data.Length)
            {
                var b0 = data[cursor];
                var b1 = data[cursor + 1];
                if (b0 == 0 && b1 == 0)
                {
                    break;
                }

                if (b1 != 0 || !IsPrintableAscii(b0))
                {
                    chars.Clear();
                    break;
                }

                chars.Add((char)b0);
                cursor += 2;
            }

            if (chars.Count >= 4 && cursor + 1 < data.Length && data[cursor] == 0 && data[cursor + 1] == 0)
            {
                var value = new string(chars.ToArray());
                if (LooksUseful(value))
                {
                    yield return value;
                }

                i = cursor + 1;
            }
        }
    }

    private static bool LooksUseful(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        return value.Any(ch => char.IsLetterOrDigit(ch));
    }

    private static bool IsPrintableAscii(byte value) => value is >= 32 and <= 126;
}
