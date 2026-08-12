using System.Buffers;
using System.Text;
using System.Text.RegularExpressions;

namespace Jellyfin.Plugin.SubtitleFontBridge.Services;

/// <summary>
/// A deliberately small ASS parser that only extracts font family names.
/// </summary>
public sealed partial class AssFontParser : IAssFontParser
{
    internal const int MaximumSubtitleBytes = 8 * 1024 * 1024;

    /// <inheritdoc />
    public async Task<IReadOnlyList<string>> ExtractFamiliesAsync(
        Stream subtitleStream,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(subtitleStream);

        if (subtitleStream.CanSeek && subtitleStream.Length > MaximumSubtitleBytes)
        {
            throw new InvalidDataException(
                $"The subtitle is larger than the {MaximumSubtitleBytes}-byte analysis limit.");
        }

        using var memory = new MemoryStream();
        var buffer = ArrayPool<byte>.Shared.Rent(81920);
        try
        {
            while (true)
            {
                var read = await subtitleStream
                    .ReadAsync(buffer.AsMemory(0, buffer.Length), cancellationToken)
                    .ConfigureAwait(false);
                if (read == 0)
                {
                    break;
                }

                if (memory.Length + read > MaximumSubtitleBytes)
                {
                    throw new InvalidDataException(
                        $"The subtitle is larger than the {MaximumSubtitleBytes}-byte analysis limit.");
                }

                await memory.WriteAsync(buffer.AsMemory(0, read), cancellationToken)
                    .ConfigureAwait(false);
            }
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(buffer);
        }

        return ExtractFamilies(Decode(memory.GetBuffer().AsSpan(0, checked((int)memory.Length))));
    }

    internal static IReadOnlyList<string> ExtractFamilies(string subtitle)
    {
        ArgumentNullException.ThrowIfNull(subtitle);

        var families = new List<string>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var inStylesSection = false;
        var fontNameIndex = 1;

        foreach (var rawLine in subtitle.Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n'))
        {
            var line = rawLine.Trim();
            if (line.Length == 0 || line[0] is ';')
            {
                continue;
            }

            if (line[0] == '[' && line[^1] == ']')
            {
                inStylesSection =
                    line.Equals("[V4+ Styles]", StringComparison.OrdinalIgnoreCase)
                    || line.Equals("[V4 Styles]", StringComparison.OrdinalIgnoreCase);
                if (inStylesSection)
                {
                    fontNameIndex = 1;
                }

                continue;
            }

            if (inStylesSection && line.StartsWith("Format:", StringComparison.OrdinalIgnoreCase))
            {
                var columns = line[7..].Split(',');
                for (var index = 0; index < columns.Length; index++)
                {
                    if (columns[index].Trim().Equals("Fontname", StringComparison.OrdinalIgnoreCase))
                    {
                        fontNameIndex = index;
                        break;
                    }
                }

                continue;
            }

            if (inStylesSection && line.StartsWith("Style:", StringComparison.OrdinalIgnoreCase))
            {
                var values = line[6..].Split(',');
                if (fontNameIndex < values.Length)
                {
                    AddFamily(values[fontNameIndex], families, seen);
                }
            }
        }

        foreach (Match overrideMatch in OverrideBlockRegex().Matches(subtitle))
        {
            var tags = overrideMatch.Groups["tags"].Value;
            foreach (Match fontMatch in FontOverrideRegex().Matches(tags))
            {
                AddFamily(fontMatch.Groups["name"].Value, families, seen);
            }
        }

        return families;
    }

    private static string Decode(ReadOnlySpan<byte> bytes)
    {
        Encoding encoding;
        var offset = 0;

        if (bytes.Length >= 4
            && bytes[0] == 0x00
            && bytes[1] == 0x00
            && bytes[2] == 0xFE
            && bytes[3] == 0xFF)
        {
            encoding = new UTF32Encoding(bigEndian: true, byteOrderMark: true, throwOnInvalidCharacters: false);
            offset = 4;
        }
        else if (bytes.Length >= 4
                 && bytes[0] == 0xFF
                 && bytes[1] == 0xFE
                 && bytes[2] == 0x00
                 && bytes[3] == 0x00)
        {
            encoding = new UTF32Encoding(bigEndian: false, byteOrderMark: true, throwOnInvalidCharacters: false);
            offset = 4;
        }
        else if (bytes.Length >= 3
                 && bytes[0] == 0xEF
                 && bytes[1] == 0xBB
                 && bytes[2] == 0xBF)
        {
            encoding = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: false);
            offset = 3;
        }
        else if (bytes.Length >= 2 && bytes[0] == 0xFE && bytes[1] == 0xFF)
        {
            encoding = Encoding.BigEndianUnicode;
            offset = 2;
        }
        else if (bytes.Length >= 2 && bytes[0] == 0xFF && bytes[1] == 0xFE)
        {
            encoding = Encoding.Unicode;
            offset = 2;
        }
        else
        {
            encoding = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: false);
        }

        return encoding.GetString(bytes[offset..]);
    }

    private static void AddFamily(
        string value,
        ICollection<string> families,
        ISet<string> seen)
    {
        var family = value.Trim().TrimStart('@').Trim();
        if (family.Length == 0 || family.Length > 256 || !seen.Add(family))
        {
            return;
        }

        families.Add(family);
    }

    [GeneratedRegex(@"\{(?<tags>[^}]*)\}", RegexOptions.CultureInvariant)]
    private static partial Regex OverrideBlockRegex();

    [GeneratedRegex(@"\\fn(?<name>[^\\}]*)", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex FontOverrideRegex();
}
