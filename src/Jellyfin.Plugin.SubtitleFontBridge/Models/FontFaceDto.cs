namespace Jellyfin.Plugin.SubtitleFontBridge.Models;

/// <summary>
/// Describes one face contained in a streamed font file.
/// </summary>
public sealed record FontFaceDto(
    string Family,
    string Style,
    string? PostScriptName,
    int Weight,
    int Width,
    string Slant,
    int CollectionIndex);
