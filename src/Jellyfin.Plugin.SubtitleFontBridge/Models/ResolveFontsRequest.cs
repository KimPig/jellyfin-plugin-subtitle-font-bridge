using System.ComponentModel.DataAnnotations;

namespace Jellyfin.Plugin.SubtitleFontBridge.Models;

/// <summary>
/// A request to resolve explicit font family names.
/// </summary>
public sealed class ResolveFontsRequest
{
    /// <summary>
    /// Gets or sets the requested font family names.
    /// </summary>
    [Required]
    [MinLength(1)]
    [MaxLength(32)]
    public IReadOnlyList<string> Families { get; set; } = Array.Empty<string>();
}
