[CmdletBinding()]
param(
    [string]$DotnetPath = "dotnet",
    [ValidateSet("Debug", "Release")]
    [string]$Configuration = "Release"
)

$ErrorActionPreference = "Stop"
$projectRoot = Split-Path -Parent $PSScriptRoot
$project = Join-Path $projectRoot "src\Jellyfin.Plugin.SubtitleFontBridge\Jellyfin.Plugin.SubtitleFontBridge.csproj"
$publishDirectory = Join-Path $projectRoot "artifacts\publish"
$archive = Join-Path $projectRoot "artifacts\Jellyfin.Plugin.SubtitleFontBridge_1.0.0.0.zip"

& $DotnetPath publish $project `
    --configuration $Configuration `
    --output $publishDirectory `
    --nologo `
    -p:UseAppHost=false
if ($LASTEXITCODE -ne 0) {
    throw "dotnet publish failed with exit code $LASTEXITCODE."
}

$pluginAssembly = Join-Path $publishDirectory "Jellyfin.Plugin.SubtitleFontBridge.dll"
if (-not (Test-Path -LiteralPath $pluginAssembly -PathType Leaf)) {
    throw "The plugin assembly was not produced: $pluginAssembly"
}

New-Item -ItemType Directory -Path (Split-Path -Parent $archive) -Force | Out-Null
Compress-Archive -LiteralPath $pluginAssembly -DestinationPath $archive -Force

$hash = Get-FileHash -LiteralPath $archive -Algorithm SHA256
[pscustomobject]@{
    Archive = $archive
    Size = (Get-Item -LiteralPath $archive).Length
    SHA256 = $hash.Hash.ToLowerInvariant()
}
