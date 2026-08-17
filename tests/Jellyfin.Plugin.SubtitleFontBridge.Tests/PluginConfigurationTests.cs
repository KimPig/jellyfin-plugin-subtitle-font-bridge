using Jellyfin.Plugin.SubtitleFontBridge.Configuration;
using Xunit;

namespace Jellyfin.Plugin.SubtitleFontBridge.Tests;

public sealed class PluginConfigurationTests
{
    [Fact]
    public void FontSourcesAreEnabledByDefault()
    {
        var configuration = new PluginConfiguration();

        Assert.True(configuration.SearchServerFonts);
        Assert.True(configuration.SearchAttachmentOptimizerCache);
    }
}
