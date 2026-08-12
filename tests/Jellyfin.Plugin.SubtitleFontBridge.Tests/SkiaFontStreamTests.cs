using Jellyfin.Plugin.SubtitleFontBridge.Services;
using SkiaSharp;
using Xunit;

namespace Jellyfin.Plugin.SubtitleFontBridge.Tests;

public sealed class SkiaFontStreamTests
{
    [Fact]
    public void SupportsOffsetReadsAndSeeking()
    {
        using var manager = SKFontManager.CreateDefault();
        if (manager.FontFamilyCount == 0)
        {
            return;
        }

        var family = manager.GetFamilyName(0);
        var styles = manager.GetFontStyles(family);
        var typeface = styles.CreateTypeface(0);
        var source = typeface.OpenStream(out _);
        Assert.NotNull(source);

        using var stream = new SkiaFontStream(styles, typeface, source);
        var buffer = Enumerable.Repeat((byte)0xCC, 12).ToArray();
        var read = stream.Read(buffer, 4, 4);

        Assert.Equal(4, read);
        Assert.All(buffer[..4], static value => Assert.Equal(0xCC, value));
        Assert.Equal(4, stream.Position);
        Assert.Equal(0, stream.Seek(0, SeekOrigin.Begin));

        Span<byte> first = stackalloc byte[4];
        stream.ReadExactly(first);
        Assert.Equal(buffer.AsSpan(4, 4), first);
    }
}
