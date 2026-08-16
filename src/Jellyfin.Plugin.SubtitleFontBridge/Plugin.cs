using Jellyfin.Plugin.SubtitleFontBridge.Configuration;
using MediaBrowser.Common.Configuration;
using MediaBrowser.Common.Plugins;
using MediaBrowser.Model.Serialization;

namespace Jellyfin.Plugin.SubtitleFontBridge;

/// <summary>
/// The Subtitle Font Bridge plugin.
/// </summary>
public sealed class Plugin : BasePlugin<PluginConfiguration>
{
    /// <summary>
    /// The common prefix for every plugin API route.
    /// </summary>
    public const string ApiRoute = "SubtitleFontBridge";

    /// <summary>
    /// The stable plugin identifier shared with build.yaml.
    /// </summary>
    public static readonly Guid PluginId = Guid.Parse("81ea0bd3-d8e0-4f4a-b680-bf8b83a673f7");

    /// <summary>
    /// Initializes a new instance of the <see cref="Plugin"/> class.
    /// </summary>
    /// <param name="applicationPaths">The Jellyfin application paths.</param>
    /// <param name="xmlSerializer">The Jellyfin XML serializer.</param>
    public Plugin(IApplicationPaths applicationPaths, IXmlSerializer xmlSerializer)
        : base(applicationPaths, xmlSerializer)
    {
        Instance = this;
    }

    /// <inheritdoc />
    public override string Name => "Subtitle Font Bridge";

    /// <inheritdoc />
    public override string Description =>
        "Supplies Jellyfin Web with server-hosted fonts referenced by ASS/SSA subtitles.";

    /// <inheritdoc />
    public override Guid Id => PluginId;

    /// <summary>
    /// Gets the loaded plugin instance.
    /// </summary>
    public static Plugin? Instance { get; private set; }
}
