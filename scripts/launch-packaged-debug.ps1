param(
    [ValidateSet("x64", "x86", "ARM64")]
    [string]$Platform = "x64",

    [string]$Configuration = "Debug",

    [switch]$NoBuild
)

$ErrorActionPreference = "Stop"

$repoRoot = Split-Path -Parent $PSScriptRoot
$project = Join-Path $repoRoot "src\Kroira.App\Kroira.App.csproj"
$packageName = "SATIVAGENETICS.KROIRAIPTV"
$targetFramework = "net8.0-windows10.0.19041.0"
$runtime = switch ($Platform) {
    "x64" { "win-x64" }
    "x86" { "win-x86" }
    "ARM64" { "win-arm64" }
}

$outputDir = Join-Path $repoRoot "src\Kroira.App\bin\$Platform\$Configuration\$targetFramework\$runtime"
$appxDir = Join-Path $outputDir "AppX"
$manifest = Join-Path $appxDir "AppxManifest.xml"

if (-not $NoBuild) {
    dotnet build $project -c $Configuration -p:Platform=$Platform
}

if (-not (Test-Path $appxDir)) {
    New-Item -ItemType Directory -Path $appxDir | Out-Null
}

Get-Process Kroira.App -ErrorAction SilentlyContinue | Stop-Process -Force
Start-Sleep -Milliseconds 300

$items = @(
    "Kroira.App.dll",
    "Kroira.App.exe",
    "Kroira.App.deps.json",
    "Kroira.App.runtimeconfig.json",
    "resources.pri",
    "AppxManifest.xml",
    "App.xbf",
    "MainWindow.xbf",
    "Controls",
    "Styles",
    "Views"
)

foreach ($item in $items) {
    $source = Join-Path $outputDir $item
    $destination = Join-Path $appxDir $item

    if (Test-Path $source) {
        try {
            Copy-Item -LiteralPath $source -Destination $destination -Recurse -Force
        }
        catch {
            if ($item -eq "Kroira.App.exe" -and (Test-Path $destination)) {
                Write-Warning "Kroira.App.exe is locked; reusing the existing packaged debug apphost."
            }
            else {
                throw
            }
        }
    }
}

if (-not (Test-Path $manifest)) {
    throw "Packaged debug manifest was not found at $manifest"
}

$registeredPackage = Get-AppxPackage -Name $packageName
$registeredLocation = if ($null -ne $registeredPackage) { [System.IO.Path]::GetFullPath($registeredPackage.InstallLocation) } else { $null }
$debugLocation = [System.IO.Path]::GetFullPath($appxDir)

if ($null -ne $registeredPackage -and $registeredLocation -eq $debugLocation) {
    Write-Host "Using existing packaged debug registration: $debugLocation"
}
else {
    if ($null -ne $registeredPackage) {
        Write-Host "Removing existing current-user registration: $($registeredPackage.PackageFullName)"
        Remove-AppxPackage -Package $registeredPackage.PackageFullName
    }

    Add-AppxPackage -Register $manifest -ForceApplicationShutdown
}

$package = Get-AppxPackage -Name $packageName
if ($null -eq $package) {
    throw "Kroira package registration failed."
}

Start-Process "shell:AppsFolder\$($package.PackageFamilyName)!App"

Write-Host "Launched packaged debug app: $($package.PackageFamilyName)!App"
Write-Host "Do not smoke-test this project by launching $outputDir\Kroira.App.exe directly; this project is MSIX-packaged."
