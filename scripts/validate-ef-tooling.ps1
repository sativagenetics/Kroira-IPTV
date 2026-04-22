param(
    [string]$Project = "src\\Kroira.App\\Kroira.App.csproj",
    [string]$StartupProject = "src\\Kroira.App\\Kroira.App.csproj",
    [string]$Context = "AppDbContext",
    [switch]$GenerateScript
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

$repoRoot = Resolve-Path (Join-Path $PSScriptRoot "..")
$migrationsDir = Join-Path $repoRoot "src\\Kroira.App\\Migrations"

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

Push-Location $repoRoot
try {
    $migrationFiles = Get-ChildItem $migrationsDir -File |
        Where-Object { $_.Name -match '^\d{14}_.+\.cs$' -and $_.Name -notlike '*.Designer.cs' } |
        Sort-Object Name

    $missingDesigners = foreach ($migrationFile in $migrationFiles) {
        $designerPath = Join-Path $migrationsDir ($migrationFile.BaseName + ".Designer.cs")
        if (-not (Test-Path $designerPath)) {
            $migrationFile.Name
        }
    }

    if ($missingDesigners) {
        throw ("Missing migration designer files:`n" + ($missingDesigners -join "`n"))
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
