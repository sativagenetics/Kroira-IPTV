param(
    [string]$Configuration = "Release",
    [string]$CaseId = "",
    [switch]$UpdateBaselines
)

$ErrorActionPreference = "Stop"
Set-StrictMode -Version Latest

$repoRoot = Split-Path -Parent $PSScriptRoot
$solution = Join-Path $repoRoot "Kroira.sln"
$runnerProject = Join-Path $repoRoot "tests\Kroira.Regressions\Kroira.Regressions.csproj"
$artifactRoot = Join-Path $repoRoot "tests\Kroira.Regressions\artifacts"

if (-not (Test-Path $solution)) {
    throw "Solution not found at $solution"
}

if (-not (Test-Path $runnerProject)) {
    throw "Regression runner project not found at $runnerProject"
}

New-Item -ItemType Directory -Force -Path $artifactRoot | Out-Null
Get-ChildItem -Path $artifactRoot -Force -ErrorAction SilentlyContinue | Remove-Item -Force -Recurse

$commonBuildArgs = @(
    "-c", $Configuration,
    "-p:Platform=x64",
    "-p:AppxPackageSigningEnabled=false",
    "--nologo",
    "-v:minimal"
)

Write-Host "Building app solution..."
& dotnet build $solution @commonBuildArgs
if ($LASTEXITCODE -ne 0) {
    exit $LASTEXITCODE
}

# The harness builds Kroira.App with KroiraHeadlessHarness=true via project reference,
# so it needs its own build pass instead of reusing the packaged app output shape.
Write-Host "Building regression runner..."
& dotnet build $runnerProject @commonBuildArgs
if ($LASTEXITCODE -ne 0) {
    exit $LASTEXITCODE
}

$runnerArgs = @(
    "run",
    "--project", $runnerProject,
    "--no-build",
    "-c", $Configuration,
    "--property:Platform=x64",
    "--property:AppxPackageSigningEnabled=false",
    "--"
)

if ($CaseId) {
    $runnerArgs += @("--case", $CaseId)
}

if ($UpdateBaselines) {
    $runnerArgs += "--update"
}

Write-Host "Running deterministic regression corpus..."
& dotnet @runnerArgs
$runnerExitCode = $LASTEXITCODE

if ($UpdateBaselines) {
    $patchPath = Join-Path $artifactRoot "baseline-update.patch"
    $gitDiff = & git diff --no-ext-diff -- tests/Kroira.Regressions/Corpus
    if ($LASTEXITCODE -ne 0) {
        exit $LASTEXITCODE
    }

    if ($gitDiff) {
        $gitDiff | Out-File -FilePath $patchPath -Encoding utf8
        Write-Host "Baseline patch written to $patchPath"
    }
    else {
        "No baseline changes were produced." | Set-Content -Path $patchPath
        Write-Host "No baseline changes were produced."
    }
}

exit $runnerExitCode
