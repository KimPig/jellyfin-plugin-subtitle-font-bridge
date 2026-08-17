using System.Diagnostics.CodeAnalysis;
using Jellyfin.Plugin.SubtitleFontBridge.Models;

namespace Jellyfin.Plugin.SubtitleFontBridge.Services;

/// <summary>
/// Resolves and opens fonts visible to the Jellyfin server process.
/// </summary>
public interface ISystemFontCatalog
{
    /// <summary>
    /// Gets the number of families reported by the platform font manager.
    /// </summary>
    int FontFamilyCount { get; }

    /// <summary>
    /// Gets the number of family names resolved during this server session.
    /// </summary>
    int CachedFamilyCount { get; }

    /// <summary>
    /// Gets the number of unique font files indexed during this server session.
    /// </summary>
    int IndexedFileCount { get; }

    /// <summary>
    /// Gets a value indicating whether server operating-system fonts are enabled.
    /// </summary>
    bool SearchServerFonts { get; }

    /// <summary>
    /// Gets a value indicating whether the Attachment Optimizer cache is enabled in settings.
    /// </summary>
    bool SearchAttachmentOptimizerCache { get; }

    /// <summary>
    /// Gets a value indicating whether Attachment Optimizer is installed, enabled, and supported.
    /// </summary>
    bool AttachmentOptimizerAvailable { get; }

    /// <summary>
    /// Resolves a bounded collection of font family names.
    /// </summary>
    SystemFontResolutionDto ResolveFamilies(IEnumerable<string> families);

    /// <summary>
    /// Opens an indexed font file by opaque content identifier.
    /// </summary>
    bool TryOpenFont(
        string fontId,
        [NotNullWhen(true)] out SystemFontResource? resource);
}
