#requires -Version 5.1
<#
.SYNOPSIS
  Runs the tests and builds the release: dist\RogBacklightTimeout.exe (self-contained, single file) and the release
  zip with the licence and notice files, BUILD-INFO.txt and SHA256SUMS.
.DESCRIPTION
  The version comes from the latest vX.Y.Z git tag (MinVer); untagged commits build as a pre-release of the next
  version. CI runs this same script, so a local build and a release build are made the same way.
.EXAMPLE
  .\scripts\publish.ps1
#>
[CmdletBinding()]
param(
    [switch]$SkipTests
)

$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$out = Join-Path $root 'dist'
$solution = Join-Path $root 'RogBacklightTimeout.slnx'

function Invoke-DotNet {
    & dotnet @args
    if ($LASTEXITCODE -ne 0) { throw "dotnet $($args[0]) failed with exit code $LASTEXITCODE." }
}

if (-not $SkipTests) {
    Invoke-DotNet test --solution $solution -c Release
}

if (Test-Path $out) { Remove-Item $out -Recurse -Force }
Invoke-DotNet publish (Join-Path $root 'src\RogBacklightTimeout\RogBacklightTimeout.csproj') -c Release -nologo -v:q -o $out

$exe = Join-Path $out 'RogBacklightTimeout.exe'
$version = ((Get-Item $exe).VersionInfo.ProductVersion -split '\+')[0]

# Licence texts and attribution that must travel with the executable (GPL-3.0, Apache-2.0 for HidSharp, MIT for .NET).
$notices = 'LICENSE', 'NOTICE.md', 'THIRD-PARTY-NOTICES.md', 'THIRD-PARTY-NOTICES-dotnet.txt'
foreach ($name in $notices) {
    $source = Join-Path $root $name
    if (-not (Test-Path $source)) { throw "Missing required notice file: $name" }
    Copy-Item $source $out
}

function Get-Sha256([string]$Path) { (Get-FileHash $Path -Algorithm SHA256).Hash.ToLowerInvariant() }

$commit = (& git -C $root rev-parse HEAD 2>$null)
if (-not $commit) { $commit = 'unknown' }
$built = if ($env:GITHUB_ACTIONS -eq 'true') {
    "GitHub Actions, $env:GITHUB_REPOSITORY run $env:GITHUB_RUN_ID"
} else {
    'local build'
}
$info = @(
    "ROG Backlight Timeout $version",
    "source: https://github.com/XPookachu/rog-backlight-timeout/tree/$commit",
    "built by: $built",
    'files (sha256):'
)
foreach ($name in @('RogBacklightTimeout.exe') + $notices) {
    $info += "  $(Get-Sha256 (Join-Path $out $name))  $name"
}
$buildInfo = Join-Path $out 'BUILD-INFO.txt'
$info | Set-Content -Encoding utf8 $buildInfo

$zip = Join-Path $out "RogBacklightTimeout-v$version.zip"
$packaged = @($exe, $buildInfo) + ($notices | ForEach-Object { Join-Path $out $_ })
Compress-Archive -Path $packaged -DestinationPath $zip

$sums = Join-Path $out 'SHA256SUMS'
@($exe, $zip) | ForEach-Object { "$(Get-Sha256 $_)  $(Split-Path $_ -Leaf)" } | Set-Content -Encoding ascii $sums

Get-Content $buildInfo
Write-Host ("Created {0} ({1:N1} MB)" -f $exe, ((Get-Item $exe).Length / 1MB))
Write-Host "Created $zip (release asset)"
Get-Content $sums
