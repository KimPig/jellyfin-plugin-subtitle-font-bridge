using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;
using Jellyfin.Plugin.SubtitleFontBridge.Services;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Jellyfin.Plugin.SubtitleFontBridge.Tests;

public sealed class FontSourceSelectionTests : IDisposable
{
    private readonly string _root = Path.Combine(
        Path.GetTempPath(),
        "SubtitleFontBridgeSourceTests",
        Guid.NewGuid().ToString("N"));

    public FontSourceSelectionTests() => Directory.CreateDirectory(_root);

    [Fact]
    public void UsesOptimizerCacheWhenEnabledAndAvailable()
    {
        var systemDirectory = CreateDirectory("system");
        var optimizerDirectory = CreateDirectory("optimizer");
        WriteMinimalFont(Path.Combine(optimizerDirectory, "cached.ttf"), "Cached Font");
        using var catalog = CreateCatalog(
            systemDirectory,
            optimizerDirectory,
            new SystemFontCatalog.FontSourceState(true, true, true));

        var resolution = catalog.ResolveFamilies(["Cached Font"]);

        Assert.Empty(resolution.MissingFamilies);
        Assert.Single(resolution.Files);
    }

    [Fact]
    public void IgnoresOptimizerCacheWhenPluginIsUnavailable()
    {
        var systemDirectory = CreateDirectory("system");
        var optimizerDirectory = CreateDirectory("optimizer");
        WriteMinimalFont(Path.Combine(optimizerDirectory, "cached.ttf"), "Cached Font");
        using var catalog = CreateCatalog(
            systemDirectory,
            optimizerDirectory,
            new SystemFontCatalog.FontSourceState(true, true, false));

        var resolution = catalog.ResolveFamilies(["Cached Font"]);

        Assert.Contains("Cached Font", resolution.MissingFamilies);
        Assert.Empty(resolution.Files);
    }

    [Fact]
    public void ServerFontTakesPriorityOverOptimizerCache()
    {
        var systemDirectory = CreateDirectory("system");
        var optimizerDirectory = CreateDirectory("optimizer");
        var systemPath = Path.Combine(systemDirectory, "system.ttf");
        WriteMinimalFont(systemPath, "Shared Font");
        WriteMinimalFont(Path.Combine(optimizerDirectory, "optimizer.ttf"), "Shared Font", "Extra Alias");
        using var catalog = CreateCatalog(
            systemDirectory,
            optimizerDirectory,
            new SystemFontCatalog.FontSourceState(true, true, true));

        var resolution = catalog.ResolveFamilies(["Shared Font"]);

        var file = Assert.Single(resolution.Files);
        Assert.Equal(
            Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(systemPath))),
            file.Id);
    }

    [Fact]
    public void ConfigurationChangeInvalidatesResolvedFamilies()
    {
        var systemDirectory = CreateDirectory("system");
        var optimizerDirectory = CreateDirectory("optimizer");
        WriteMinimalFont(Path.Combine(systemDirectory, "system.ttf"), "System Font");
        var state = new SystemFontCatalog.FontSourceState(true, false, false);
        using var catalog = new SystemFontCatalog(
            NullLogger<SystemFontCatalog>.Instance,
            [systemDirectory],
            optimizerDirectory,
            includePlatformFallback: false,
            () => state,
            watchForChanges: false);

        Assert.Empty(catalog.ResolveFamilies(["System Font"]).MissingFamilies);
        state = new SystemFontCatalog.FontSourceState(false, false, false);

        var disabledResolution = catalog.ResolveFamilies(["System Font"]);
        Assert.Contains("System Font", disabledResolution.MissingFamilies);
        Assert.Empty(disabledResolution.Files);
    }

    [Fact]
    public async Task DetectsFontAddedToOptimizerCacheWhileRunning()
    {
        var systemDirectory = CreateDirectory("system");
        var optimizerDirectory = CreateDirectory("optimizer");
        using var catalog = new SystemFontCatalog(
            NullLogger<SystemFontCatalog>.Instance,
            [systemDirectory],
            optimizerDirectory,
            includePlatformFallback: false,
            static () => new SystemFontCatalog.FontSourceState(true, true, true),
            watchForChanges: true);

        Assert.Contains(
            "Late Font",
            catalog.ResolveFamilies(["Late Font"]).MissingFamilies);

        var shardDirectory = Path.Combine(optimizerDirectory, "ab");
        Directory.CreateDirectory(shardDirectory);
        WriteMinimalFont(Path.Combine(shardDirectory, "added.ttf"), "Late Font");

        var deadline = DateTime.UtcNow.AddSeconds(5);
        while (DateTime.UtcNow < deadline)
        {
            var resolution = catalog.ResolveFamilies(["Late Font"]);
            if (resolution.MissingFamilies.Count == 0)
            {
                Assert.Single(resolution.Files);
                return;
            }

            await Task.Delay(100);
        }

        Assert.Fail("The optimizer font cache watcher did not invalidate the index.");
    }

    public void Dispose()
    {
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, recursive: true);
        }
    }

    private SystemFontCatalog CreateCatalog(
        string systemDirectory,
        string optimizerDirectory,
        SystemFontCatalog.FontSourceState state) =>
        new(
            NullLogger<SystemFontCatalog>.Instance,
            [systemDirectory],
            optimizerDirectory,
            includePlatformFallback: false,
            () => state,
            watchForChanges: false);

    private string CreateDirectory(string name)
    {
        var path = Path.Combine(_root, name);
        Directory.CreateDirectory(path);
        return path;
    }

    private static void WriteMinimalFont(
        string path,
        params string[] familyNames)
    {
        var encodedNames = familyNames
            .Select(static value => Encoding.BigEndianUnicode.GetBytes(value))
            .ToArray();
        var recordBytes = encodedNames.Length * 12;
        var stringBytes = encodedNames.Sum(static value => value.Length);
        var nameTableLength = 6 + recordBytes + stringBytes;
        var bytes = new byte[28 + nameTableLength];

        WriteUInt32(bytes, 0, 0x00010000);
        WriteUInt16(bytes, 4, 1);
        WriteUInt32(bytes, 12, 0x6E616D65);
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
            WriteUInt16(bytes, record + 4, 0x0409);
            WriteUInt16(bytes, record + 6, 1);
            WriteUInt16(bytes, record + 8, (ushort)encodedNames[index].Length);
            WriteUInt16(bytes, record + 10, (ushort)stringOffset);
            encodedNames[index].CopyTo(bytes, table + 6 + recordBytes + stringOffset);
            stringOffset += encodedNames[index].Length;
        }

        File.WriteAllBytes(path, bytes);
    }

    private static void WriteUInt16(byte[] bytes, int offset, ushort value) =>
        BinaryPrimitives.WriteUInt16BigEndian(bytes.AsSpan(offset, 2), value);

    private static void WriteUInt32(byte[] bytes, int offset, uint value) =>
        BinaryPrimitives.WriteUInt32BigEndian(bytes.AsSpan(offset, 4), value);
}
