#!/usr/bin/env pwsh
<#
.SYNOPSIS
    Builds CalendarSync for all platforms and generates release artifacts.

.DESCRIPTION
    Produces self-contained single-file binaries for all target platforms,
    packages them as zip (Windows) or tar.gz (Linux/macOS), writes a
    SHA-256 checksums file, and generates WinGet manifest YAML files.

    Run this locally to verify the release before pushing a tag, or let
    the GitHub Actions release workflow call it automatically.

.PARAMETER Version
    Package version (e.g. "1.2.3"). Defaults to "0.0.0-local".

.PARAMETER OutputDir
    Output directory for all artifacts. Defaults to "./dist".

.EXAMPLE
    ./build.ps1
    ./build.ps1 -Version "1.2.0"
    ./build.ps1 -Version "1.2.0" -OutputDir "/tmp/calsync-release"
#>
param(
    [string] $Version   = "0.0.0-local",
    [string] $OutputDir = "./dist"
)

$ErrorActionPreference = "Stop"
Set-StrictMode -Version Latest

$Project  = "src/CalendarSync.Cli"
$AppName  = "CalendarSync"
$RepoUrl  = "https://github.com/tsolbjor/calendarsync"
$Publisher = "Thomas Olbjor"

$Targets = @(
    @{ RID = "win-x64";   Ext = ".exe"; Archive = "zip" }
    @{ RID = "win-arm64"; Ext = ".exe"; Archive = "zip" }
    @{ RID = "linux-x64"; Ext = "";     Archive = "tar" }
    @{ RID = "osx-x64";   Ext = "";     Archive = "tar" }
    @{ RID = "osx-arm64"; Ext = "";     Archive = "tar" }
)

# ── Helpers ───────────────────────────────────────────────────────────────────

function Get-Sha256([string]$Path) {
    (Get-FileHash $Path -Algorithm SHA256).Hash.ToLower()
}

function Write-Step([string]$Msg) {
    Write-Host "`n▶ $Msg" -ForegroundColor Cyan
}

function Write-Detail([string]$Msg) {
    Write-Host "  $Msg" -ForegroundColor DarkGray
}

# ── Prepare output directory ──────────────────────────────────────────────────

if (Test-Path $OutputDir) { Remove-Item $OutputDir -Recurse -Force }
New-Item -ItemType Directory -Path $OutputDir | Out-Null

Write-Host "`nCalendarSync $Version — building all targets" -ForegroundColor White

# ── Build each target ─────────────────────────────────────────────────────────

$Artifacts = [ordered]@{}

foreach ($target in $Targets) {
    $rid        = $target.RID
    $ext        = $target.Ext
    $publishDir = Join-Path $OutputDir "publish/$rid"

    Write-Step "Publishing $rid"

    dotnet publish $Project `
        -c Release `
        -r $rid `
        --self-contained `
        -o $publishDir `
        /p:Version=$Version `
        /p:AssemblyVersion=$($Version -replace '-.*','') `
        /p:PublishSingleFile=true `
        /p:DebugType=none `
        /p:DebugSymbols=false `
        -nologo -verbosity:quiet

    if ($LASTEXITCODE -ne 0) { throw "dotnet publish failed for $rid" }

    $archiveName = "$AppName-$Version-$rid"
    $binaryPath  = Join-Path $publishDir "$AppName$ext"

    if ($target.Archive -eq "zip") {
        $archivePath = Join-Path $OutputDir "$archiveName.zip"
        Compress-Archive -Path $binaryPath -DestinationPath $archivePath
    } else {
        $archivePath = Join-Path $OutputDir "$archiveName.tar.gz"
        tar -czf $archivePath -C $publishDir "$AppName$ext"
        if ($LASTEXITCODE -ne 0) { throw "tar failed for $rid" }
    }

    $hash = Get-Sha256 $archivePath
    $Artifacts[$archiveName] = @{ Path = $archivePath; Hash = $hash; RID = $rid }
    Write-Detail "→ $(Split-Path $archivePath -Leaf)"
    Write-Detail "  sha256: $hash"
}

# ── Checksums file ────────────────────────────────────────────────────────────

Write-Step "Writing checksums"

$checksumLines = $Artifacts.GetEnumerator() | ForEach-Object {
    "$($_.Value.Hash)  $(Split-Path $_.Value.Path -Leaf)"
}
$checksumFile = Join-Path $OutputDir "checksums.txt"
$checksumLines | Set-Content $checksumFile
Write-Detail "→ checksums.txt"

# ── WinGet manifests ──────────────────────────────────────────────────────────

Write-Step "Generating WinGet manifests"

$wingetDir = Join-Path $OutputDir "winget/$Version"
New-Item -ItemType Directory -Path $wingetDir | Out-Null

$winX64   = $Artifacts["$AppName-$Version-win-x64"]
$winArm64 = $Artifacts["$AppName-$Version-win-arm64"]

$winX64Url   = "$RepoUrl/releases/download/v$Version/$AppName-$Version-win-x64.zip"
$winArm64Url = "$RepoUrl/releases/download/v$Version/$AppName-$Version-win-arm64.zip"

# Version manifest
@"
PackageIdentifier: tsolbjor.CalendarSync
PackageVersion: $Version
DefaultLocale: en-US
ManifestType: version
ManifestVersion: 1.6.0
"@ | Set-Content (Join-Path $wingetDir "tsolbjor.CalendarSync.yaml")

# Installer manifest
@"
PackageIdentifier: tsolbjor.CalendarSync
PackageVersion: $Version
InstallerLocale: en-US
InstallerType: portable
Commands:
- CalendarSync
InstallModes:
- interactive
- silent
Installers:
- Architecture: x64
  InstallerUrl: $winX64Url
  InstallerSha256: $($winX64.Hash.ToUpper())
- Architecture: arm64
  InstallerUrl: $winArm64Url
  InstallerSha256: $($winArm64.Hash.ToUpper())
ManifestType: installer
ManifestVersion: 1.6.0
"@ | Set-Content (Join-Path $wingetDir "tsolbjor.CalendarSync.installer.yaml")

# Locale manifest
@"
PackageIdentifier: tsolbjor.CalendarSync
PackageVersion: $Version
PackageLocale: en-US
Publisher: $Publisher
PublisherUrl: https://github.com/tsolbjor
PublisherSupportUrl: $RepoUrl/issues
PackageName: CalendarSync
PackageUrl: $RepoUrl
License: MIT
LicenseUrl: $RepoUrl/blob/main/LICENSE
Copyright: Copyright (c) $Publisher
ShortDescription: Plan your week across multiple calendar providers
Description: |-
  A .NET CLI tool for consultants and anyone juggling multiple calendar
  identities. Fetches events from Microsoft 365, Google Calendar, and
  read-only ICS feeds, then lets you pick which ones to block off as
  placeholders in your other calendars.
Tags:
- calendar
- sync
- microsoft
- google
- productivity
- cli
ManifestType: defaultLocale
ManifestVersion: 1.6.0
"@ | Set-Content (Join-Path $wingetDir "tsolbjor.CalendarSync.locale.en-US.yaml")

Write-Detail "→ tsolbjor.CalendarSync.yaml"
Write-Detail "→ tsolbjor.CalendarSync.installer.yaml"
Write-Detail "→ tsolbjor.CalendarSync.locale.en-US.yaml"

# ── Summary ───────────────────────────────────────────────────────────────────

Write-Host "`n✔ Done — artifacts in $OutputDir" -ForegroundColor Green
Write-Host @"

  To publish a GitHub Release, push a tag:
    git tag v$Version && git push origin v$Version

  To submit to WinGet, open a PR to https://github.com/microsoft/winget-pkgs
  with the files from: $wingetDir

"@ -ForegroundColor DarkGray
