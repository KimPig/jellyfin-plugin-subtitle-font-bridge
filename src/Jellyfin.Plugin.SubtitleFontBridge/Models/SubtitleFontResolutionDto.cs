namespace Jellyfin.Plugin.SubtitleFontBridge.Models;

/// <summary>
/// Contains the fonts resolved from one Jellyfin subtitle stream.
/// </summary>
public sealed record SubtitleFontResolutionDto(
    Guid ItemId,
    string MediaSourceId,
    int SubtitleIndex,
    SystemFontResolutionDto Resolution);
