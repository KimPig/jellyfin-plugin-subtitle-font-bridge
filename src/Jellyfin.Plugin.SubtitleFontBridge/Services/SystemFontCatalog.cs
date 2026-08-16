using System.Buffers;
using System.Collections.Concurrent;
using System.Globalization;
using System.Diagnostics.CodeAnalysis;
using System.Security.Cryptography;
using Jellyfin.Plugin.SubtitleFontBridge.Models;
using Microsoft.Extensions.Logging;
using SkiaSharp;

namespace Jellyfin.Plugin.SubtitleFontBridge.Services;

/// <summary>
/// Lazily indexes fonts exposed by Skia's platform font manager.
/// </summary>
public sealed class SystemFontCatalog : ISystemFontCatalog, IDisposable
{
    private const int MaximumFamiliesPerRequest = 32;
    private const int MaximumFamilyNameLength = 256;

    private readonly ILogger<SystemFontCatalog> _logger;
    private readonly SKFontManager _fontManager;
    private readonly Lazy<OpenTypeFontIndex> _openTypeIndex;
    private readonly bool _includePlatformFallback;
    private readonly ConcurrentDictionary<string, Lazy<CachedFamily>> _families =
        new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, Lazy<CachedOpenTypeFile>> _openTypeFiles =
        new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, FontSource> _sources =
        new(StringComparer.OrdinalIgnoreCase);
    private bool _disposed;

    /// <summary>
    /// Initializes a new instance of the <see cref="SystemFontCatalog"/> class.
    /// </summary>
    public SystemFontCatalog(ILogger<SystemFontCatalog> logger)
        : this(logger, OpenTypeFontIndex.GetDefaultFontDirectories(), includePlatformFallback: true)
    {
    }

    internal SystemFontCatalog(
        ILogger<SystemFontCatalog> logger,
        IEnumerable<string> fontDirectories,
        bool includePlatformFallback)
    {
        _logger = logger;
        _fontManager = SKFontManager.CreateDefault();
        _includePlatformFallback = includePlatformFallback;
        var directories = fontDirectories
            .Where(static path => !string.IsNullOrWhiteSpace(path))
            .Select(Path.GetFullPath)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        _openTypeIndex = new Lazy<OpenTypeFontIndex>(
            () => OpenTypeFontIndex.Build(directories, _logger),
            LazyThreadSafetyMode.ExecutionAndPublication);
        _logger.LogInformation(
            "Subtitle Font Bridge initialized with {FamilyCount} platform font families and {DirectoryCount} font directories",
            _fontManager.FontFamilyCount,
            directories.Length);
    }

    /// <inheritdoc />
    public int FontFamilyCount => _fontManager.FontFamilyCount;

    /// <inheritdoc />
    public int CachedFamilyCount => _families.Count;

    /// <inheritdoc />
    public int IndexedFileCount => _sources.Values
        .Select(static source => source.Id)
        .Distinct(StringComparer.OrdinalIgnoreCase)
        .Count();

    /// <inheritdoc />
    public SystemFontResolutionDto ResolveFamilies(IEnumerable<string> families)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentNullException.ThrowIfNull(families);

        var requested = NormalizeFamilies(families);
        var resolvedFamilies = new List<ResolvedFontFamilyDto>(requested.Count);
        var missingFamilies = new List<string>();
        var files = new Dictionary<string, FontFileBuilder>(StringComparer.OrdinalIgnoreCase);

        foreach (var family in requested)
        {
            var cached = _families.GetOrAdd(
                family,
                key => new Lazy<CachedFamily>(
                    () => BuildFamily(key),
                    LazyThreadSafetyMode.ExecutionAndPublication)).Value;

            if (cached.Files.Count == 0)
            {
                // Do not retain arbitrary misses supplied by authenticated clients.
                // Installed families are cheap to retry and this keeps the cache bounded
                // by family names that actually resolve to a platform font.
                _families.TryRemove(family, out _);
                missingFamilies.Add(family);
                continue;
            }

            var fontIds = new List<string>(cached.Files.Count);
            foreach (var file in cached.Files)
            {
                fontIds.Add(file.Id);
                if (!files.TryGetValue(file.Id, out var builder))
                {
                    builder = new FontFileBuilder(file);
                    files.Add(file.Id, builder);
                }
                else
                {
                    builder.AddFaces(file.Faces);
                }
            }

            resolvedFamilies.Add(new ResolvedFontFamilyDto(family, fontIds));
        }

        return new SystemFontResolutionDto(
            requested,
            resolvedFamilies,
            missingFamilies,
            files.Values.Select(static builder => builder.Build()).ToArray());
    }

    /// <inheritdoc />
    public bool TryOpenFont(
        string fontId,
        [NotNullWhen(true)] out SystemFontResource? resource)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        resource = null;

        if (!IsValidFontId(fontId))
        {
            return false;
        }

        var candidates = _sources
            .Where(pair => pair.Key.StartsWith(fontId + ":", StringComparison.OrdinalIgnoreCase))
            .Select(static pair => pair.Value)
            .ToArray();
        foreach (var candidate in candidates)
        {
            if (candidate.FilePath is not null)
            {
                try
                {
                    var fileStream = new FileStream(
                        candidate.FilePath,
                        FileMode.Open,
                        FileAccess.Read,
                        FileShare.ReadWrite | FileShare.Delete);
                    resource = new SystemFontResource(
                        fileStream,
                        candidate.Id,
                        candidate.FileName,
                        candidate.ContentType,
                        candidate.Size);
                    return true;
                }
                catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
                {
                    _logger.LogWarning(
                        exception,
                        "Unable to open indexed OpenType font {FontId}",
                        fontId);
                    continue;
                }
            }

            SKFontStyleSet? styleSet = null;
            SKTypeface? typeface = null;
            SKStreamAsset? stream = null;
            try
            {
                styleSet = _fontManager.GetFontStyles(candidate.Family);
                if (candidate.StyleIndex >= styleSet.Count)
                {
                    continue;
                }

                typeface = styleSet.CreateTypeface(candidate.StyleIndex);
                stream = typeface?.OpenStream(out _);
                if (typeface is null || stream is null)
                {
                    continue;
                }

                resource = new SystemFontResource(
                    new SkiaFontStream(styleSet, typeface, stream),
                    candidate.Id,
                    candidate.FileName,
                    candidate.ContentType,
                    candidate.Size);

                styleSet = null;
                typeface = null;
                stream = null;
                return true;
            }
            catch (Exception exception) when (exception is InvalidOperationException or IOException)
            {
                _logger.LogWarning(
                    exception,
                    "Unable to open candidate for indexed system font {FontId}",
                    fontId);
            }
            finally
            {
                stream?.Dispose();
                typeface?.Dispose();
                styleSet?.Dispose();
            }
        }

        return false;
    }

    /// <inheritdoc />
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _fontManager.Dispose();
        _disposed = true;
        GC.SuppressFinalize(this);
    }

    private static IReadOnlyList<string> NormalizeFamilies(IEnumerable<string> families)
    {
        var result = new List<string>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var value in families)
        {
            if (value is null)
            {
                continue;
            }

            var family = OpenTypeFontIndex.NormalizeFamilyName(value);
            if (family.Length == 0)
            {
                continue;
            }

            if (family.Length > MaximumFamilyNameLength)
            {
                throw new ArgumentException(
                    $"Font family names cannot exceed {MaximumFamilyNameLength} characters.",
                    nameof(families));
            }

            if (seen.Add(family))
            {
                result.Add(family);
                if (result.Count > MaximumFamiliesPerRequest)
                {
                    throw new ArgumentException(
                        $"No more than {MaximumFamiliesPerRequest} font families can be resolved at once.",
                        nameof(families));
                }
            }
        }

        return result;
    }

    private CachedFamily BuildFamily(string family)
    {
        try
        {
            var openTypeFiles = BuildOpenTypeFamily(family);
            if (openTypeFiles.Count > 0 || !_includePlatformFallback)
            {
                _logger.LogDebug(
                    "Resolved OpenType font alias {Family} to {FileCount} unique files",
                    family,
                    openTypeFiles.Count);
                return new CachedFamily(openTypeFiles);
            }

            using var styleSet = _fontManager.GetFontStyles(family);
            if (styleSet.Count == 0)
            {
                return new CachedFamily([]);
            }

            var uniqueFaces = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var files = new Dictionary<string, FontFileBuilder>(StringComparer.OrdinalIgnoreCase);

            for (var styleIndex = 0; styleIndex < styleSet.Count; styleIndex++)
            {
                using var style = styleSet[styleIndex];
                using var typeface = styleSet.CreateTypeface(styleIndex);
                if (typeface is null)
                {
                    continue;
                }

                var postScriptName = typeface.PostScriptName;
                var faceIdentity = string.IsNullOrWhiteSpace(postScriptName)
                    ? string.Create(
                        CultureInfo.InvariantCulture,
                        $"{typeface.FamilyName}|{style.Weight}|{style.Width}|{style.Slant}")
                    : postScriptName;
                if (!uniqueFaces.Add(faceIdentity))
                {
                    continue;
                }

                using var stream = typeface.OpenStream(out var collectionIndex);
                if (stream is null)
                {
                    continue;
                }

                var inspected = InspectStream(stream);

                var fileName = inspected.Id + "." + inspected.Extension;
                var face = new FontFaceDto(
                    typeface.FamilyName,
                    styleSet.GetStyleName(styleIndex),
                    postScriptName,
                    style.Weight,
                    style.Width,
                    style.Slant.ToString(),
                    collectionIndex);
                var source = new FontSource(
                    inspected.Id,
                    family,
                    styleIndex,
                    fileName,
                    inspected.ContentType,
                    inspected.Size);

                _sources.TryAdd(CreateSourceKey(inspected.Id, source), source);
                if (!files.TryGetValue(inspected.Id, out var builder))
                {
                    builder = new FontFileBuilder(
                        inspected.Id,
                        fileName,
                        Plugin.ApiRoute + "/Files/" + fileName,
                        inspected.ContentType,
                        inspected.Size);
                    files.Add(inspected.Id, builder);
                }

                builder.AddFace(face);
            }

            _logger.LogDebug(
                "Resolved system font family {Family} to {FileCount} unique files",
                family,
                files.Count);
            return new CachedFamily(files.Values.Select(static builder => builder.Build()).ToArray());
        }
        catch (Exception exception) when (exception is InvalidOperationException
                                           or IOException
                                           or UnauthorizedAccessException)
        {
            _logger.LogWarning(exception, "Unable to resolve system font family {Family}", family);
            return new CachedFamily([]);
        }
    }

    private IReadOnlyList<SystemFontFileDto> BuildOpenTypeFamily(string family)
    {
        var files = new Dictionary<string, FontFileBuilder>(StringComparer.OrdinalIgnoreCase);
        foreach (var indexedFont in _openTypeIndex.Value.Find(family))
        {
            var cached = _openTypeFiles.GetOrAdd(
                indexedFont.Path,
                _ => new Lazy<CachedOpenTypeFile>(
                    () => BuildOpenTypeFile(indexedFont),
                    LazyThreadSafetyMode.ExecutionAndPublication)).Value;

            _sources.TryAdd(CreateSourceKey(cached.Source.Id, cached.Source), cached.Source);
            if (!files.TryGetValue(cached.File.Id, out var builder))
            {
                builder = new FontFileBuilder(cached.File);
                files.Add(cached.File.Id, builder);
            }
            else
            {
                builder.AddFaces(cached.File.Faces);
            }
        }

        return files.Values.Select(static builder => builder.Build()).ToArray();
    }

    private CachedOpenTypeFile BuildOpenTypeFile(OpenTypeFontIndex.IndexedOpenTypeFont indexedFont)
    {
        using var stream = new FileStream(
            indexedFont.Path,
            FileMode.Open,
            FileAccess.Read,
            FileShare.ReadWrite | FileShare.Delete);
        var inspected = InspectStream(stream);
        var fileName = inspected.Id + "." + inspected.Extension;
        var faces = indexedFont.Faces.Select(static face => new FontFaceDto(
            face.FamilyNames.FirstOrDefault() ?? string.Empty,
            string.Empty,
            null,
            400,
            5,
            "Upright",
            face.CollectionIndex)).ToArray();
        var file = new SystemFontFileDto(
            inspected.Id,
            fileName,
            Plugin.ApiRoute + "/Files/" + fileName,
            inspected.ContentType,
            inspected.Size,
            faces);
        var source = new FontSource(
            inspected.Id,
            string.Empty,
            -1,
            fileName,
            inspected.ContentType,
            inspected.Size,
            indexedFont.Path);
        return new CachedOpenTypeFile(file, source);
    }

    private static InspectedStream InspectStream(SKStreamAsset stream)
    {
        stream.Rewind();
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        var buffer = ArrayPool<byte>.Shared.Rent(81920);
        var signature = new byte[4];
        long size = 0;

        try
        {
            while (true)
            {
                var read = stream.Read(buffer, buffer.Length);
                if (read == 0)
                {
                    break;
                }

                if (size < signature.Length)
                {
                    var signatureBytes = Math.Min(read, signature.Length - checked((int)size));
                    buffer.AsSpan(0, signatureBytes).CopyTo(signature.AsSpan(checked((int)size)));
                }

                hash.AppendData(buffer, 0, read);
                size += read;
            }
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(buffer);
        }

        var id = Convert.ToHexStringLower(hash.GetHashAndReset());
        var (extension, contentType) = DetectFormat(signature);
        return new InspectedStream(id, extension, contentType, size);
    }

    private static InspectedStream InspectStream(Stream stream)
    {
        stream.Position = 0;
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        var buffer = ArrayPool<byte>.Shared.Rent(81920);
        var signature = new byte[4];
        long size = 0;

        try
        {
            while (true)
            {
                var read = stream.Read(buffer, 0, buffer.Length);
                if (read == 0)
                {
                    break;
                }

                if (size < signature.Length)
                {
                    var signatureBytes = Math.Min(read, signature.Length - checked((int)size));
                    buffer.AsSpan(0, signatureBytes).CopyTo(signature.AsSpan(checked((int)size)));
                }

                hash.AppendData(buffer, 0, read);
                size += read;
            }
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(buffer);
        }

        var id = Convert.ToHexStringLower(hash.GetHashAndReset());
        var (extension, contentType) = DetectFormat(signature);
        return new InspectedStream(id, extension, contentType, size);
    }

    private static (string Extension, string ContentType) DetectFormat(ReadOnlySpan<byte> signature)
    {
        if (signature.SequenceEqual("OTTO"u8))
        {
            return ("otf", "font/otf");
        }

        if (signature.SequenceEqual("ttcf"u8))
        {
            return ("ttc", "font/collection");
        }

        if (signature.SequenceEqual("wOFF"u8))
        {
            return ("woff", "font/woff");
        }

        if (signature.SequenceEqual("wOF2"u8))
        {
            return ("woff2", "font/woff2");
        }

        if ((signature[0] == 0x00
             && signature[1] == 0x01
             && signature[2] == 0x00
             && signature[3] == 0x00)
            || signature.SequenceEqual("true"u8)
            || signature.SequenceEqual("typ1"u8))
        {
            return ("ttf", "font/ttf");
        }

        return ("font", "application/octet-stream");
    }

    private static bool IsValidFontId(string fontId) =>
        fontId.Length == 64 && fontId.All(static character =>
            character is >= '0' and <= '9'
            or >= 'a' and <= 'f'
            or >= 'A' and <= 'F');

    private static string CreateSourceKey(string fontId, FontSource source) =>
        source.FilePath is null
            ? string.Create(
                CultureInfo.InvariantCulture,
                $"{fontId}:skia:{source.Family}:{source.StyleIndex}")
            : fontId + ":file:" + source.FilePath;

    private sealed record CachedFamily(IReadOnlyList<SystemFontFileDto> Files);

    private sealed record CachedOpenTypeFile(SystemFontFileDto File, FontSource Source);

    private sealed record FontSource(
        string Id,
        string Family,
        int StyleIndex,
        string FileName,
        string ContentType,
        long Size,
        string? FilePath = null);

    private sealed record InspectedStream(
        string Id,
        string Extension,
        string ContentType,
        long Size);

    private sealed class FontFileBuilder
    {
        private readonly List<FontFaceDto> _faces = [];
        private readonly HashSet<string> _faceKeys = new(StringComparer.OrdinalIgnoreCase);

        public FontFileBuilder(SystemFontFileDto file)
            : this(file.Id, file.FileName, file.Path, file.ContentType, file.Size)
        {
            AddFaces(file.Faces);
        }

        public FontFileBuilder(
            string id,
            string fileName,
            string path,
            string contentType,
            long size)
        {
            Id = id;
            FileName = fileName;
            Path = path;
            ContentType = contentType;
            Size = size;
        }

        public string Id { get; }

        public string FileName { get; }

        public string Path { get; }

        public string ContentType { get; }

        public long Size { get; }

        public void AddFaces(IEnumerable<FontFaceDto> faces)
        {
            foreach (var face in faces)
            {
                AddFace(face);
            }
        }

        public void AddFace(FontFaceDto face)
        {
            var key = string.Create(
                CultureInfo.InvariantCulture,
                $"{face.Family}|{face.PostScriptName}|{face.CollectionIndex}|{face.Weight}|{face.Width}|{face.Slant}");
            if (_faceKeys.Add(key))
            {
                _faces.Add(face);
            }
        }

        public SystemFontFileDto Build() =>
            new(Id, FileName, Path, ContentType, Size, _faces.ToArray());
    }
}
