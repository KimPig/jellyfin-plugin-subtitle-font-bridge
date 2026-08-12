# Subtitle Font Bridge

A Jellyfin plugin for Windows servers that supplies Jellyfin Web with the fonts referenced by ASS/SSA subtitles.

`Jellyfin.Plugin.SubtitleFontBridge` reads a selected subtitle stream, extracts only the font families
referenced by its styles and inline `\\fn` overrides, and exposes matching fonts
installed on the server through authenticated HTTP endpoints.

The plugin does not copy fonts into its data directory. Font bytes are opened
from the operating system font manager only when a family is requested. TTF,
OTF, WOFF, WOFF2, and TTC resources are identified by a SHA-256 content id, so
multiple faces from the same TTC collection are sent only once.

## Compatibility

- Jellyfin Server 12 (`targetAbi: 12.0.0.0`)
- .NET 10
- Built against `Jellyfin.Controller` and `Jellyfin.Model` 12.0.0-rc5
- Built against the SkiaSharp 3.119.4 already bundled with Jellyfin 12

The catalog works with the fonts visible to the Jellyfin server process. On
Windows, that includes machine fonts and user fonts visible to the account that
runs Jellyfin. Restart Jellyfin after installing or removing fonts.

## API

All routes require normal Jellyfin authentication. `Status` additionally
requires administrator elevation.

| Method | Route | Purpose |
| --- | --- | --- |
| `GET` | `SubtitleFontBridge/Subtitles/{itemId}/{mediaSourceId}/{subtitleIndex}` | Extract an ASS/SSA track and resolve its fonts. Item access is checked for the authenticated user. |
| `POST` | `SubtitleFontBridge/Resolve` | Resolve up to 32 explicit family names. |
| `GET` | `SubtitleFontBridge/Files/{sha256}.{extension}` | Stream an opaque, previously resolved font resource with range and immutable-cache support. |
| `GET` | `SubtitleFontBridge/Status` | Show platform, SkiaSharp, family-cache, and file-index diagnostics. |

Example explicit resolution request:

```json
{
  "Families": ["맑은 고딕", "Noto Sans CJK KR", "Arial"]
}
```

Each response returns relative `Path` values such as:

```text
SubtitleFontBridge/Files/52ab...f109.ttc
```

A Jellyfin Web integration should turn each path into an authenticated server
URL with its existing `ApiClient`, then add all returned URLs to the
`@jellyfin/libass-wasm` `fonts` option. The stock Jellyfin Web client does not
know about this plugin API, so the plugin intentionally does not change playback
until the small Web-side integration is added.

## Build and test

Install a .NET 10 SDK, then run:

```powershell
dotnet test .\Jellyfin.Plugin.SubtitleFontBridge.slnx -c Release
powershell -ExecutionPolicy Bypass -File .\scripts\package.ps1
```

The packaging script creates:

```text
artifacts/Jellyfin.Plugin.SubtitleFontBridge_1.0.0.0.zip
```

The ZIP contains only `Jellyfin.Plugin.SubtitleFontBridge.dll`. Jellyfin supplies the
controller, model, ASP.NET Core, and SkiaSharp runtime assemblies.

## Manual installation

1. Stop Jellyfin Server.
2. Create a versioned directory below Jellyfin's plugin directory, for example
   `plugins/Subtitle Font Bridge_1.0.0.0`.
3. Extract `Jellyfin.Plugin.SubtitleFontBridge.dll` into that directory.
4. Start Jellyfin and check `GET SubtitleFontBridge/Status` as an administrator.

For the default Windows data path, the plugin root is normally:

```text
C:\ProgramData\Jellyfin\Server\plugins
```

## Security and font licensing

- Font paths are never accepted from a client or returned in JSON.
- File routes accept only a 64-character content hash that the plugin indexed.
- Subtitle item lookup uses the authenticated Jellyfin user, so an inaccessible
  item returns `404`.
- Font routes are authenticated, but a logged-in Jellyfin user can download a
  font after resolving its family. Only use fonts whose license permits this
  server-to-client transfer.
- Subtitle analysis is limited to 8 MiB and explicit resolution is limited to
  32 family names per request.

## Current scope

This first version deliberately has no configuration page and no persistent
font cache. The first request for a family hashes its matching font files on the
request thread; later requests during the same server session reuse that result.
This keeps startup fast, but the first playback that references several large
CJK fonts can take longer. Adding bounded background indexing, allow/deny lists,
and a Web client integration are separate follow-up changes.
