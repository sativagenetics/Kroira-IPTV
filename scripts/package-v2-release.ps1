param(
    [ValidateSet("Debug", "Release")]
    [string]$Configuration = "Release",

    [ValidateSet("x64", "x86", "ARM64")]
    [string]$Platform = "x64",

    [switch]$Unsigned,

    [switch]$SkipValidation,

    [string]$OutputRoot = "artifacts\packages\v2-rc"
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

$repoRoot = (Resolve-Path (Join-Path $PSScriptRoot "..")).Path
$validateScript = Join-Path $repoRoot "scripts\validate-v2-release.ps1"
$unsignedPackageScript = Join-Path $repoRoot "scripts\package-unsigned.ps1"

if (-not $Unsigned) {
    throw "Only unsigned local release-candidate packaging is supported by this repository script. Pass -Unsigned."
}

if (-not $SkipValidation) {
    if (-not (Test-Path $validateScript)) {
        throw "V2 validation script not found at $validateScript"
    }

    & powershell -NoProfile -ExecutionPolicy Bypass -File $validateScript `
        -Configuration $Configuration `
        -Platform $Platform
    if ($LASTEXITCODE -ne 0) {
        exit $LASTEXITCODE
    }
}

if (-not (Test-Path $unsignedPackageScript)) {
    throw "Unsigned package script not found at $unsignedPackageScript"
}

Write-Host "Packaging KROIRA IPTV V2 release candidate..."
& powershell -NoProfile -ExecutionPolicy Bypass -File $unsignedPackageScript `
    -Configuration $Configuration `
    -Platform $Platform `
    -OutputRoot $OutputRoot

exit $LASTEXITCODE
