param(
    [string]$Configuration = "Debug",
    [string]$Platform = "x64",
    [string]$OutputRoot,
    [switch]$SkipBuild,
    [switch]$SkipDeploy,
    [switch]$SkipPlayer
)

$ErrorActionPreference = "Stop"

$repoRoot = Resolve-Path (Join-Path $PSScriptRoot "..")
$projectPath = Join-Path $repoRoot "src\Kroira.App\Kroira.App.csproj"
$buildOutputRoot = Join-Path $repoRoot "src\Kroira.App\bin\$Platform\$Configuration\net8.0-windows10.0.19041.0\win-$Platform"
$stagedPackageRoot = Join-Path $buildOutputRoot "AppX"
$manifestPath = Join-Path $stagedPackageRoot "AppxManifest.xml"
$packageName = "SATIVAGENETICS.KROIRAIPTV"
$appId = "App"
$timestamp = Get-Date -Format "yyyyMMdd-HHmmss"
if ([string]::IsNullOrWhiteSpace($OutputRoot)) {
    $OutputRoot = Join-Path $repoRoot "artifacts\ui-screenshots"
}
elseif (-not [System.IO.Path]::IsPathRooted($OutputRoot)) {
    $OutputRoot = Join-Path $repoRoot $OutputRoot
}

$shotRoot = Join-Path $OutputRoot $timestamp
$startupLogPath = Join-Path $env:LOCALAPPDATA "Kroira\startup-log.txt"
$startupLogStartLength = if (Test-Path $startupLogPath) { (Get-Item $startupLogPath).Length } else { 0 }

New-Item -ItemType Directory -Force -Path $shotRoot | Out-Null

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

if (-not $SkipBuild) {
    dotnet build (Join-Path $repoRoot "Kroira.sln") -c $Configuration -p:Platform=$Platform
}

function Sync-CurrentBuildToStagedPackage {
    if (-not (Test-Path $buildOutputRoot)) {
        throw "Build output not found: $buildOutputRoot"
    }

    New-Item -ItemType Directory -Force -Path $stagedPackageRoot | Out-Null

    Get-ChildItem -Path $buildOutputRoot -File | ForEach-Object {
        Copy-Item -LiteralPath $_.FullName -Destination $stagedPackageRoot -Force
    }

    Get-ChildItem -Path $buildOutputRoot -Directory |
        Where-Object { $_.Name -ne "AppX" -and $_.Name -ne "publish" } |
        ForEach-Object {
            Copy-Item -LiteralPath $_.FullName -Destination $stagedPackageRoot -Recurse -Force
    }
}

Get-Process Kroira.App -ErrorAction SilentlyContinue | Stop-Process -Force
Start-Sleep -Milliseconds 300

Sync-CurrentBuildToStagedPackage

if (-not (Test-Path $manifestPath)) {
    throw "Packaged AppX manifest not found: $manifestPath"
}

if (-not $SkipDeploy) {
    $existingPackage = Get-AppxPackage -Name $packageName | Select-Object -First 1
    $registeredLocation = if ($existingPackage) { [System.IO.Path]::GetFullPath($existingPackage.InstallLocation) } else { $null }
    $stagedLocation = [System.IO.Path]::GetFullPath($stagedPackageRoot)

    if (-not $existingPackage -or -not [string]::Equals($registeredLocation, $stagedLocation, [System.StringComparison]::OrdinalIgnoreCase)) {
        Add-AppxPackage -Register $manifestPath -DisableDevelopmentMode -ForceUpdateFromAnyVersion -ForceApplicationShutdown
    }
    else {
        Write-Host "Using existing development registration at $stagedPackageRoot"
    }
}

$package = Get-AppxPackage -Name $packageName | Select-Object -First 1
if (-not $package) {
    throw "Package is not deployed: $packageName"
}

$aumid = "$($package.PackageFamilyName)!$appId"

Add-Type -AssemblyName System.Drawing
Add-Type -AssemblyName UIAutomationClient
Add-Type -AssemblyName UIAutomationTypes
Add-Type -TypeDefinition @"
using System;
using System.Runtime.InteropServices;

public static class KroiraVisualSmokeNative
{
    [DllImport("user32.dll")] public static extern bool SetForegroundWindow(IntPtr hWnd);
    [DllImport("user32.dll")] public static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);
    [DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr hWnd, out RECT rect);
    [DllImport("user32.dll")] public static extern bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter, int X, int Y, int cx, int cy, uint uFlags);
    [DllImport("user32.dll")] public static extern bool SetCursorPos(int X, int Y);
    public static readonly IntPtr HWND_TOPMOST = new IntPtr(-1);
    public static readonly IntPtr HWND_NOTOPMOST = new IntPtr(-2);
    public const uint SWP_NOMOVE = 0x0002;
    public const uint SWP_NOSIZE = 0x0001;
    public const uint SWP_SHOWWINDOW = 0x0040;

    public struct RECT
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }
}
"@

function Wait-KroiraWindow {
    param([int]$TimeoutSeconds = 30)

    $deadline = (Get-Date).AddSeconds($TimeoutSeconds)
    do {
        $proc = Get-Process Kroira.App -ErrorAction SilentlyContinue |
            Where-Object { $_.MainWindowHandle -ne 0 } |
            Select-Object -First 1
        if ($proc) {
            return $proc
        }

        Start-Sleep -Milliseconds 250
    } while ((Get-Date) -lt $deadline)

    throw "KROIRA process did not expose a main window within $TimeoutSeconds seconds."
}

function Focus-KroiraWindow {
    param([Parameter(Mandatory)]$Process)

    [KroiraVisualSmokeNative]::ShowWindow($Process.MainWindowHandle, 3) | Out-Null
    [KroiraVisualSmokeNative]::SetWindowPos(
        $Process.MainWindowHandle,
        [KroiraVisualSmokeNative]::HWND_TOPMOST,
        0,
        0,
        0,
        0,
        ([KroiraVisualSmokeNative]::SWP_NOMOVE -bor [KroiraVisualSmokeNative]::SWP_NOSIZE -bor [KroiraVisualSmokeNative]::SWP_SHOWWINDOW)) | Out-Null
    Start-Sleep -Milliseconds 250
    [KroiraVisualSmokeNative]::SetForegroundWindow($Process.MainWindowHandle) | Out-Null
    Start-Sleep -Milliseconds 250
    [KroiraVisualSmokeNative]::SetWindowPos(
        $Process.MainWindowHandle,
        [KroiraVisualSmokeNative]::HWND_NOTOPMOST,
        0,
        0,
        0,
        0,
        ([KroiraVisualSmokeNative]::SWP_NOMOVE -bor [KroiraVisualSmokeNative]::SWP_NOSIZE -bor [KroiraVisualSmokeNative]::SWP_SHOWWINDOW)) | Out-Null
}

function Get-KroiraAutomationRoot {
    param([Parameter(Mandatory)]$Process)
    [System.Windows.Automation.AutomationElement]::FromHandle($Process.MainWindowHandle)
}

function Find-ElementByName {
    param(
        [Parameter(Mandatory)]$Root,
        [Parameter(Mandatory)][string]$Name
    )

    $condition = [System.Windows.Automation.PropertyCondition]::new(
        [System.Windows.Automation.AutomationElement]::NameProperty,
        $Name)
    $Root.FindFirst([System.Windows.Automation.TreeScope]::Descendants, $condition)
}

function Find-ElementByAutomationId {
    param(
        [Parameter(Mandatory)]$Root,
        [Parameter(Mandatory)][string]$AutomationId
    )

    $condition = [System.Windows.Automation.PropertyCondition]::new(
        [System.Windows.Automation.AutomationElement]::AutomationIdProperty,
        $AutomationId)
    $Root.FindFirst([System.Windows.Automation.TreeScope]::Descendants, $condition)
}

function Invoke-Or-SelectElement {
    param([Parameter(Mandatory)]$Element)

    $selectionPattern = $null
    if ($Element.TryGetCurrentPattern([System.Windows.Automation.SelectionItemPattern]::Pattern, [ref]$selectionPattern)) {
        $selectionPattern.Select()
        return
    }

    $invokePattern = $null
    if ($Element.TryGetCurrentPattern([System.Windows.Automation.InvokePattern]::Pattern, [ref]$invokePattern)) {
        $invokePattern.Invoke()
        return
    }

    throw "Element does not support SelectionItemPattern or InvokePattern: $($Element.Current.Name)"
}

function Navigate-Kroira {
    param(
        [Parameter(Mandatory)]$Process,
        [Parameter(Mandatory)][string]$Name
    )

    Focus-KroiraWindow -Process $Process
    $root = Get-KroiraAutomationRoot -Process $Process
    $element = Find-ElementByName -Root $root -Name $Name
    if (-not $element) {
        throw "Navigation target not found: $Name"
    }

    Invoke-Or-SelectElement -Element $element
    Start-Sleep -Milliseconds 1800
}

function Capture-KroiraWindow {
    param(
        [Parameter(Mandatory)]$Process,
        [Parameter(Mandatory)][string]$Name
    )

    Focus-KroiraWindow -Process $Process
    $rect = New-Object KroiraVisualSmokeNative+RECT
    [KroiraVisualSmokeNative]::GetWindowRect($Process.MainWindowHandle, [ref]$rect) | Out-Null
    $width = $rect.Right - $rect.Left
    $height = $rect.Bottom - $rect.Top
    if ($width -le 0 -or $height -le 0) {
        throw "Invalid KROIRA window bounds: $width x $height"
    }

    [KroiraVisualSmokeNative]::SetCursorPos($rect.Right - 40, $rect.Bottom - 40) | Out-Null
    Start-Sleep -Milliseconds 700

    $bitmap = New-Object System.Drawing.Bitmap $width, $height
    $graphics = [System.Drawing.Graphics]::FromImage($bitmap)
    try {
        $graphics.CopyFromScreen($rect.Left, $rect.Top, 0, 0, $bitmap.Size)
        $path = Join-Path $shotRoot ("{0}.png" -f $Name)
        $bitmap.Save($path, [System.Drawing.Imaging.ImageFormat]::Png)
        Write-Host "Captured $path"
    }
    finally {
        $graphics.Dispose()
        $bitmap.Dispose()
    }
}

Start-Process "shell:AppsFolder\$aumid"
$process = Wait-KroiraWindow
Start-Sleep -Seconds 3

$pages = @(
    @{ Nav = "Home"; Name = "01-home" },
    @{ Nav = "Movies"; Name = "02-movies" },
    @{ Nav = "Series"; Name = "03-series" },
    @{ Nav = "Live TV"; Name = "04-live-tv" },
    @{ Nav = "Continue Watching"; Name = "05-continue-watching" },
    @{ Nav = "Favorites"; Name = "06-favorites" },
    @{ Nav = "Sources"; Name = "07-sources" },
    @{ Nav = "Settings"; Name = "08-settings" },
    @{ Nav = "Profile"; Name = "09-profile" }
)

foreach ($page in $pages) {
    Navigate-Kroira -Process $process -Name $page.Nav
    Capture-KroiraWindow -Process $process -Name $page.Name
}

if (-not $SkipPlayer) {
    Navigate-Kroira -Process $process -Name "Home"
    $root = Get-KroiraAutomationRoot -Process $process
    $playButton = Find-ElementByAutomationId -Root $root -AutomationId "FeaturedPrimaryButton"
    if (-not $playButton) {
        $playButton = Find-ElementByAutomationId -Root $root -AutomationId "FeaturedPlayButton"
    }

    if ($playButton) {
        Invoke-Or-SelectElement -Element $playButton
        Start-Sleep -Seconds 6
        Capture-KroiraWindow -Process $process -Name "10-player"
    }
    else {
        Write-Warning "Player capture skipped: featured play button was not found."
    }
}

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

[pscustomobject]@{
    PackageFamilyName = $package.PackageFamilyName
    AppUserModelId = $aumid
    ManifestPath = $manifestPath
    ScreenshotFolder = $shotRoot
    BrowsePerformanceWarningCount = $browsePerformanceWarnings.Count
    BrowsePerformanceWarnings = @($browsePerformanceWarnings | Select-Object -First 20)
    CapturedAt = (Get-Date).ToString("o")
} | ConvertTo-Json | Set-Content -Path (Join-Path $shotRoot "visual-smoke.json") -Encoding UTF8

Write-Host "Screenshots: $shotRoot"
