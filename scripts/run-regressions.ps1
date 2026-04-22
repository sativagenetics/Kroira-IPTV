param(
    [Parameter(ValueFromRemainingArguments = $true)]
    [string[]]$RunnerArgs
)

$ErrorActionPreference = "Stop"

$repoRoot = Split-Path -Parent $PSScriptRoot
$project = Join-Path $repoRoot "tests\Kroira.Regressions\Kroira.Regressions.csproj"

if (-not (Test-Path $project)) {
    throw "Regression runner project not found at $project"
}

Write-Host "Running KROIRA regression corpus..."
& dotnet run --project $project --property:Platform=x64 -- @RunnerArgs
exit $LASTEXITCODE
