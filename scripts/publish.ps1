#requires -Version 5.1
<#
.SYNOPSIS
  Runs the tests and builds the single self-contained executable into dist\RogBacklightTimeout.exe.
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

if (-not $SkipTests) {
    dotnet test (Join-Path $root 'tests\RogBacklightTimeout.Tests') -c Release -nologo -v:q
    if ($LASTEXITCODE -ne 0) { throw 'Tests failed.' }
}

if (Test-Path $out) { Remove-Item $out -Recurse -Force }
dotnet publish (Join-Path $root 'src\RogBacklightTimeout\RogBacklightTimeout.csproj') -c Release -nologo -v:q -o $out
if ($LASTEXITCODE -ne 0) { throw 'Publish failed.' }

$exe = Join-Path $out 'RogBacklightTimeout.exe'

# Licence texts and attribution that must travel with the executable (GPL-3.0, Apache-2.0 for HidSharp, MIT for .NET).
$notices = 'LICENSE', 'NOTICE.md', 'THIRD-PARTY-NOTICES.md', 'THIRD-PARTY-NOTICES-dotnet.txt'
foreach ($name in $notices) {
    $src = Join-Path $root $name
    if (-not (Test-Path $src)) { throw "Missing required notice file: $name" }
    Copy-Item $src $out
}

[xml]$props = Get-Content (Join-Path $root 'Directory.Build.props')
$version = $props.Project.PropertyGroup.Version
$zip = Join-Path $out "RogBacklightTimeout-v$version.zip"
Compress-Archive -Path (@($exe) + ($notices | ForEach-Object { Join-Path $out $_ })) -DestinationPath $zip

Write-Host ("Created {0} ({1:N1} MB)" -f $exe, ((Get-Item $exe).Length / 1MB))
Write-Host ("SHA256  exe  {0}" -f (Get-FileHash $exe -Algorithm SHA256).Hash.ToLowerInvariant())
Write-Host ("Created {0} (release asset: exe + licence and notice files)" -f $zip)
Write-Host ("SHA256  zip  {0}" -f (Get-FileHash $zip -Algorithm SHA256).Hash.ToLowerInvariant())
