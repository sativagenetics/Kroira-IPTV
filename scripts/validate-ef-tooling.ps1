param(
    [string]$Project = "src\\Kroira.App\\Kroira.App.csproj",
    [string]$StartupProject = "src\\Kroira.App\\Kroira.App.csproj",
    [string]$Context = "AppDbContext",
    [switch]$GenerateScript,
    [switch]$ValidateAddRemove
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

$repoRoot = Resolve-Path (Join-Path $PSScriptRoot "..")
$migrationsDir = Join-Path $repoRoot "src\\Kroira.App\\Migrations"
$snapshotPath = Join-Path $migrationsDir "AppDbContextModelSnapshot.cs"

function Invoke-DotNetEf {
    param(
        [Parameter(Mandatory = $true)]
        [string[]]$Arguments
    )

    Write-Host ("dotnet " + ($Arguments -join " "))
    & dotnet @Arguments
    if ($LASTEXITCODE -ne 0) {
        throw "dotnet command failed with exit code $LASTEXITCODE"
    }
}

function Get-MigrationFiles {
    return Get-ChildItem $migrationsDir -Recurse -File |
        Where-Object { $_.Name -match '^\d{14}_.+\.cs$' -and $_.Name -notlike '*.Designer.cs' } |
        Sort-Object Name
}

function Get-NormalizedSnapshotContent {
    param(
        [Parameter(Mandatory = $true)]
        [string]$Path
    )

    $content = Get-Content $Path -Raw
    return [regex]::Replace($content, '\.ToTable\(\"([^\"]+)\", \(string\)null\);', '.ToTable("$1");')
}

function Normalize-SnapshotFormatting {
    param(
        [Parameter(Mandatory = $true)]
        [string]$Path
    )

    $content = Get-Content $Path -Raw
    $normalized = Get-NormalizedSnapshotContent -Path $Path
    if ($normalized -eq $content) {
        return
    }

    $utf8Bom = New-Object System.Text.UTF8Encoding($true)
    [System.IO.File]::WriteAllText((Resolve-Path $Path), $normalized, $utf8Bom)
}

Push-Location $repoRoot
try {
    $migrationFiles = Get-MigrationFiles

    $missingDesigners = foreach ($migrationFile in $migrationFiles) {
        $designerPath = Join-Path $migrationsDir ($migrationFile.BaseName + ".Designer.cs")
        if (-not (Test-Path $designerPath)) {
            $migrationFile.Name
        }
    }

    if ($missingDesigners) {
        throw ("Missing migration designer files:`n" + ($missingDesigners -join "`n"))
    }

    $headMigration = $migrationFiles | Select-Object -Last 1
    $currentTimestampPrefix = Get-Date -Format "yyyyMMddHHmmss"
    if ($headMigration) {
        Write-Host "Current migration head: $($headMigration.BaseName)"
        Write-Host "Current local timestamp prefix: $currentTimestampPrefix"
    }

    Invoke-DotNetEf @(
        "ef", "dbcontext", "info",
        "--project", $Project,
        "--startup-project", $StartupProject,
        "--context", $Context)

    Invoke-DotNetEf @(
        "ef", "migrations", "list",
        "--project", $Project,
        "--startup-project", $StartupProject,
        "--context", $Context)

    if ($ValidateAddRemove) {
        $baselineHead = if ($null -ne $headMigration) { $headMigration.BaseName } else { $null }
        $baselineSnapshotContent = Get-NormalizedSnapshotContent -Path $snapshotPath
        $probeName = "RcRemoveSafetyProbe$(Get-Date -Format 'HHmmss')"
        $probeOutputDir = "Migrations\\_RcRemoveSafetyProbe"

        Write-Host "Validating add/remove round-trip with probe migration '$probeName'."
        Invoke-DotNetEf @(
            "ef", "migrations", "add", $probeName,
            "--project", $Project,
            "--startup-project", $StartupProject,
            "--context", $Context,
            "--output-dir", $probeOutputDir)

        $postAddHead = (Get-MigrationFiles | Select-Object -Last 1).BaseName
        if ($postAddHead -notlike "*_$probeName") {
            throw "Probe migration did not become the latest head. Head after add: $postAddHead"
        }

        Invoke-DotNetEf @(
            "ef", "migrations", "remove",
            "--project", $Project,
            "--startup-project", $StartupProject,
            "--context", $Context,
            "--force")

        $postRemoveHead = (Get-MigrationFiles | Select-Object -Last 1).BaseName
        Normalize-SnapshotFormatting -Path $snapshotPath
        $postRemoveSnapshotContent = Get-NormalizedSnapshotContent -Path $snapshotPath
        if ($postRemoveHead -ne $baselineHead) {
            throw "Migration head changed after probe remove. Expected '$baselineHead' but found '$postRemoveHead'."
        }

        if ($postRemoveSnapshotContent -ne $baselineSnapshotContent) {
            throw "Model snapshot changed after probe remove."
        }

        $probeDirectoryPath = Join-Path $migrationsDir "_RcRemoveSafetyProbe"
        if ((Test-Path $probeDirectoryPath) -and -not (Get-ChildItem $probeDirectoryPath -Force | Select-Object -First 1)) {
            Remove-Item $probeDirectoryPath -Force
        }

        Write-Host "Add/remove round-trip validation passed."
    }

    if ($GenerateScript) {
        $artifactsDir = Join-Path $repoRoot "artifacts"
        $scriptOutputPath = Join-Path $artifactsDir "ef-tooling-validation.sql"
        if (-not (Test-Path $artifactsDir)) {
            New-Item -ItemType Directory -Path $artifactsDir | Out-Null
        }

        Invoke-DotNetEf @(
            "ef", "migrations", "script",
            "--project", $Project,
            "--startup-project", $StartupProject,
            "--context", $Context,
            "--output", $scriptOutputPath)

        Write-Host "Migration script written to $scriptOutputPath"
    }

    Write-Host "EF tooling validation passed."
}
finally {
    Pop-Location
}
