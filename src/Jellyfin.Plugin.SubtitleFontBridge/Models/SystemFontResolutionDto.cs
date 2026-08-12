namespace Jellyfin.Plugin.SubtitleFontBridge.Models;

/// <summary>
/// Contains the files needed for a set of requested font families.
/// </summary>
public sealed record SystemFontResolutionDto(
    IReadOnlyList<string> RequestedFamilies,
    IReadOnlyList<ResolvedFontFamilyDto> Families,
    IReadOnlyList<string> MissingFamilies,
    IReadOnlyList<SystemFontFileDto> Files);
