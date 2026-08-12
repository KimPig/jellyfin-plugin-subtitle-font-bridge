using Jellyfin.Plugin.SubtitleFontBridge.Models;
using MediaBrowser.Controller.Entities;

namespace Jellyfin.Plugin.SubtitleFontBridge.Services;

/// <summary>
/// Resolves fonts referenced by one Jellyfin subtitle stream.
/// </summary>
public interface ISubtitleFontResolver
{
    /// <summary>
    /// Extracts an ASS stream and resolves its font families.
    /// </summary>
    Task<SystemFontResolutionDto> ResolveAsync(
        BaseItem item,
        string mediaSourceId,
        int subtitleIndex,
        CancellationToken cancellationToken);
}
