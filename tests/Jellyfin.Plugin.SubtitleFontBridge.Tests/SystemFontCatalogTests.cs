using Jellyfin.Plugin.SubtitleFontBridge.Services;
using Microsoft.Extensions.Logging.Abstractions;
using SkiaSharp;
using Xunit;

namespace Jellyfin.Plugin.SubtitleFontBridge.Tests;

public sealed class SystemFontCatalogTests
{
    [Fact]
    public void ResolveFamiliesOpensARealPlatformFont()
    {
        using var manager = SKFontManager.CreateDefault();
        if (manager.FontFamilyCount == 0)
        {
            return;
        }

        var family = manager.GetFamilyName(0);
        using var catalog = new SystemFontCatalog(NullLogger<SystemFontCatalog>.Instance);

        var resolution = catalog.ResolveFamilies([family, "__jellyfin_font_that_does_not_exist__"]);

        Assert.Single(resolution.Families);
        Assert.Contains("__jellyfin_font_that_does_not_exist__", resolution.MissingFamilies);
        Assert.Equal(1, catalog.CachedFamilyCount);
        Assert.NotEmpty(resolution.Files);
        var file = resolution.Files[0];
        Assert.True(file.Size > 4);
        Assert.True(catalog.TryOpenFont(file.Id, out var resource));
        using var fontStream = resource.Stream;
        Span<byte> signature = stackalloc byte[4];
        fontStream.ReadExactly(signature);
        Assert.Contains(
            EncodingName(signature),
            new[] { "ttf", "otf", "ttc", "woff", "woff2" });
        Assert.False(catalog.TryOpenFont(new string('0', 64), out _));
    }

    [Fact]
    public void LocalizedAndEnglishMalgunNamesShareAtLeastOneFileOnWindows()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        using var manager = SKFontManager.CreateDefault();
        using var englishStyles = manager.GetFontStyles("Malgun Gothic");
        using var koreanStyles = manager.GetFontStyles("맑은 고딕");
        if (englishStyles.Count == 0 || koreanStyles.Count == 0)
        {
            return;
        }

        using var catalog = new SystemFontCatalog(NullLogger<SystemFontCatalog>.Instance);
        var resolution = catalog.ResolveFamilies(["Malgun Gothic", "맑은 고딕"]);
        var english = Assert.Single(
            resolution.Families,
            static family => family.RequestedFamily == "Malgun Gothic");
        var korean = Assert.Single(
            resolution.Families,
            static family => family.RequestedFamily == "맑은 고딕");

        Assert.NotEmpty(english.FontIds.Intersect(korean.FontIds, StringComparer.OrdinalIgnoreCase));
    }

    private static string EncodingName(ReadOnlySpan<byte> signature)
    {
        if (signature.SequenceEqual("OTTO"u8))
        {
            return "otf";
        }

        if (signature.SequenceEqual("ttcf"u8))
        {
            return "ttc";
        }

        if (signature.SequenceEqual("wOFF"u8))
        {
            return "woff";
        }

        if (signature.SequenceEqual("wOF2"u8))
        {
            return "woff2";
        }

        return (signature[0] == 0x00
                && signature[1] == 0x01
                && signature[2] == 0x00
                && signature[3] == 0x00)
            || signature.SequenceEqual("true"u8)
            || signature.SequenceEqual("typ1"u8)
                ? "ttf"
                : "unknown";
    }
}
