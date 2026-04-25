param(
    [ValidateSet("Debug", "Release")]
    [string]$Configuration = "Debug",

    [ValidateSet("x64", "x86", "ARM64")]
    [string]$Platform = "x64",

    [string]$RegressionConfiguration = "Release",

    [switch]$SkipRegressions
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

$repoRoot = (Resolve-Path (Join-Path $PSScriptRoot "..")).Path
$validateScript = Join-Path $repoRoot "scripts\validate-release.ps1"

if (-not (Test-Path $validateScript)) {
    throw "Release validation script not found at $validateScript"
}

Write-Host "Validating KROIRA IPTV V2 release candidate..."
$args = @(
    "-NoProfile",
    "-ExecutionPolicy",
    "Bypass",
    "-File",
    $validateScript,
    "-Configuration",
    $Configuration,
    "-Platform",
    $Platform,
    "-RegressionConfiguration",
    $RegressionConfiguration
)

if ($SkipRegressions) {
    $args += "-SkipRegressions"
}

& powershell @args

exit $LASTEXITCODE
