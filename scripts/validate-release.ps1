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
$solution = Join-Path $repoRoot "Kroira.sln"
$regressionScript = Join-Path $repoRoot "scripts\ci-regressions.ps1"

function Invoke-CheckedCommand {
    param(
        [Parameter(Mandatory = $true)]
        [string]$Name,

        [Parameter(Mandatory = $true)]
        [scriptblock]$Command
    )

    Write-Host ""
    Write-Host "==> $Name"
    & $Command
    if ($LASTEXITCODE -ne 0) {
        exit $LASTEXITCODE
    }
}

$commonArgs = @(
    "-c", $Configuration,
    "-p:Platform=$Platform",
    "-p:AppxPackageSigningEnabled=false",
    "--no-restore",
    "/m:1",
    "-p:BuildInParallel=false"
)

Invoke-CheckedCommand "Restore solution" {
    & dotnet restore $solution
}

Invoke-CheckedCommand "Build solution" {
    & dotnet build $solution @commonArgs
}

Invoke-CheckedCommand "Test solution" {
    & dotnet test $solution @commonArgs
}

if (-not $SkipRegressions -and (Test-Path $regressionScript)) {
    Invoke-CheckedCommand "Run regression corpus" {
        & powershell -NoProfile -ExecutionPolicy Bypass -File $regressionScript -Configuration $RegressionConfiguration
    }
}
elseif ($SkipRegressions) {
    Write-Host "Regression corpus skipped by request."
}
else {
    Write-Host "Regression script not found: $regressionScript"
}
