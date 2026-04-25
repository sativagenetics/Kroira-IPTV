param(
    [string]$ProjectRoot = (Join-Path $PSScriptRoot '..\src\Kroira.App'),
    [switch]$ScanViewModels,
    [switch]$FailOnHardCoded
)

$ErrorActionPreference = 'Stop'

$requiredLocales = @(
    'en-US',
    'tr-TR',
    'zh-Hans',
    'es-ES',
    'ar-SA',
    'fr-FR',
    'de-DE',
    'pt-BR',
    'hi-IN',
    'ja-JP',
    'ko-KR'
)

function Read-ResourceKeys {
    param([string]$Path)

    [xml]$xml = Get-Content -LiteralPath $Path -Raw
    return @($xml.root.data | ForEach-Object { $_.name } | Sort-Object -Unique)
}

function Get-XamlResourceReferences {
    param([string]$Root)

    $refs = New-Object 'System.Collections.Generic.SortedSet[string]'
    $attrPattern = '(Content|Text|Header|PlaceholderText|Title|Message|PrimaryButtonText|SecondaryButtonText|CloseButtonText|RetryActionLabel|ToolTipService\.ToolTip|AutomationProperties\.Name)\s*=\s*"([^"]*)"'

    Get-ChildItem -LiteralPath $Root -Recurse -File |
        Where-Object { $_.Extension -eq '.xaml' -and $_.FullName -notmatch '\\(bin|obj|AppPackages)\\' } |
        ForEach-Object {
            $text = Get-Content -LiteralPath $_.FullName -Raw
            foreach ($tag in [regex]::Matches($text, '<[A-Za-z0-9_:.]+[\s\S]*?>')) {
                $attrs = $tag.Value
                $uid = [regex]::Match($attrs, 'x:Uid\s*=\s*"([^"]+)"')
                if (-not $uid.Success) {
                    continue
                }

                foreach ($match in [regex]::Matches($attrs, $attrPattern)) {
                    $value = $match.Groups[2].Value
                    if ([string]::IsNullOrWhiteSpace($value) -or $value.TrimStart().StartsWith('{')) {
                        continue
                    }

                    $propertyName = $match.Groups[1].Value
                    if ($propertyName -eq 'AutomationProperties.Name') {
                        $propertyName = '[using:Microsoft.UI.Xaml.Automation]AutomationProperties.Name'
                    }

                    [void]$refs.Add("$($uid.Groups[1].Value).$propertyName")
                }
            }
        }

    return @($refs)
}

function Get-CodeResourceReferences {
    param([string]$Root)

    $refs = New-Object 'System.Collections.Generic.SortedSet[string]'
    Get-ChildItem -LiteralPath $Root -Recurse -File |
        Where-Object { $_.Extension -eq '.cs' -and $_.FullName -notmatch '\\(bin|obj|AppPackages)\\' } |
        ForEach-Object {
            $text = Get-Content -LiteralPath $_.FullName -Raw
            foreach ($match in [regex]::Matches($text, '(?:LocalizedStrings\.(?:Get|Format)|\b[LF])\("([A-Za-z][A-Za-z0-9]*(?:\.[A-Za-z0-9]+)+)"')) {
                [void]$refs.Add($match.Groups[1].Value)
            }
        }

    return @($refs)
}

function Get-ManifestResourceReferences {
    param([string]$Root)

    $manifestPath = Join-Path $Root 'Package.appxmanifest'
    if (-not (Test-Path -LiteralPath $manifestPath)) {
        return @()
    }

    $refs = @()
    $text = Get-Content -LiteralPath $manifestPath -Raw
    foreach ($match in [regex]::Matches($text, 'ms-resource:(?<raw>[^"\s<>]+)')) {
        $raw = $match.Groups['raw'].Value.Trim()
        if ([string]::IsNullOrWhiteSpace($raw)) {
            continue
        }

        $key = $raw.TrimStart('/')
        if ($key.StartsWith('Resources/', [StringComparison]::OrdinalIgnoreCase)) {
            $key = $key.Substring('Resources/'.Length)
        }

        $lastSlash = $key.LastIndexOf('/')
        if ($lastSlash -ge 0) {
            $key = $key.Substring($lastSlash + 1)
        }

        if ([string]::IsNullOrWhiteSpace($key)) {
            continue
        }

        $refs += [pscustomobject]@{
            Raw = $raw
            Key = $key
        }
    }

    return $refs
}

function Get-HardCodedXamlWarnings {
    param([string]$Root)

    $warnings = New-Object System.Collections.Generic.List[string]
    $visiblePattern = '(Content|Text|Header|PlaceholderText|Title|Message|PrimaryButtonText|SecondaryButtonText|CloseButtonText|RetryActionLabel|ToolTipService\.ToolTip|AutomationProperties\.Name)\s*=\s*"([^"{][^"]*[A-Za-z][^"]*)"'
    $allowedLiteralPattern = '^(KROIRA|IPTV STUDIO|IPTV|EPG|M3U|M3U8|Xtream|Stalker|VOD|TMDb|IMDb|XMLTV|MAC|URL|URI|FPS|DPAPI|DRM|A/V|PIP)$'
    Get-ChildItem -LiteralPath $Root -Recurse -File |
        Where-Object { $_.Extension -eq '.xaml' -and $_.FullName -notmatch '\\(bin|obj|AppPackages|Strings)\\' } |
        ForEach-Object {
            $path = $_.FullName
            $text = Get-Content -LiteralPath $path -Raw
            foreach ($tag in [regex]::Matches($text, '<[A-Za-z0-9_:.]+[\s\S]*?>')) {
                $tagText = $tag.Value
                if ($tagText -match 'x:Uid=' -or $tagText -match '^\s*<Setter\b') {
                    continue
                }

                foreach ($match in [regex]::Matches($tagText, $visiblePattern)) {
                    $value = $match.Groups[2].Value.Trim()
                    if ($value -match $allowedLiteralPattern -or
                        $value -match '^(http|https|ms-appx|Segoe|[A-Z0-9_./:+-]+)$') {
                        continue
                    }

                    $lineNumber = ($text.Substring(0, $tag.Index) -split "`n").Count
                    $relative = Resolve-Path -LiteralPath $path -Relative
                    $warnings.Add("${relative}:$($lineNumber): $($match.Groups[1].Value)=""$value""")
                }
            }
        }

    return @($warnings)
}

function Get-LegacyWinUiResourceKeys {
    param([string]$StringsRoot)

    $legacyKeys = New-Object System.Collections.Generic.List[string]
    Get-ChildItem -LiteralPath $StringsRoot -Recurse -File -Filter Resources.resw |
        ForEach-Object {
            $path = $_.FullName
            foreach ($key in Read-ResourceKeys $path) {
                if ($key -match '\[using:Windows\.UI\.Xaml\.Automation\]AutomationProperties\.Name') {
                    $relative = Resolve-Path -LiteralPath $path -Relative
                    $legacyKeys.Add("${relative}: $key")
                }
            }
        }

    return @($legacyKeys)
}

function Test-CodeFileNeedsVisibleStringScan {
    param([string]$Path)

    if ($Path -match '\\(Views|Controls)\\') {
        return $true
    }

    if ($ScanViewModels -and $Path -match '\\ViewModels\\') {
        return $true
    }

    return [System.IO.Path]::GetFileName($Path) -in @(
        'App.xaml.cs',
        'AppAppearanceService.cs',
        'AppSubmissionInfo.cs',
        'EpgCoverageReportService.cs',
        'PlayableItemInspectionService.cs',
        'SourceGuidanceService.cs',
        'SourceLifecycleService.cs'
    )
}

function Test-IgnoredCodeLiteral {
    param(
        [string]$Line,
        [string]$Value
    )

    if ([string]::IsNullOrWhiteSpace($Value)) {
        return $true
    }

    if ($Value -match '^[A-Za-z][A-Za-z0-9]*(\.[A-Za-z0-9]+)+$') {
        return $true
    }

    if ($Value -match '^(Kroira|Kroira PiP|KROIRA|KROIRA IPTV|IPTV|EPG|M3U|M3U8|Xtream|Stalker|VOD|TMDb|IMDb|XMLTV|MAC|URL|URI|FPS|DPAPI|DRM|UTC|Error|Healthy|Good|off|stale|outdated|companion|Portal MAC|MOVIES UI|SERIES UI|APP [0-9A-Z: ]+|(M3U|Stalker|Xtream) Source)$') {
        return $true
    }

    if ($Value -match '^https?://proxy-host:port or socks5://proxy-host:port$') {
        return $true
    }

    if ($Value -match '^(http|https|ms-|sqlite|SELECT|INSERT|UPDATE|DELETE|Data Source=|#[A-Za-z0-9:_-]+|[A-Z0-9_./:+-]+)$') {
        return $true
    }

    if ($Value -match '[\\\^\$\[\]\{\}\?\*]' -or $Value -match '^\W+$') {
        return $true
    }

    if ($Line -match '\b(?:L|F|LocalizedStrings\.(?:Get|Format))\("' -or
        $Line -match '\$"|Log|Logger|SafeAppendLog|SafeLogException|RuntimeEventLogger|RunFatalStartupStep|RunRecoverableStartupStep|CancelStartup|ChooseMovieVariantAsync|Replace\(|Debug|nameof|typeof|Path\.|CommandText|Environment\.|Resource|Key|Uri|Url|StreamUrl|Password|Username|Email|Regex|Guid|DbSet|Migration|Migrations|Telemetry|AppendAllText|DateTime|TimeSpan|CancellationToken|PropertyMetadata|DependencyProperty|FontWeights|Brush|Color|Glyph|ContentId|SourceId|ProfileId|Exception\)|Json|Serialize|Deserialize|TryParse|Parse|Split|Join|StartsWith|EndsWith|Contains|Equals|GetString|GetProperty|GetValue|Normalize') {
        return $true
    }

    return $false
}

function Get-HardCodedCodeWarnings {
    param([string]$Root)

    $warnings = New-Object System.Collections.Generic.List[string]
    Get-ChildItem -LiteralPath $Root -Recurse -File |
        Where-Object { $_.Extension -eq '.cs' -and $_.FullName -notmatch '\\(bin|obj|AppPackages|Migrations|Strings|Data|Models|Properties)\\' -and (Test-CodeFileNeedsVisibleStringScan $_.FullName) } |
        ForEach-Object {
            $path = $_.FullName
            $lines = Get-Content -LiteralPath $path
            for ($i = 0; $i -lt $lines.Count; $i++) {
                $line = $lines[$i]
                foreach ($match in [regex]::Matches($line, '"([^"]*[A-Za-z][^"]{2,})"')) {
                    $value = $match.Groups[1].Value.Trim()
                    if (Test-IgnoredCodeLiteral $line $value) {
                        continue
                    }

                    if ($value -match '\{[0-9]') {
                        continue
                    }

                    $relative = Resolve-Path -LiteralPath $path -Relative
                    $warnings.Add("${relative}:$($i + 1): ""$value""")
                }
            }
        }

    return @($warnings)
}

$stringsRoot = Join-Path $ProjectRoot 'Strings'
$failures = New-Object System.Collections.Generic.List[string]

foreach ($locale in $requiredLocales) {
    $path = Join-Path $stringsRoot "$locale\Resources.resw"
    if (-not (Test-Path -LiteralPath $path)) {
        $failures.Add("Missing resource file: $path")
    }
}

if ($failures.Count -eq 0) {
    $baselinePath = Join-Path $stringsRoot 'en-US\Resources.resw'
    $baseline = @(Read-ResourceKeys $baselinePath)
    $baselineSet = [System.Collections.Generic.HashSet[string]]::new([string[]]$baseline)

    foreach ($locale in $requiredLocales) {
        $path = Join-Path $stringsRoot "$locale\Resources.resw"
        $keys = @(Read-ResourceKeys $path)
        $keySet = [System.Collections.Generic.HashSet[string]]::new([string[]]$keys)

        $missing = @($baseline | Where-Object { -not $keySet.Contains($_) })
        $extra = @($keys | Where-Object { -not $baselineSet.Contains($_) })
        if ($missing.Count -gt 0) {
            $failures.Add("$locale missing keys: $($missing -join ', ')")
        }
        if ($extra.Count -gt 0) {
            $failures.Add("$locale extra keys: $($extra -join ', ')")
        }
    }

    $xamlRefs = @(Get-XamlResourceReferences $ProjectRoot)
    $codeRefs = @(Get-CodeResourceReferences $ProjectRoot)
    $manifestRefs = @(Get-ManifestResourceReferences $ProjectRoot)
    $dottedManifestRefs = @($manifestRefs | Where-Object { $_.Raw -match '\.' } | ForEach-Object { $_.Raw } | Sort-Object -Unique)
    if ($dottedManifestRefs.Count -gt 0) {
        $failures.Add("Manifest ms-resource references should use flat key names: $($dottedManifestRefs -join ', ')")
    }

    $manifestKeys = @($manifestRefs | ForEach-Object { $_.Key })
    $allRefs = @($xamlRefs + $codeRefs + $manifestKeys | Sort-Object -Unique)
    $missingRefs = @($allRefs | Where-Object { -not $baselineSet.Contains($_) })
    if ($missingRefs.Count -gt 0) {
        $failures.Add("Resource references missing from en-US: $($missingRefs -join ', ')")
    }

    $legacyWinUiKeys = @(Get-LegacyWinUiResourceKeys $stringsRoot)
    if ($legacyWinUiKeys.Count -gt 0) {
        $failures.Add("WinUI 3 AutomationProperties resources must use Microsoft.UI.Xaml.Automation, not Windows.UI.Xaml.Automation: $($legacyWinUiKeys -join '; ')")
    }
}

$xamlWarnings = @(Get-HardCodedXamlWarnings $ProjectRoot)
$codeWarnings = @(Get-HardCodedCodeWarnings $ProjectRoot)

if ($failures.Count -gt 0) {
    Write-Host 'Localization validation failed:'
    $failures | ForEach-Object { Write-Host "  - $_" }
    exit 1
}

Write-Host "Localization key parity OK for $($requiredLocales.Count) locales."
Write-Host "XAML hard-coded visible string warnings: $($xamlWarnings.Count)"
$xamlWarnings | Select-Object -First 80 | ForEach-Object { Write-Host "  $_" }
Write-Host "C# hard-coded visible string warnings: $($codeWarnings.Count)"
$codeWarnings | Select-Object -First 80 | ForEach-Object { Write-Host "  $_" }

if ($FailOnHardCoded -and (($xamlWarnings.Count + $codeWarnings.Count) -gt 0)) {
    exit 2
}
