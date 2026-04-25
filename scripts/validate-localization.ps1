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

    return @(Read-ResourceData $Path | ForEach-Object { $_.Key } | Sort-Object -Unique)
}

function Read-ResourceData {
    param([string]$Path)

    [xml]$xml = Get-Content -LiteralPath $Path -Raw -Encoding UTF8
    return @($xml.root.data | ForEach-Object {
        $valueNode = $_.SelectSingleNode('value')
        [pscustomobject]@{
            Key = $_.name
            Value = if ($null -eq $valueNode) { [string]::Empty } else { $valueNode.InnerText }
        }
    })
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
    $lookupPattern = '(?:LocalizedStrings\.(?:Get|GetOrDefault|TryGet|Format)|\b[LF])\(\s*"([^"]+)"'
    Get-ChildItem -LiteralPath $Root -Recurse -File |
        Where-Object { $_.Extension -eq '.cs' -and $_.FullName -notmatch '\\(bin|obj|AppPackages)\\' } |
        ForEach-Object {
            $text = Get-Content -LiteralPath $_.FullName -Raw
            foreach ($match in [regex]::Matches($text, $lookupPattern)) {
                $key = $match.Groups[1].Value
                if ($key -match '^[A-Za-z][A-Za-z0-9_]*(?:[._][A-Za-z0-9_]+)*$') {
                    [void]$refs.Add($key)
                }
            }
        }

    return @($refs)
}

function Get-DottedCodeResourceKeyLiterals {
    param(
        [string]$Root,
        [System.Collections.Generic.HashSet[string]]$ResourceKeySet
    )

    $warnings = New-Object System.Collections.Generic.List[string]
    Get-ChildItem -LiteralPath $Root -Recurse -File |
        Where-Object { $_.Extension -eq '.cs' -and $_.FullName -notmatch '\\(bin|obj|AppPackages|Migrations)\\' } |
        ForEach-Object {
            $path = $_.FullName
            $lines = Get-Content -LiteralPath $path
            for ($i = 0; $i -lt $lines.Count; $i++) {
                foreach ($match in [regex]::Matches($lines[$i], '"([^"]+)"')) {
                    $value = $match.Groups[1].Value
                    if ($value.Contains('.') -and $ResourceKeySet.Contains($value)) {
                        $relative = Resolve-Path -LiteralPath $path -Relative
                        $warnings.Add("${relative}:$($i + 1): ""$value""")
                    }
                }
            }
        }

    return @($warnings)
}

function Test-MojibakeValue {
    param([string]$Value)

    if ([string]::IsNullOrEmpty($Value)) {
        return $false
    }

    $patterns = @(
        '[\u0080-\u009F\uFFFD]',
        '\u00EF\u00BF\u00BD',
        '\u00C3[\u0080-\u00BF\u0192\u201A-\u2122]',
        '\u00C2[\u0080-\u00BF]',
        '\u00E2[\u0080-\u009F\u2018-\u2026]',
        '\u00E0[\u0080-\u00BF]',
        '\u00E3[\u0080-\u00BF]',
        '\u00EC[\u0080-\u00BF]',
        '\u00ED[\u0080-\u00BF]',
        '\u00D8[\u0080-\u00BF]',
        '\u00D9[\u0080-\u00BF]'
    )

    foreach ($pattern in $patterns) {
        if ($Value -match $pattern) {
            return $true
        }
    }

    if ($Value -match '\?{2,}' -or
        $Value -match '(?<=\p{L})\?(?=\p{L})' -or
        $Value -match '^\?(?=\p{L})') {
        return $true
    }

    return $false
}

function Get-PlaceholderSignature {
    param([string]$Value)

    $indexes = New-Object 'System.Collections.Generic.SortedSet[int]'
    foreach ($match in [regex]::Matches($Value, '\{([0-9]+)(?:[^{}]*)\}')) {
        [void]$indexes.Add([int]$match.Groups[1].Value)
    }

    return ($indexes | ForEach-Object { $_.ToString([Globalization.CultureInfo]::InvariantCulture) }) -join ','
}

function Get-ResourceValueFailures {
    param(
        [string]$Locale,
        [string]$Path
    )

    $failures = New-Object System.Collections.Generic.List[string]
    $rawKeyPattern = '^(Language|Settings|App|Sources|Player|General|Home|Browse|Discovery|Movies|Series|Channels|Favorites|ContinueWatching|PlayableInspection|SourceLifecycle|EpgCoverage|Submission)[._][A-Za-z0-9_.]+$'

    foreach ($entry in Read-ResourceData $Path) {
        if (Test-MojibakeValue $entry.Value) {
            $failures.Add("${Locale}: $($entry.Key) contains likely mojibake")
        }

        if ([string]::Equals($entry.Key, $entry.Value, [StringComparison]::Ordinal)) {
            $failures.Add("${Locale}: $($entry.Key) value equals its resource key")
        }

        if ($entry.Value -match $rawKeyPattern) {
            $failures.Add("${Locale}: $($entry.Key) value looks like a raw resource key: $($entry.Value)")
        }
    }

    return @($failures)
}

function Get-PlaceholderFailures {
    param(
        [string]$Locale,
        [string]$Path,
        [hashtable]$BaselinePlaceholders
    )

    $failures = New-Object System.Collections.Generic.List[string]
    foreach ($entry in Read-ResourceData $Path) {
        if (-not $BaselinePlaceholders.ContainsKey($entry.Key)) {
            continue
        }

        $expected = $BaselinePlaceholders[$entry.Key]
        $actual = Get-PlaceholderSignature $entry.Value
        if ($expected -ne $actual) {
            $failures.Add("${Locale}: $($entry.Key) placeholders expected {$expected}, found {$actual}")
        }
    }

    return @($failures)
}

function Add-LimitedFailureList {
    param(
        [System.Collections.Generic.List[string]]$Failures,
        [string]$Title,
        [object[]]$Items,
        [int]$Limit = 60
    )

    if ($Items.Count -eq 0) {
        return
    }

    $sample = @($Items | Select-Object -First $Limit)
    if ($Items.Count -gt $Limit) {
        $sample += "... $($Items.Count - $Limit) more"
    }

    $Failures.Add("${Title}: $($sample -join '; ')")
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

    if ($Line -match '\b(?:L|F|LocalizedStrings\.(?:Get|GetOrDefault|TryGet|Format))\("' -or
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
    $baselinePlaceholders = @{}
    foreach ($entry in Read-ResourceData $baselinePath) {
        $baselinePlaceholders[$entry.Key] = Get-PlaceholderSignature $entry.Value
    }

    $resourceValueFailures = New-Object System.Collections.Generic.List[string]
    $placeholderFailures = New-Object System.Collections.Generic.List[string]

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

        foreach ($failure in Get-ResourceValueFailures $locale $path) {
            $resourceValueFailures.Add($failure)
        }

        foreach ($failure in Get-PlaceholderFailures $locale $path $baselinePlaceholders) {
            $placeholderFailures.Add($failure)
        }
    }

    Add-LimitedFailureList $failures 'Resource value validation failures' @($resourceValueFailures)
    Add-LimitedFailureList $failures 'Resource placeholder validation failures' @($placeholderFailures)

    $xamlRefs = @(Get-XamlResourceReferences $ProjectRoot)
    $codeRefs = @(Get-CodeResourceReferences $ProjectRoot)
    $dottedCodeRefs = @($codeRefs | Where-Object { $_ -match '\.' } | Sort-Object -Unique)
    if ($dottedCodeRefs.Count -gt 0) {
        $failures.Add("C# ResourceLoader lookups must use flat key names: $($dottedCodeRefs -join ', ')")
    }

    $dottedCodeLiterals = @(Get-DottedCodeResourceKeyLiterals $ProjectRoot $baselineSet)
    Add-LimitedFailureList $failures 'C# raw dotted resource key literals' $dottedCodeLiterals

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
