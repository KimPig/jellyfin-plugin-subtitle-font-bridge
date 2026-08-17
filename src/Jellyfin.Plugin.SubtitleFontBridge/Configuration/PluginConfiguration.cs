using MediaBrowser.Model.Plugins;

namespace Jellyfin.Plugin.SubtitleFontBridge.Configuration;

/// <summary>
/// Subtitle font source settings.
/// </summary>
public sealed class PluginConfiguration : BasePluginConfiguration
{
    /// <summary>
    /// Gets or sets a value indicating whether fonts installed in the server operating system are used.
    /// </summary>
    public bool SearchServerFonts { get; set; } = true;

    /// <summary>
    /// Gets or sets a value indicating whether fonts cached by Attachment Optimizer are used.
    /// </summary>
    public bool SearchAttachmentOptimizerCache { get; set; } = true;
}
