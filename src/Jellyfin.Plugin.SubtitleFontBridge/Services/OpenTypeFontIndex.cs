using System.Buffers.Binary;
using System.Text;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.SubtitleFontBridge.Services;

/// <summary>
/// Indexes the multilingual family aliases stored in OpenType name tables.
/// </summary>
internal sealed class OpenTypeFontIndex
{
    private const uint TrueTypeCollectionTag = 0x74746366; // ttcf
    private const uint NameTableTag = 0x6E616D65; // name
    private static readonly string[] SupportedExtensions = [".ttf", ".otf", ".ttc", ".otc"];

    private readonly IReadOnlyDictionary<string, IReadOnlyList<IndexedOpenTypeFont>> _fontsByFamily;

    private OpenTypeFontIndex(
        IReadOnlyDictionary<string, IReadOnlyList<IndexedOpenTypeFont>> fontsByFamily,
        int fileCount)
    {
        _fontsByFamily = fontsByFamily;
        FileCount = fileCount;
    }

    public int FamilyCount => _fontsByFamily.Count;

    public int FileCount { get; }

    public IReadOnlyList<IndexedOpenTypeFont> Find(string family) =>
        _fontsByFamily.TryGetValue(NormalizeFamilyName(family), out var fonts) ? fonts : [];

    public static OpenTypeFontIndex Build(
        IEnumerable<string> directories,
        ILogger logger)
    {
        ArgumentNullException.ThrowIfNull(directories);
        ArgumentNullException.ThrowIfNull(logger);

        var fontsByFamily = new Dictionary<string, Dictionary<string, IndexedOpenTypeFont>>(
            StringComparer.OrdinalIgnoreCase);
        var indexedPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var path in EnumerateFontFiles(directories))
        {
            try
            {
                var font = ReadFont(path);
                var aliases = font.Faces
                    .SelectMany(static face => face.FamilyNames)
                    .Select(NormalizeFamilyName)
                    .Where(static name => name.Length > 0)
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToArray();
                if (aliases.Length == 0)
                {
                    continue;
                }

                indexedPaths.Add(path);
                foreach (var alias in aliases)
                {
                    if (!fontsByFamily.TryGetValue(alias, out var files))
                    {
                        files = new Dictionary<string, IndexedOpenTypeFont>(StringComparer.OrdinalIgnoreCase);
                        fontsByFamily.Add(alias, files);
                    }

                    files.TryAdd(path, font);
                }
            }
            catch (Exception exception) when (exception is IOException
                                               or UnauthorizedAccessException
                                               or InvalidDataException)
            {
                logger.LogDebug(exception, "Unable to index OpenType font file {FontFile}", path);
            }
        }

        var index = fontsByFamily.ToDictionary(
            static pair => pair.Key,
            static pair => (IReadOnlyList<IndexedOpenTypeFont>)pair.Value.Values
                .OrderBy(static font => font.Path, StringComparer.OrdinalIgnoreCase)
                .ToArray(),
            StringComparer.OrdinalIgnoreCase);
        logger.LogInformation(
            "Indexed {FontFileCount} OpenType font files with {FamilyAliasCount} family aliases",
            indexedPaths.Count,
            index.Count);
        return new OpenTypeFontIndex(index, indexedPaths.Count);
    }

    public static IReadOnlyList<string> GetDefaultFontDirectories()
    {
        var directories = new List<string>();

        if (OperatingSystem.IsWindows())
        {
            AddSpecialFolder(directories, Environment.SpecialFolder.Windows, "Fonts");
            AddSpecialFolder(
                directories,
                Environment.SpecialFolder.LocalApplicationData,
                "Microsoft",
                "Windows",
                "Fonts");
        }
        else if (OperatingSystem.IsLinux())
        {
            directories.Add("/usr/share/fonts");
            directories.Add("/usr/local/share/fonts");
            AddSpecialFolder(directories, Environment.SpecialFolder.UserProfile, ".local", "share", "fonts");
            AddSpecialFolder(directories, Environment.SpecialFolder.UserProfile, ".fonts");
        }
        else if (OperatingSystem.IsMacOS())
        {
            directories.Add("/System/Library/Fonts");
            directories.Add("/Library/Fonts");
            AddSpecialFolder(directories, Environment.SpecialFolder.UserProfile, "Library", "Fonts");
        }

        return directories
            .Where(Directory.Exists)
            .Select(Path.GetFullPath)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    public static string NormalizeFamilyName(string value)
    {
        var trimmed = value.Trim().TrimStart('@').Trim().Normalize(NormalizationForm.FormKC);
        var builder = new StringBuilder(trimmed.Length);
        var pendingSpace = false;
        foreach (var character in trimmed)
        {
            if (char.IsWhiteSpace(character))
            {
                pendingSpace = builder.Length > 0;
                continue;
            }

            if (pendingSpace)
            {
                builder.Append(' ');
                pendingSpace = false;
            }

            builder.Append(character);
        }

        return builder.ToString();
    }

    private static IEnumerable<string> EnumerateFontFiles(IEnumerable<string> directories)
    {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var options = new EnumerationOptions
        {
            RecurseSubdirectories = true,
            IgnoreInaccessible = true,
            AttributesToSkip = FileAttributes.ReparsePoint
        };

        foreach (var directory in directories
                     .Where(static path => !string.IsNullOrWhiteSpace(path))
                     .Select(Path.GetFullPath)
                     .Distinct(StringComparer.OrdinalIgnoreCase))
        {
            if (!Directory.Exists(directory))
            {
                continue;
            }

            IEnumerable<string> files;
            try
            {
                files = Directory.EnumerateFiles(directory, "*", options).ToArray();
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                continue;
            }

            foreach (var file in files)
            {
                if (SupportedExtensions.Contains(Path.GetExtension(file), StringComparer.OrdinalIgnoreCase)
                    && seen.Add(file))
                {
                    yield return file;
                }
            }
        }
    }

    private static IndexedOpenTypeFont ReadFont(string path)
    {
        using var stream = new FileStream(
            path,
            FileMode.Open,
            FileAccess.Read,
            FileShare.ReadWrite | FileShare.Delete);
        if (stream.Length < 12)
        {
            throw new InvalidDataException("Font file is too short.");
        }

        var firstTag = ReadUInt32(stream);
        IReadOnlyList<uint> faceOffsets;
        if (firstTag == TrueTypeCollectionTag)
        {
            _ = ReadUInt32(stream); // collection version
            var faceCount = ReadUInt32(stream);
            if (faceCount is 0 or > 4096 || stream.Position + (faceCount * 4L) > stream.Length)
            {
                throw new InvalidDataException("Invalid TrueType collection header.");
            }

            var offsets = new uint[faceCount];
            for (var index = 0; index < offsets.Length; index++)
            {
                offsets[index] = ReadUInt32(stream);
            }

            faceOffsets = offsets;
        }
        else
        {
            faceOffsets = [0];
        }

        var faces = new List<OpenTypeFace>(faceOffsets.Count);
        for (var faceIndex = 0; faceIndex < faceOffsets.Count; faceIndex++)
        {
            var familyNames = ReadFaceFamilyNames(stream, faceOffsets[faceIndex]);
            if (familyNames.Count > 0)
            {
                faces.Add(new OpenTypeFace(faceIndex, familyNames));
            }
        }

        return new IndexedOpenTypeFont(path, faces);
    }

    private static IReadOnlyList<string> ReadFaceFamilyNames(Stream stream, uint faceOffset)
    {
        if (faceOffset + 12L > stream.Length)
        {
            throw new InvalidDataException("Invalid font face offset.");
        }

        stream.Position = faceOffset + 4;
        var tableCount = ReadUInt16(stream);
        stream.Position += 6;
        if (tableCount > 4096 || stream.Position + (tableCount * 16L) > stream.Length)
        {
            throw new InvalidDataException("Invalid OpenType table directory.");
        }

        uint? nameTableOffset = null;
        uint? nameTableLength = null;
        for (var index = 0; index < tableCount; index++)
        {
            var tag = ReadUInt32(stream);
            _ = ReadUInt32(stream); // checksum
            var offset = ReadUInt32(stream);
            var length = ReadUInt32(stream);
            if (tag == NameTableTag)
            {
                nameTableOffset = offset;
                nameTableLength = length;
            }
        }

        if (nameTableOffset is null
            || nameTableLength is null
            || nameTableOffset.Value + (long)nameTableLength.Value > stream.Length
            || nameTableLength.Value < 6)
        {
            return [];
        }

        stream.Position = nameTableOffset.Value;
        _ = ReadUInt16(stream); // name table format
        var recordCount = ReadUInt16(stream);
        var stringStorageOffset = ReadUInt16(stream);
        if (recordCount > 16384 || 6L + (recordCount * 12L) > nameTableLength.Value)
        {
            throw new InvalidDataException("Invalid OpenType name table.");
        }

        var records = new List<NameRecord>(recordCount);
        for (var index = 0; index < recordCount; index++)
        {
            var platformId = ReadUInt16(stream);
            _ = ReadUInt16(stream); // encoding id
            _ = ReadUInt16(stream); // language id
            var nameId = ReadUInt16(stream);
            var length = ReadUInt16(stream);
            var offset = ReadUInt16(stream);
            if (nameId is 1 or 16)
            {
                records.Add(new NameRecord(platformId, length, offset));
            }
        }

        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var storageStart = nameTableOffset.Value + stringStorageOffset;
        var tableEnd = nameTableOffset.Value + (long)nameTableLength.Value;
        foreach (var record in records)
        {
            var valueOffset = storageStart + record.Offset;
            if (record.Length == 0 || valueOffset + record.Length > tableEnd)
            {
                continue;
            }

            stream.Position = valueOffset;
            var bytes = new byte[record.Length];
            stream.ReadExactly(bytes);
            var value = record.PlatformId is 0 or 3
                ? Encoding.BigEndianUnicode.GetString(bytes)
                : Encoding.Latin1.GetString(bytes);
            value = value.Trim('\0', ' ', '\t', '\r', '\n');
            if (value.Length > 0)
            {
                names.Add(value);
            }
        }

        return names.ToArray();
    }

    private static void AddSpecialFolder(
        ICollection<string> directories,
        Environment.SpecialFolder specialFolder,
        params string[] segments)
    {
        var root = Environment.GetFolderPath(specialFolder);
        if (!string.IsNullOrWhiteSpace(root))
        {
            directories.Add(Path.Combine([root, .. segments]));
        }
    }

    private static ushort ReadUInt16(Stream stream)
    {
        Span<byte> bytes = stackalloc byte[2];
        stream.ReadExactly(bytes);
        return BinaryPrimitives.ReadUInt16BigEndian(bytes);
    }

    private static uint ReadUInt32(Stream stream)
    {
        Span<byte> bytes = stackalloc byte[4];
        stream.ReadExactly(bytes);
        return BinaryPrimitives.ReadUInt32BigEndian(bytes);
    }

    internal sealed record IndexedOpenTypeFont(
        string Path,
        IReadOnlyList<OpenTypeFace> Faces);

    internal sealed record OpenTypeFace(
        int CollectionIndex,
        IReadOnlyList<string> FamilyNames);

    private sealed record NameRecord(
        ushort PlatformId,
        ushort Length,
        ushort Offset);
}
