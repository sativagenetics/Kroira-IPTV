param(
    [Parameter(ValueFromRemainingArguments = $true)]
    [string[]]$RunnerArgs
)

$ErrorActionPreference = "Stop"

$repoRoot = Split-Path -Parent $PSScriptRoot
$project = Join-Path $repoRoot "tests\Kroira.Regressions\Kroira.Regressions.csproj"
$startupLogPath = Join-Path $env:LOCALAPPDATA "Kroira\startup-log.txt"
$startupLogStartLength = if (Test-Path $startupLogPath) { (Get-Item $startupLogPath).Length } else { 0 }

function Get-AppendedLogLines {
    param(
        [Parameter(Mandatory)][string]$Path,
        [long]$StartLength = 0
    )

    if (-not (Test-Path $Path)) {
        return @()
    }

    $stream = [System.IO.File]::Open($Path, [System.IO.FileMode]::Open, [System.IO.FileAccess]::Read, [System.IO.FileShare]::ReadWrite)
    try {
        if ($StartLength -gt 0 -and $StartLength -lt $stream.Length) {
            [void]$stream.Seek($StartLength, [System.IO.SeekOrigin]::Begin)
        }
        elseif ($StartLength -ge $stream.Length) {
            return @()
        }

        $reader = [System.IO.StreamReader]::new($stream)
        try {
            $text = $reader.ReadToEnd()
        }
        finally {
            $reader.Dispose()
        }
    }
    finally {
        $stream.Dispose()
    }

    if ([string]::IsNullOrWhiteSpace($text)) {
        return @()
    }

    return $text -split "\r?\n" | Where-Object { -not [string]::IsNullOrWhiteSpace($_) }
}

if (-not (Test-Path $project)) {
    throw "Regression runner project not found at $project"
}

Write-Host "Running KROIRA regression corpus..."
& dotnet run --project $project --property:Platform=x64 -- @RunnerArgs
$exitCode = $LASTEXITCODE

$browsePerformanceWarnings = @(
    Get-AppendedLogLines -Path $startupLogPath -StartLength $startupLogStartLength |
        Where-Object { $_ -match "PERF WARN" }
)
if ($browsePerformanceWarnings.Count -gt 0) {
    Write-Warning "Browse performance warnings detected: $($browsePerformanceWarnings.Count)"
    $browsePerformanceWarnings | Select-Object -First 20 | ForEach-Object { Write-Warning $_ }
}
else {
    Write-Host "Browse performance warnings: 0"
}

exit $exitCode
