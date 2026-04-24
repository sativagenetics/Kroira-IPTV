param(
    [string]$Configuration = "Debug",
    [string]$Platform = "x64",
    [switch]$SkipBuild,
    [switch]$SkipPlayer,
    [switch]$SanitizedDataConfirmed
)

$ErrorActionPreference = "Stop"

$repoRoot = Resolve-Path (Join-Path $PSScriptRoot "..")
$outputRoot = Join-Path $repoRoot "artifacts\store-screenshots"

Write-Host "Capturing packaged KROIRA screenshots for store/release review."
Write-Host "Close notification overlays first. Use sanitized/sample sources for public Store assets."
if (-not $SanitizedDataConfirmed) {
    Write-Warning "This script cannot sanitize local app data. Public Store screenshots require a clean profile with only licensed/sample content. See docs\store_screenshot_workflow.md."
}

& (Join-Path $PSScriptRoot "visual-smoke.ps1") `
    -Configuration $Configuration `
    -Platform $Platform `
    -OutputRoot $outputRoot `
    -SkipBuild:$SkipBuild `
    -SkipPlayer:$SkipPlayer

$latestCapture = Get-ChildItem -LiteralPath $outputRoot -Directory |
    Sort-Object LastWriteTime -Descending |
    Select-Object -First 1

if ($latestCapture) {
    $notice = @(
        "KROIRA Store screenshot capture",
        "",
        "These files are store-ready only if the app was launched with sanitized/sample data.",
        "Do not submit captures that expose real provider names, private source names, third-party logos, copyrighted posters, credentials, MAC addresses, or personal profile names.",
        "Sanitized data confirmed: $($SanitizedDataConfirmed.IsPresent)",
        "Workflow: docs\store_screenshot_workflow.md"
    )

    $notice | Set-Content -Path (Join-Path $latestCapture.FullName "STORE_SCREENSHOT_NOTICE.txt") -Encoding UTF8
}
