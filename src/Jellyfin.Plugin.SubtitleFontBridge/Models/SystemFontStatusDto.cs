namespace Jellyfin.Plugin.SubtitleFontBridge.Models;

/// <summary>
/// Describes the current system font catalog state.
/// </summary>
public sealed record SystemFontStatusDto(
    bool Available,
    string Platform,
    int FontFamilyCount,
    int CachedFamilyCount,
    int IndexedFileCount,
    string SkiaSharpVersion,
    bool SearchServerFonts,
    bool SearchAttachmentOptimizerCache,
    bool AttachmentOptimizerAvailable);
