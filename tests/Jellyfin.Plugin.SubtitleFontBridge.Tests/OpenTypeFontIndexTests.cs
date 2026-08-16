using System.Buffers.Binary;
using System.Text;
using Jellyfin.Plugin.SubtitleFontBridge.Services;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Jellyfin.Plugin.SubtitleFontBridge.Tests;

public sealed class OpenTypeFontIndexTests : IDisposable
{
    private readonly string _root = Path.Combine(
        Path.GetTempPath(),
        "SubtitleFontBridgeOpenTypeTests",
        Guid.NewGuid().ToString("N"));

    public OpenTypeFontIndexTests() => Directory.CreateDirectory(_root);

    [Fact]
    public void ResolvesEveryLocalizedFamilyAliasFromInternalNameRecords()
    {
        var path = Path.Combine(_root, "YDOO08.TTF");
        WriteMinimalFont(
            path,
            (1, "Yj BACDOO Bold"),
            (1, "양재백두체B"),
            (16, "양재 백두체"));
        using var catalog = new SystemFontCatalog(
            NullLogger<SystemFontCatalog>.Instance,
            [_root],
            includePlatformFallback: false);

        var resolution = catalog.ResolveFamilies(
            ["Yj BACDOO Bold", "양재백두체B", "  @양재   백두체  "]);

        Assert.Empty(resolution.MissingFamilies);
        Assert.Equal(3, resolution.Families.Count);
        Assert.Single(resolution.Files);
        var expectedId = resolution.Files[0].Id;
        Assert.All(resolution.Families, family => Assert.Contains(expectedId, family.FontIds));
    }

    [Fact]
    public void StreamsTheIndexedFileWithoutUsingItsFileNameAsTheFamily()
    {
        var path = Path.Combine(_root, "unrelated-file-name.ttf");
        WriteMinimalFont(path, (1, "테스트 글꼴"));
        using var catalog = new SystemFontCatalog(
            NullLogger<SystemFontCatalog>.Instance,
            [_root],
            includePlatformFallback: false);

        var resolution = catalog.ResolveFamilies(["테스트 글꼴"]);

        var file = Assert.Single(resolution.Files);
        Assert.True(catalog.TryOpenFont(file.Id, out var resource));
        using var stream = resource.Stream;
        Span<byte> signature = stackalloc byte[4];
        stream.ReadExactly(signature);
        Assert.True(signature.SequenceEqual(new byte[] { 0x00, 0x01, 0x00, 0x00 }));
    }

    [Fact]
    public void NormalizationUsesFormKcAndCollapsesWhitespace()
    {
        var path = Path.Combine(_root, "normalized.ttf");
        WriteMinimalFont(path, (1, "ABC Font"));
        using var catalog = new SystemFontCatalog(
            NullLogger<SystemFontCatalog>.Instance,
            [_root],
            includePlatformFallback: false);

        var resolution = catalog.ResolveFamilies(["＠ＡＢＣ\t  Font"]);

        Assert.Single(resolution.Families);
        Assert.Empty(resolution.MissingFamilies);
    }

    [Fact]
    [Trait("Category", "Integration")]
    public void ResolvesLocalizedYangjaeAliasFromTheInstalledOpenTypeTable()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        var userFontDirectory = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Microsoft",
            "Windows",
            "Fonts");
        var fontPath = Path.Combine(userFontDirectory, "YDOO08.TTF");
        if (!File.Exists(fontPath))
        {
            return;
        }

        using var catalog = new SystemFontCatalog(
            NullLogger<SystemFontCatalog>.Instance,
            [userFontDirectory],
            includePlatformFallback: false);

        var resolution = catalog.ResolveFamilies(["양재백두체B"]);

        Assert.Single(resolution.Families);
        Assert.Empty(resolution.MissingFamilies);
    }

    public void Dispose()
    {
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, recursive: true);
        }
    }

    private static void WriteMinimalFont(
        string path,
        params (ushort NameId, string Value)[] names)
    {
        var encodedNames = names
            .Select(static name => (name.NameId, Bytes: Encoding.BigEndianUnicode.GetBytes(name.Value)))
            .ToArray();
        var recordBytes = encodedNames.Length * 12;
        var stringBytes = encodedNames.Sum(static name => name.Bytes.Length);
        var nameTableLength = 6 + recordBytes + stringBytes;
        var bytes = new byte[28 + nameTableLength];

        WriteUInt32(bytes, 0, 0x00010000);
        WriteUInt16(bytes, 4, 1);
        WriteUInt32(bytes, 12, 0x6E616D65); // name
        WriteUInt32(bytes, 20, 28);
        WriteUInt32(bytes, 24, (uint)nameTableLength);

        const int table = 28;
        WriteUInt16(bytes, table, 0);
        WriteUInt16(bytes, table + 2, (ushort)encodedNames.Length);
        WriteUInt16(bytes, table + 4, (ushort)(6 + recordBytes));

        var stringOffset = 0;
        for (var index = 0; index < encodedNames.Length; index++)
        {
            var record = table + 6 + (index * 12);
            WriteUInt16(bytes, record, 3);
            WriteUInt16(bytes, record + 2, 1);
            WriteUInt16(bytes, record + 4, index == 0 ? (ushort)0x0409 : (ushort)0x0412);
            WriteUInt16(bytes, record + 6, encodedNames[index].NameId);
            WriteUInt16(bytes, record + 8, (ushort)encodedNames[index].Bytes.Length);
            WriteUInt16(bytes, record + 10, (ushort)stringOffset);
            encodedNames[index].Bytes.CopyTo(bytes, table + 6 + recordBytes + stringOffset);
            stringOffset += encodedNames[index].Bytes.Length;
        }

        File.WriteAllBytes(path, bytes);
    }

    private static void WriteUInt16(byte[] bytes, int offset, ushort value) =>
        BinaryPrimitives.WriteUInt16BigEndian(bytes.AsSpan(offset, 2), value);

    private static void WriteUInt32(byte[] bytes, int offset, uint value) =>
        BinaryPrimitives.WriteUInt32BigEndian(bytes.AsSpan(offset, 4), value);
}
