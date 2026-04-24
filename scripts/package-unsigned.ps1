param(
    [ValidateSet("Debug", "Release")]
    [string]$Configuration = "Release",

    [ValidateSet("x64", "x86", "ARM64")]
    [string]$Platform = "x64",

    [string]$OutputRoot = "artifacts\packages\unsigned"
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

$repoRoot = (Resolve-Path (Join-Path $PSScriptRoot "..")).Path
$project = Join-Path $repoRoot "src\Kroira.App\Kroira.App.csproj"
$projectText = Get-Content -Path $project -Raw

if ($projectText -notmatch "<EnableMsixTooling>true</EnableMsixTooling>" -or
    $projectText -notmatch "<WindowsPackageType>MSIX</WindowsPackageType>") {
    throw "Unsigned MSIX packaging is not configured for this project."
}

$runtime = switch ($Platform) {
    "x64" { "win-x64" }
    "x86" { "win-x86" }
    "ARM64" { "win-arm64" }
}

if ([System.IO.Path]::IsPathRooted($OutputRoot)) {
    $packageOutput = $OutputRoot
}
else {
    $packageOutput = Join-Path $repoRoot $OutputRoot
}

New-Item -ItemType Directory -Force -Path $packageOutput | Out-Null

$packageArgs = @(
    "publish",
    $project,
    "-c", $Configuration,
    "-p:Platform=$Platform",
    "-p:RuntimeIdentifier=$runtime",
    "-p:AppxPackageSigningEnabled=false",
    "-p:GenerateAppxPackageOnBuild=true",
    "-p:UapAppxPackageBuildMode=SideloadOnly",
    "-p:AppxBundle=Never",
    "-p:AppxPackageDir=$packageOutput\"
)

Write-Host "Creating unsigned MSIX package output under $packageOutput"
& dotnet @packageArgs
if ($LASTEXITCODE -ne 0) {
    exit $LASTEXITCODE
}

$packages = Get-ChildItem -Path $packageOutput -Recurse -File -Include *.msix,*.appx,*.appxupload,*.msixbundle,*.appxbundle |
    Sort-Object LastWriteTime -Descending

if (-not $packages) {
    Write-Warning "Packaging completed, but no package artifacts were found under $packageOutput."
}
else {
    Write-Host "Package artifacts:"
    $packages | ForEach-Object { Write-Host "  $($_.FullName)" }
}
