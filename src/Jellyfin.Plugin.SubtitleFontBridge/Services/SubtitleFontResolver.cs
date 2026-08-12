using Jellyfin.Plugin.SubtitleFontBridge.Models;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.MediaEncoding;

namespace Jellyfin.Plugin.SubtitleFontBridge.Services;

/// <summary>
/// Resolves system fonts from an ASS subtitle stream supplied by Jellyfin.
/// </summary>
public sealed class SubtitleFontResolver : ISubtitleFontResolver
{
    private readonly ISubtitleEncoder _subtitleEncoder;
    private readonly IAssFontParser _assFontParser;
    private readonly ISystemFontCatalog _fontCatalog;

    /// <summary>
    /// Initializes a new instance of the <see cref="SubtitleFontResolver"/> class.
    /// </summary>
    public SubtitleFontResolver(
        ISubtitleEncoder subtitleEncoder,
        IAssFontParser assFontParser,
        ISystemFontCatalog fontCatalog)
    {
        _subtitleEncoder = subtitleEncoder;
        _assFontParser = assFontParser;
        _fontCatalog = fontCatalog;
    }

    /// <inheritdoc />
    public async Task<SystemFontResolutionDto> ResolveAsync(
        BaseItem item,
        string mediaSourceId,
        int subtitleIndex,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(item);
        ArgumentException.ThrowIfNullOrWhiteSpace(mediaSourceId);

        await using var subtitleStream = await _subtitleEncoder.GetSubtitles(
            item,
            mediaSourceId,
            subtitleIndex,
            "ass",
            0,
            0,
            false,
            cancellationToken).ConfigureAwait(false);

        var families = await _assFontParser
            .ExtractFamiliesAsync(subtitleStream, cancellationToken)
            .ConfigureAwait(false);

        return _fontCatalog.ResolveFamilies(families);
    }
}
