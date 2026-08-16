using Jellyfin.Plugin.SubtitleFontBridge.Models;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.MediaEncoding;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.SubtitleFontBridge.Services;

/// <summary>
/// Resolves system fonts from an ASS subtitle stream supplied by Jellyfin.
/// </summary>
public sealed class SubtitleFontResolver : ISubtitleFontResolver
{
    private readonly ISubtitleEncoder _subtitleEncoder;
    private readonly IAssFontParser _assFontParser;
    private readonly ISystemFontCatalog _fontCatalog;
    private readonly ILogger<SubtitleFontResolver> _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="SubtitleFontResolver"/> class.
    /// </summary>
    public SubtitleFontResolver(
        ISubtitleEncoder subtitleEncoder,
        IAssFontParser assFontParser,
        ISystemFontCatalog fontCatalog,
        ILogger<SubtitleFontResolver> logger)
    {
        _subtitleEncoder = subtitleEncoder;
        _assFontParser = assFontParser;
        _fontCatalog = fontCatalog;
        _logger = logger;
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

        var resolution = _fontCatalog.ResolveFamilies(families);
        _logger.LogInformation(
            "Resolved ASS fonts for item {ItemId}, media source {MediaSourceId}, subtitle {SubtitleIndex}: requested [{RequestedFamilies}], missing [{MissingFamilies}], files [{FontFiles}]",
            item.Id,
            mediaSourceId,
            subtitleIndex,
            string.Join(", ", resolution.RequestedFamilies),
            string.Join(", ", resolution.MissingFamilies),
            string.Join(", ", resolution.Files.Select(static file => file.FileName)));

        return resolution;
    }
}
