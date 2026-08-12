namespace Jellyfin.Plugin.SubtitleFontBridge.Services;

/// <summary>
/// Extracts font family references from ASS/SSA subtitle scripts.
/// </summary>
public interface IAssFontParser
{
    /// <summary>
    /// Extracts style and inline override font family names.
    /// </summary>
    /// <param name="subtitleStream">The ASS/SSA subtitle stream.</param>
    /// <param name="cancellationToken">A cancellation token.</param>
    /// <returns>The distinct family names in first-seen order.</returns>
    Task<IReadOnlyList<string>> ExtractFamiliesAsync(
        Stream subtitleStream,
        CancellationToken cancellationToken);
}
