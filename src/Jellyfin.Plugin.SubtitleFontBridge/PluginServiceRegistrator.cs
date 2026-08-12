using Jellyfin.Plugin.SubtitleFontBridge.Services;
using MediaBrowser.Controller;
using MediaBrowser.Controller.Plugins;
using Microsoft.Extensions.DependencyInjection;

namespace Jellyfin.Plugin.SubtitleFontBridge;

/// <summary>
/// Registers plugin services with Jellyfin's dependency injection container.
/// </summary>
public sealed class PluginServiceRegistrator : IPluginServiceRegistrator
{
    /// <inheritdoc />
    public void RegisterServices(
        IServiceCollection serviceCollection,
        IServerApplicationHost applicationHost)
    {
        ArgumentNullException.ThrowIfNull(serviceCollection);

        serviceCollection.AddSingleton<IAssFontParser, AssFontParser>();
        serviceCollection.AddSingleton<ISystemFontCatalog, SystemFontCatalog>();
        serviceCollection.AddScoped<ISubtitleFontResolver, SubtitleFontResolver>();
    }
}
