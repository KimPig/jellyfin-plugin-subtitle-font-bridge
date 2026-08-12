namespace Jellyfin.Plugin.SubtitleFontBridge.Models;

/// <summary>
/// Maps an ASS family name to the font files that can satisfy it.
/// </summary>
public sealed record ResolvedFontFamilyDto(
    string RequestedFamily,
    IReadOnlyList<string> FontIds);
