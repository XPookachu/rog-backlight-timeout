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
$hash = (Get-FileHash $exe -Algorithm SHA256).Hash.ToLowerInvariant()
Write-Host ("Created {0} ({1:N1} MB)" -f $exe, ((Get-Item $exe).Length / 1MB))
Write-Host "SHA256  $hash"
