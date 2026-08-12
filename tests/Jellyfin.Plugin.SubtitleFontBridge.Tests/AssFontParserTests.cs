using System.Text;
using Jellyfin.Plugin.SubtitleFontBridge.Services;
using Xunit;

namespace Jellyfin.Plugin.SubtitleFontBridge.Tests;

public sealed class AssFontParserTests
{
    [Fact]
    public void ExtractFamiliesFindsStylesAndInlineOverridesInOrder()
    {
        const string Subtitle = """
            [Script Info]
            Title: Parser test

            [V4+ Styles]
            Format: Name, Fontname, Fontsize, PrimaryColour, Bold, Italic
            Style: Default,맑은 고딕,48,&H00FFFFFF,0,0
            Style: Signs,Noto Sans CJK KR,36,&H00FFFFFF,-1,0
            Style: Duplicate,맑은 고딕,32,&H00FFFFFF,0,0

            [Events]
            Format: Layer, Start, End, Style, Text
            Dialogue: 0,0:00:00.00,0:00:02.00,Default,{\fnArial\b1}Hello
            Dialogue: 0,0:00:02.00,0:00:04.00,Default,{\fn@Yu Gothic\i1}World
            """;

        var result = AssFontParser.ExtractFamilies(Subtitle);

        Assert.Equal(
            ["맑은 고딕", "Noto Sans CJK KR", "Arial", "Yu Gothic"],
            result);
    }

    [Fact]
    public void ExtractFamiliesHonorsTheStyleFormatColumnOrder()
    {
        const string Subtitle = """
            [V4+ Styles]
            Format: Name, Fontsize, Fontname, Bold
            Style: Default,52,Source Han Sans KR,0
            """;

        var result = AssFontParser.ExtractFamilies(Subtitle);

        Assert.Equal(["Source Han Sans KR"], result);
    }

    [Fact]
    public async Task ExtractFamiliesAsyncDetectsUtf16Bom()
    {
        const string Subtitle = "[V4+ Styles]\r\nFormat: Name, Fontname\r\nStyle: Default,맑은 고딕\r\n";
        var preamble = Encoding.Unicode.GetPreamble();
        var content = Encoding.Unicode.GetBytes(Subtitle);
        await using var stream = new MemoryStream([.. preamble, .. content]);
        var parser = new AssFontParser();

        var result = await parser.ExtractFamiliesAsync(stream, CancellationToken.None);

        Assert.Equal(["맑은 고딕"], result);
    }

    [Fact]
    public async Task ExtractFamiliesAsyncRejectsOversizedInputBeforeReading()
    {
        await using var stream = new MemoryStream(new byte[AssFontParser.MaximumSubtitleBytes + 1]);
        var parser = new AssFontParser();

        await Assert.ThrowsAsync<InvalidDataException>(
            () => parser.ExtractFamiliesAsync(stream, CancellationToken.None));
    }
}
