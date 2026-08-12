namespace Jellyfin.Plugin.SubtitleFontBridge.Models;

/// <summary>
/// Describes an opaque, authenticated font resource.
/// </summary>
public sealed record SystemFontFileDto(
    string Id,
    string FileName,
    string Path,
    string ContentType,
    long Size,
    IReadOnlyList<FontFaceDto> Faces);
