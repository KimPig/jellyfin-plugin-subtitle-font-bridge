namespace Jellyfin.Plugin.SubtitleFontBridge.Services;

/// <summary>
/// An open system font resource owned by the HTTP response.
/// </summary>
public sealed record SystemFontResource(
    Stream Stream,
    string Id,
    string FileName,
    string ContentType,
    long Size);
