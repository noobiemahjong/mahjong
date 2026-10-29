<#
.SYNOPSIS
  Repository document gate: rules G1-G12.
.DESCRIPTION
  Scans markdown files under -Root and prints one line per finding:
    <ERROR|WARN> <Gid> <relpath>:<line> <message>
  followed by a summary. The exit code is 1 only when -Fail is given and at
  least one ERROR was reported; WARN never changes the exit code.
.PARAMETER Root
  Directory to scan. Defaults to the repository root (two levels above this script).
.PARAMETER Fail
  Return exit code 1 when at least one ERROR was reported.
.EXAMPLE
  pwsh -File tools/doc-gate/doc-gate.ps1 -Fail
.EXAMPLE
  pwsh -File tools/doc-gate/doc-gate.ps1 -Root tools/doc-gate/tests/fixtures/bad-link/bad -Fail
#>
param(
    [string]$Root = '',
    [switch]$Fail
)

Set-StrictMode -Version 2.0
$ErrorActionPreference = 'Stop'

# Force UTF-8 on redirected stdout so CI logs and pipes decode non-ASCII paths.
try { [Console]::OutputEncoding = New-Object System.Text.UTF8Encoding($false) } catch { }

$script:Findings = New-Object System.Collections.Generic.List[object]
$script:RootFull = ''

# Metadata header prefixes used by G12. Expressed with code points so the
# source stays pure ASCII.
$script:MetaPrefixes = @(
    ([string][char]0x72B6 + [char]0x6001),
    ([string][char]0x4F9D + [char]0x8D56),
    ([string][char]0x5BF9 + [char]0x5E94 + ' master'),
    ([string][char]0x9A8C + [char]0x6536)
)

function Add-Finding {
    param(
        [string]$Level,
        [string]$Gid,
        [string]$Rel,
        [int]$Line = 0,
        [string]$Message = ''
    )
    if ($Line -lt 0) { $Line = 0 }
    [void]$script:Findings.Add([pscustomobject]@{
        Level = $Level
        Gid = $Gid
        Rel = $Rel
        Line = $Line
        Message = $Message
    })
}

function Test-IntegerValue {
    param($Value)
    if ($null -eq $Value) { return $false }
    if ($Value -is [int] -or $Value -is [long] -or $Value -is [int16] -or $Value -is [byte] -or $Value -is [uint32] -or $Value -is [uint64]) {
        return $true
    }
    return $false
}

function Test-RegistryPath {
    param([string]$Path)
    if ([string]::IsNullOrEmpty($Path)) { return $false }
    if ($Path -match '^[A-Za-z]:') { return $false }
    if ($Path.Contains('\')) { return $false }
    if ($Path.StartsWith('/')) { return $false }
    if ($Path.EndsWith('/')) { return $false }
    if ($Path -match '[*?\[\]{}]') { return $false }
    foreach ($seg in $Path.Split('/')) {
        if ($seg.Length -eq 0) { return $false }
        if ($seg -ceq '..') { return $false }
    }
    return $true
}

function Get-RelPath {
    param([string]$FullPath)
    $rel = $FullPath.Substring($script:RootFull.Length)
    $rel = $rel.TrimStart([char]'\', [char]'/')
    return $rel.Replace('\', '/')
}

function Test-ExcludedPath {
    param([string]$Rel)
    $parts = $Rel.Split('/')
    for ($i = 0; $i -lt ($parts.Length - 1); $i++) {
        foreach ($seg in @('.git', 'content', 'lib', 'bin', 'obj', 'build', 'addons')) {
            if ([string]::Equals($parts[$i], $seg, [System.StringComparison]::OrdinalIgnoreCase)) { return $true }
        }
    }
    if ($Rel.StartsWith('tools/doc-gate/tests/fixtures/', [System.StringComparison]::OrdinalIgnoreCase)) { return $true }
    return $false
}

function Read-BytesText {
    param([string]$FullPath)
    $bytes = [System.IO.File]::ReadAllBytes($FullPath)
    $strict = New-Object System.Text.UTF8Encoding($false, $true)
    $loose = New-Object System.Text.UTF8Encoding($false)
    $valid = $true
    $text = ''
    try {
        $text = $strict.GetString($bytes)
    } catch {
        $valid = $false
        $text = $loose.GetString($bytes)
    }
    if ($text.Length -gt 0 -and [int]$text[0] -eq 0xFEFF) {
        $text = $text.Substring(1)
    }
    return [pscustomobject]@{ Bytes = $bytes; Text = $text; ValidUtf8 = $valid }
}

function Hide-InlineCode {
    param([string]$Line)
    $chars = $Line.ToCharArray()
    $n = $chars.Length
    $i = 0
    $tick = [char]96
    while ($i -lt $n) {
        if ($chars[$i] -ne $tick) { $i++; continue }
        $start = $i
        $run = 0
        while ($i -lt $n -and $chars[$i] -eq $tick) { $run++; $i++ }
        $probe = $i
        $closeStart = -1
        $closeEnd = -1
        while ($probe -lt $n) {
            if ($chars[$probe] -eq $tick) {
                $r = 0
                $q = $probe
                while ($q -lt $n -and $chars[$q] -eq $tick) { $r++; $q++ }
                if ($r -eq $run) { $closeStart = $probe; $closeEnd = $q; break }
                $probe = $q
            } else {
                $probe++
            }
        }
        if ($closeStart -ge 0) {
            for ($m = $start; $m -lt $closeEnd; $m++) { $chars[$m] = [char]'x' }
            $i = $closeEnd
        }
    }
    return (-join $chars)
}

function Get-UnescapedPipeIndexes {
    param([string]$Line)
    $result = New-Object System.Collections.ArrayList
    $i = 0
    while ($i -lt $Line.Length) {
        $c = $Line[$i]
        if ($c -eq [char]92) { $i += 2; continue }
        if ($c -eq [char]124) { [void]$result.Add($i) }
        $i++
    }
    return $result
}

function Split-TableRow {
    param([string]$Line)
    $idx = @(Get-UnescapedPipeIndexes -Line $Line)
    $cells = New-Object System.Collections.ArrayList
    $prev = -1
    foreach ($p in $idx) {
        [void]$cells.Add($Line.Substring($prev + 1, $p - $prev - 1))
        $prev = $p
    }
    [void]$cells.Add($Line.Substring($prev + 1))
    $trimStart = $Line.Length - $Line.TrimStart().Length
    $trimEnd = $Line.TrimEnd().Length
    $hasLead = ($idx.Count -gt 0 -and $idx[0] -eq $trimStart)
    $hasTail = ($idx.Count -gt 0 -and $idx[$idx.Count - 1] -eq ($trimEnd - 1))
    if ($hasLead -and $cells.Count -gt 0) { $cells.RemoveAt(0) }
    if ($hasTail -and $cells.Count -gt 0) { $cells.RemoveAt($cells.Count - 1) }
    return $cells
}

function Test-TableRowLike {
    param([string]$Text)
    $t = $Text.Trim()
    if ($t.Length -eq 0) { return $false }
    return ($t[0] -eq [char]124)
}

function Get-LinkTargets {
    param([string]$Line)
    $targets = New-Object System.Collections.ArrayList
    $n = $Line.Length
    $i = 0
    while ($i -lt $n) {
        if ($Line[$i] -eq [char]92) { $i += 2; continue }
        if ($Line[$i] -ne [char]91) { $i++; continue }
        $depth = 0
        $j = $i
        $close = -1
        while ($j -lt $n) {
            $ch = $Line[$j]
            if ($ch -eq [char]92) { $j += 2; continue }
            if ($ch -eq [char]91) { $depth++ }
            elseif ($ch -eq [char]93) {
                $depth--
                if ($depth -eq 0) { $close = $j; break }
            }
            $j++
        }
        if ($close -lt 0) { $i++; continue }
        $k = $close + 1
        if ($k -ge $n -or $Line[$k] -ne [char]40) { $i++; continue }
        $pdepth = 1
        $m = $k + 1
        $pclose = -1
        while ($m -lt $n) {
            $ch = $Line[$m]
            if ($ch -eq [char]92) { $m += 2; continue }
            if ($ch -eq [char]40) { $pdepth++ }
            elseif ($ch -eq [char]41) {
                $pdepth--
                if ($pdepth -eq 0) { $pclose = $m; break }
            }
            $m++
        }
        if ($pclose -lt 0) { $i++; continue }
        [void]$targets.Add($Line.Substring($k + 1, $pclose - $k - 1))
        $i = $pclose + 1
    }
    return $targets
}

function Get-LinkPath {
    param([string]$Raw)
    $t = $Raw.Trim()
    if ($t.Length -eq 0) { return $null }
    if ($t[0] -eq [char]60) {
        $gt = $t.IndexOf([char]62)
        if ($gt -gt 1) { $t = $t.Substring(1, $gt - 1).Trim() }
    } else {
        $sp = $t.IndexOfAny([char[]]@([char]32, [char]9))
        if ($sp -ge 0) { $t = $t.Substring(0, $sp) }
    }
    if ($t.Length -eq 0) { return $null }
    if ($t -match '^(https?:|mailto:)') { return $null }
    if ($t[0] -eq [char]35) { return $null }
    $hash = $t.IndexOf([char]35)
    if ($hash -ge 0) { $t = $t.Substring(0, $hash) }
    if ($t.Length -eq 0) { return $null }
    try {
        $t = [System.Uri]::UnescapeDataString($t)
    } catch {
        return $null
    }
    return $t
}

function Resolve-LinkTarget {
    param([string]$FileFull, [string]$Target)
    $dir = [System.IO.Path]::GetDirectoryName($FileFull)
    $targetPath = $Target.Replace('/', [System.IO.Path]::DirectorySeparatorChar)
    try {
        $full = [System.IO.Path]::GetFullPath((Join-Path $dir $targetPath))
    } catch {
        return [pscustomobject]@{ OutOfRoot = $false; Exists = $false }
    }
    $sep = [System.IO.Path]::DirectorySeparatorChar
    $inside = [string]::Equals($full, $script:RootFull, [System.StringComparison]::OrdinalIgnoreCase)
    if (-not $inside) {
        $inside = $full.StartsWith($script:RootFull + $sep, [System.StringComparison]::OrdinalIgnoreCase)
    }
    $exists = [System.IO.File]::Exists($full) -or [System.IO.Directory]::Exists($full)
    return [pscustomobject]@{ OutOfRoot = (-not $inside); Exists = $exists }
}

function Test-MetadataLine {
    param([string]$Text)
    foreach ($p in $script:MetaPrefixes) {
        if ($Text.StartsWith($p, [System.StringComparison]::Ordinal)) { return $true }
    }
    return $false
}

function Write-ReportAndExit {
    param([int]$FileCount)
    $sorted = $script:Findings.ToArray()
    [Array]::Sort($sorted, [System.Comparison[object]]{
        param($a, $b)
        $c = [string]::CompareOrdinal([string]$a.Rel, [string]$b.Rel)
        if ($c -ne 0) { return $c }
        if ($a.Line -ne $b.Line) {
            if ($a.Line -lt $b.Line) { return -1 }
            return 1
        }
        $c = [string]::CompareOrdinal([string]$a.Gid, [string]$b.Gid)
        if ($c -ne 0) { return $c }
        return [string]::CompareOrdinal([string]$a.Message, [string]$b.Message)
    })
    $errorCount = 0
    $warnCount = 0
    foreach ($f in $sorted) {
        Write-Output ('{0} {1} {2}:{3} {4}' -f $f.Level, $f.Gid, $f.Rel, $f.Line, $f.Message)
        if ($f.Level -eq 'ERROR') { $errorCount++ } else { $warnCount++ }
    }
    Write-Output ('doc-gate: ' + $FileCount + ' files, ' + $errorCount + ' errors, ' + $warnCount + ' warnings')
    if ($errorCount -gt 0) {
        Write-Output 'doc-gate: FAIL'
    } else {
        Write-Output 'doc-gate: OK'
    }
    if ($Fail -and $errorCount -gt 0) { exit 1 }
    exit 0
}

# ---------------------------------------------------------------------------
# Main
# ---------------------------------------------------------------------------

if ([string]::IsNullOrWhiteSpace($Root)) {
    $scriptRoot = $PSScriptRoot
    if ([string]::IsNullOrEmpty($scriptRoot)) { $scriptRoot = (Get-Location).Path }
    $Root = Split-Path -Parent (Split-Path -Parent $scriptRoot)
}
$rootFull = [System.IO.Path]::GetFullPath($Root)
if ($rootFull.Length -gt 3) { $rootFull = $rootFull.TrimEnd([char]'\', [char]'/') }
$script:RootFull = $rootFull

if (-not [System.IO.Directory]::Exists($rootFull)) {
    Add-Finding -Level 'ERROR' -Gid 'G0' -Rel '.' -Line 0 -Message ('scan root does not exist: ' + $rootFull)
    Write-ReportAndExit -FileCount 0
}

$utf8 = New-Object System.Text.UTF8Encoding($false)
$registryRel = 'tools/doc-gate/doc-budgets.json'
$baselineRel = 'tools/doc-gate/doc-budgets-baseline.txt'
$registryPath = Join-Path $rootFull $registryRel
$baselinePath = Join-Path $rootFull $baselineRel
$limits = [hashtable]::new([System.StringComparer]::Ordinal)
$limitOrder = New-Object System.Collections.Generic.List[string]
$registryUsable = $false

if (-not [System.IO.File]::Exists($registryPath)) {
    Add-Finding -Level 'ERROR' -Gid 'G8' -Rel $registryRel -Line 0 -Message 'budget registry file is missing'
} else {
    $jsonText = $null
    try {
        $jsonText = [System.IO.File]::ReadAllText($registryPath, $utf8)
    } catch {
        Add-Finding -Level 'ERROR' -Gid 'G8' -Rel $registryRel -Line 0 -Message ('cannot read budget registry: ' + $_.Exception.Message)
    }
    if ($null -ne $jsonText) {
        if ($jsonText.Length -gt 0 -and [int]$jsonText[0] -eq 0xFEFF) { $jsonText = $jsonText.Substring(1) }
        $registryObject = $null
        try {
            $registryObject = $jsonText | ConvertFrom-Json -ErrorAction Stop
        } catch {
            Add-Finding -Level 'ERROR' -Gid 'G8' -Rel $registryRel -Line 0 -Message ('invalid JSON: ' + $_.Exception.Message)
        }
        if ($null -ne $registryObject) {
            if (-not ($registryObject -is [System.Management.Automation.PSCustomObject])) {
                Add-Finding -Level 'ERROR' -Gid 'G8' -Rel $registryRel -Line 0 -Message 'registry root must be a JSON object'
            } else {
                $rootNames = @($registryObject.PSObject.Properties.Name)
                $schemaOk = $true
                if (-not ($rootNames -contains 'version')) {
                    Add-Finding -Level 'ERROR' -Gid 'G8' -Rel $registryRel -Line 0 -Message 'registry is missing root property version'
                    $schemaOk = $false
                } elseif (-not (Test-IntegerValue -Value $registryObject.version)) {
                    Add-Finding -Level 'ERROR' -Gid 'G8' -Rel $registryRel -Line 0 -Message 'registry version must be an integer'
                    $schemaOk = $false
                }
                if (-not ($rootNames -contains 'limits')) {
                    Add-Finding -Level 'ERROR' -Gid 'G8' -Rel $registryRel -Line 0 -Message 'registry is missing root property limits'
                    $schemaOk = $false
                } elseif (-not ($registryObject.limits -is [System.Array])) {
                    Add-Finding -Level 'ERROR' -Gid 'G8' -Rel $registryRel -Line 0 -Message 'registry limits must be an array'
                    $schemaOk = $false
                }
                if ($schemaOk) {
                    $registryUsable = $true
                    $entryIndex = 0
                    foreach ($entry in @($registryObject.limits)) {
                        $entryIndex++
                        if (-not ($entry -is [System.Management.Automation.PSCustomObject])) {
                            Add-Finding -Level 'ERROR' -Gid 'G8' -Rel $registryRel -Line 0 -Message ('limits[' + $entryIndex + '] must be an object')
                            continue
                        }
                        $names = @($entry.PSObject.Properties.Name)
                        if ($names.Count -ne 2 -or -not ($names -contains 'path') -or -not ($names -contains 'limitBytes')) {
                            Add-Finding -Level 'ERROR' -Gid 'G8' -Rel $registryRel -Line 0 -Message ('limits[' + $entryIndex + '] must contain exactly path and limitBytes')
                            continue
                        }
                        $entryPath = [string]$entry.path
                        $entryLimit = $entry.limitBytes
                        $pathOk = Test-RegistryPath -Path $entryPath
                        $limitOk = Test-IntegerValue -Value $entryLimit
                        if (-not $pathOk) {
                            Add-Finding -Level 'ERROR' -Gid 'G8' -Rel $registryRel -Line 0 -Message ('limits[' + $entryIndex + '] path is invalid: ' + $entryPath)
                        }
                        if (-not $limitOk) {
                            Add-Finding -Level 'ERROR' -Gid 'G8' -Rel $registryRel -Line 0 -Message ('limits[' + $entryIndex + '] limitBytes must be an integer')
                        }
                        if ($pathOk -and $limitOk) {
                            if ($limits.ContainsKey($entryPath)) {
                                Add-Finding -Level 'ERROR' -Gid 'G8' -Rel $registryRel -Line 0 -Message ('duplicate path in registry: ' + $entryPath)
                            } else {
                                $limits[$entryPath] = [long]$entryLimit
                                [void]$limitOrder.Add($entryPath)
                            }
                        }
                    }
                }
            }
        }
    }
}

$baseline = [hashtable]::new([System.StringComparer]::Ordinal)
$baselineOrder = New-Object System.Collections.Generic.List[string]
$baselineAvailable = $false

if (-not [System.IO.File]::Exists($baselinePath)) {
    Add-Finding -Level 'ERROR' -Gid 'G8' -Rel $baselineRel -Line 0 -Message 'budget baseline file is missing'
} else {
    $baselineLines = $null
    try {
        $baselineLines = [System.IO.File]::ReadAllLines($baselinePath, $utf8)
        $baselineAvailable = $true
    } catch {
        Add-Finding -Level 'ERROR' -Gid 'G8' -Rel $baselineRel -Line 0 -Message ('cannot read budget baseline: ' + $_.Exception.Message)
    }
    if ($null -ne $baselineLines) {
        for ($k = 0; $k -lt $baselineLines.Length; $k++) {
            $line = $baselineLines[$k]
            if ($line.Length -gt 0 -and [int]$line[0] -eq 0xFEFF) { $line = $line.Substring(1) }
            $line = $line.TrimEnd([char]13)
            if ($line.Trim().Length -eq 0) { continue }
            $tabs = $line.Split([char]9)
            if ($tabs.Length -ne 2) {
                Add-Finding -Level 'ERROR' -Gid 'G8' -Rel $baselineRel -Line ($k + 1) -Message 'baseline line must be path<TAB>limitBytes'
                continue
            }
            $baselinePathText = $tabs[0]
            $baselineValueText = $tabs[1].Trim()
            $baselineValue = 0L
            if (-not [long]::TryParse($baselineValueText, [ref]$baselineValue)) {
                Add-Finding -Level 'ERROR' -Gid 'G8' -Rel $baselineRel -Line ($k + 1) -Message 'baseline limitBytes is not an integer'
                continue
            }
            if (-not (Test-RegistryPath -Path $baselinePathText)) {
                Add-Finding -Level 'ERROR' -Gid 'G8' -Rel $baselineRel -Line ($k + 1) -Message ('baseline path is invalid: ' + $baselinePathText)
                continue
            }
            if ($baseline.ContainsKey($baselinePathText)) {
                Add-Finding -Level 'ERROR' -Gid 'G8' -Rel $baselineRel -Line ($k + 1) -Message ('duplicate baseline path: ' + $baselinePathText)
                continue
            }
            $baseline[$baselinePathText] = $baselineValue
            [void]$baselineOrder.Add($baselinePathText)
        }
    }
}

if ($registryUsable -and $baselineAvailable) {
    foreach ($registeredPath in $limitOrder) {
        if (-not $baseline.ContainsKey($registeredPath)) {
            Add-Finding -Level 'ERROR' -Gid 'G8' -Rel $baselineRel -Line 0 -Message ('registry path is missing from baseline: ' + $registeredPath)
        } elseif (([long]$limits[$registeredPath]) -gt ([long]$baseline[$registeredPath])) {
            Add-Finding -Level 'ERROR' -Gid 'G8' -Rel $baselineRel -Line 0 -Message ('registry limitBytes ' + ([long]$limits[$registeredPath]) + ' exceeds baseline ' + ([long]$baseline[$registeredPath]) + ' for ' + $registeredPath)
        }
    }
    foreach ($baselinePathText in $baselineOrder) {
        if (-not $limits.ContainsKey($baselinePathText)) {
            Add-Finding -Level 'WARN' -Gid 'G8' -Rel $baselineRel -Line 0 -Message ('baseline entry is not registered in the registry: ' + $baselinePathText)
        }
    }
}

$mdFiles = [System.IO.Directory]::GetFiles($rootFull, '*.md', [System.IO.SearchOption]::AllDirectories)
$fileCount = 0

foreach ($fullPath in $mdFiles) {
    $rel = Get-RelPath -FullPath $fullPath
    if (Test-ExcludedPath -Rel $rel) { continue }
    $extension = [System.IO.Path]::GetExtension($rel)
    if (-not [string]::Equals($extension, '.md', [System.StringComparison]::OrdinalIgnoreCase)) { continue }
    $fileCount++

    $info = Read-BytesText -FullPath $fullPath
    $bytes = $info.Bytes
    $text = $info.Text

    if (-not $info.ValidUtf8) {
        Add-Finding -Level 'WARN' -Gid 'G2' -Rel $rel -Line 0 -Message 'file is not valid UTF-8'
    }
    if ($bytes.Length -ge 3 -and $bytes[0] -eq 0xEF -and $bytes[1] -eq 0xBB -and $bytes[2] -eq 0xBF) {
        Add-Finding -Level 'ERROR' -Gid 'G2' -Rel $rel -Line 1 -Message 'file starts with a UTF-8 BOM'
    }

    $crIndex = -1
    for ($k = 0; $k -lt $bytes.Length; $k++) {
        if ($bytes[$k] -eq 13) { $crIndex = $k; break }
    }
    if ($crIndex -ge 0) {
        $crLine = 1
        for ($k = 0; $k -lt $crIndex; $k++) {
            if ($bytes[$k] -eq 10) { $crLine++ }
        }
        Add-Finding -Level 'ERROR' -Gid 'G3' -Rel $rel -Line $crLine -Message 'carriage return (0x0D) found; use LF line endings'
    }

    if ($registryUsable) {
        if ($limits.ContainsKey($rel)) {
            $limitBytes = [long]$limits[$rel]
            if ($bytes.Length -gt $limitBytes) {
                Add-Finding -Level 'ERROR' -Gid 'G7' -Rel $rel -Line 0 -Message ('file is ' + $bytes.Length + ' bytes, over the registered budget of ' + $limitBytes)
            }
        } else {
            Add-Finding -Level 'WARN' -Gid 'G7' -Rel $rel -Line 0 -Message 'file is not registered in doc-budgets.json'
        }
    }

    $lines = $text.Split([char]10)
    $lineCount = $lines.Length
    $raw = New-Object 'string[]' $lineCount
    $masked = New-Object 'string[]' $lineCount
    $inFence = New-Object 'bool[]' $lineCount
    $fenceOpen = $false
    $fenceChar = [char]0
    $fenceLen = 0
    $fencePattern = '^(' + [string][char]96 + '{3,}|~{3,})'
    for ($i = 0; $i -lt $lineCount; $i++) {
        $line = $lines[$i]
        if ($line.Length -gt 0 -and [int]$line[$line.Length - 1] -eq 13) {
            $line = $line.Substring(0, $line.Length - 1)
        }
        $raw[$i] = $line
        $trimmed = $line.Trim()
        if ($fenceOpen) {
            $inFence[$i] = $true
            $masked[$i] = ''
            $closePattern = '^' + [regex]::Escape([string]$fenceChar) + '{' + $fenceLen + ',}\s*$'
            if ($trimmed -match $closePattern) { $fenceOpen = $false }
            continue
        }
        if ($trimmed -match $fencePattern) {
            $fenceOpen = $true
            $fenceChar = $trimmed[0]
            $fenceLen = $matches[1].Length
            $inFence[$i] = $true
            $masked[$i] = ''
            continue
        }
        $masked[$i] = Hide-InlineCode -Line $line
    }

    for ($i = 0; $i -lt $lineCount; $i++) {
        if ($raw[$i].Length -gt 5000) {
            Add-Finding -Level 'ERROR' -Gid 'G5' -Rel $rel -Line ($i + 1) -Message ('line is ' + $raw[$i].Length + ' characters, over the 5000 character limit')
        }
    }

    $isMilestoneIndex = [string]::Equals($rel, 'docs/milestones/README.md', [System.StringComparison]::Ordinal)
    $i = 0
    while ($i -lt $lineCount) {
        if ($inFence[$i]) { $i++; continue }
        if (-not (Test-TableRowLike -Text $masked[$i])) { $i++; continue }
        if (($i + 1) -ge $lineCount) { $i++; continue }
        if ($inFence[$i + 1] -or -not (Test-TableRowLike -Text $masked[$i + 1])) { $i++; continue }
        $blockEnd = $i + 1
        while ($blockEnd -lt $lineCount -and -not $inFence[$blockEnd] -and (Test-TableRowLike -Text $masked[$blockEnd])) {
            $blockEnd++
        }
        $headerCells = @(Split-TableRow -Line $masked[$i])
        $separatorCells = @(Split-TableRow -Line $masked[$i + 1])
        $separatorOk = $true
        if ($separatorCells.Count -ne $headerCells.Count) { $separatorOk = $false }
        if ($separatorOk) {
            foreach ($cell in $separatorCells) {
                if (-not ($cell.Trim() -match '^:?-{2,}:?$')) { $separatorOk = $false; break }
            }
        }
        if (-not $separatorOk) {
            Add-Finding -Level 'ERROR' -Gid 'G4' -Rel $rel -Line ($i + 2) -Message ('table separator line is malformed or has ' + $separatorCells.Count + ' cells (header has ' + $headerCells.Count + ')')
        }
        for ($ln = $i; $ln -lt $blockEnd; $ln++) {
            $pipes = @(Get-UnescapedPipeIndexes -Line $masked[$ln])
            for ($q = 1; $q -lt $pipes.Count; $q++) {
                if ($pipes[$q] -eq ($pipes[$q - 1] + 1)) {
                    Add-Finding -Level 'ERROR' -Gid 'G4' -Rel $rel -Line ($ln + 1) -Message 'table line contains an unescaped double pipe (empty cell)'
                    break
                }
            }
            if ($ln -ge ($i + 2)) {
                $rowCells = @(Split-TableRow -Line $masked[$ln])
                if ($rowCells.Count -ne $headerCells.Count) {
                    Add-Finding -Level 'ERROR' -Gid 'G4' -Rel $rel -Line ($ln + 1) -Message ('table row has ' + $rowCells.Count + ' cells (header has ' + $headerCells.Count + ')')
                }
            }
            if ($isMilestoneIndex -and $raw[$ln].Length -gt 300) {
                Add-Finding -Level 'ERROR' -Gid 'G6' -Rel $rel -Line ($ln + 1) -Message ('index table line is ' + $raw[$ln].Length + ' characters, over the 300 character limit')
            }
        }
        $i = $blockEnd
    }

    for ($i = 0; $i -lt $lineCount; $i++) {
        if ($inFence[$i]) { continue }
        $targets = @(Get-LinkTargets -Line $masked[$i])
        foreach ($rawTarget in $targets) {
            $target = Get-LinkPath -Raw $rawTarget
            if ($null -eq $target) { continue }
            $resolution = Resolve-LinkTarget -FileFull $fullPath -Target $target
            if ($resolution.OutOfRoot) {
                Add-Finding -Level 'WARN' -Gid 'G1' -Rel $rel -Line ($i + 1) -Message ('link target is outside the scan root: ' + $target)
            } elseif (-not $resolution.Exists) {
                Add-Finding -Level 'ERROR' -Gid 'G1' -Rel $rel -Line ($i + 1) -Message ('broken relative link: ' + $target)
            }
        }
    }

    if ($rel.StartsWith('.agents/notes/', [System.StringComparison]::Ordinal)) {
        $leaf = [System.IO.Path]::GetFileName($rel)
        $notesExempt = ($leaf -ceq 'README.md') -or ($leaf -ceq 'AGENTS.md') -or ($leaf -ceq '000-template.md')
        if (-not $notesExempt) {
            if ($leaf -ceq 'INDEX.md') {
                Add-Finding -Level 'ERROR' -Gid 'G10' -Rel $rel -Line 0 -Message 'note index files are forbidden'
            }
            $notePattern = '^\.agents/notes/(proposed|implemented|rejected|archived)/(feature|bug-fix|simplification|architecture|process|testing)/[0-9]{4}-[0-9]{2}-[0-9]{2}-[a-z0-9]+(-[a-z0-9]+)*\.md$'
            $lifecycle = ''
            if ($rel -cmatch $notePattern) {
                $lifecycle = $matches[1]
            } else {
                Add-Finding -Level 'ERROR' -Gid 'G10' -Rel $rel -Line 0 -Message 'note path must match .agents/notes/{lifecycle}/{class}/yyyy-mm-dd-slug.md'
            }
            $firstLine = ''
            $firstLineNumber = 0
            for ($i = 0; $i -lt $lineCount; $i++) {
                if ($raw[$i].Trim().Length -gt 0) {
                    $firstLine = $raw[$i].Trim()
                    $firstLineNumber = $i + 1
                    break
                }
            }
            if ($firstLine.Length -eq 0) {
                Add-Finding -Level 'ERROR' -Gid 'G10' -Rel $rel -Line 0 -Message 'note has no non-empty line'
            } elseif (-not $firstLine.StartsWith('# Agent Note:', [System.StringComparison]::Ordinal)) {
                Add-Finding -Level 'ERROR' -Gid 'G10' -Rel $rel -Line $firstLineNumber -Message 'note first non-empty line must start with "# Agent Note:"'
            }
            $statusText = $null
            $statusLineNumber = 0
            for ($i = 0; $i -lt $lineCount; $i++) {
                $candidate = $raw[$i].Trim()
                if ($candidate -match '^Status:\s*(.*)$') {
                    $statusText = $matches[1].Trim()
                    $statusLineNumber = $i + 1
                    break
                }
            }
            if ($null -eq $statusText) {
                Add-Finding -Level 'ERROR' -Gid 'G10' -Rel $rel -Line 0 -Message 'note is missing a Status: line'
            } elseif ($statusText.Length -eq 0) {
                Add-Finding -Level 'ERROR' -Gid 'G10' -Rel $rel -Line $statusLineNumber -Message 'note Status: line has no value'
            } elseif ($lifecycle.Length -gt 0) {
                $statusWord = ($statusText -split '\s+')[0]
                if (-not [string]::Equals($statusWord, $lifecycle, [System.StringComparison]::Ordinal)) {
                    Add-Finding -Level 'ERROR' -Gid 'G10' -Rel $rel -Line $statusLineNumber -Message ('note Status word "' + $statusWord + '" does not match lifecycle "' + $lifecycle + '"')
                }
            }
            $sectionPatterns = @(
                '^##\s+Problem\s*$',
                '^##\s+Decision\s*$',
                '^##\s+Alternatives considered\s*$',
                '^##\s+Consequences\s*$'
            )
            $sectionNames = @('Problem', 'Decision', 'Alternatives considered', 'Consequences')
            for ($s = 0; $s -lt $sectionPatterns.Length; $s++) {
                $found = $false
                for ($i = 0; $i -lt $lineCount; $i++) {
                    if ($raw[$i].Trim() -match $sectionPatterns[$s]) { $found = $true; break }
                }
                if (-not $found) {
                    Add-Finding -Level 'ERROR' -Gid 'G10' -Rel $rel -Line 0 -Message ('note is missing section: ## ' + $sectionNames[$s])
                }
            }
        }
    }

    $runStart = 0
    $runCount = 0
    $inHtmlComment = $false
    for ($i = 0; $i -lt $lineCount; $i++) {
        $line = $raw[$i]
        $trimmed = $line.Trim()
        $isProse = $false
        if ($inFence[$i]) {
            $isProse = $false
        } elseif ($trimmed.Length -eq 0) {
            $isProse = $false
        } elseif ($inHtmlComment) {
            $isProse = $false
            if ($trimmed.Contains('-->')) { $inHtmlComment = $false }
        } elseif ($trimmed.StartsWith('<!--', [System.StringComparison]::Ordinal)) {
            $isProse = $false
            if (-not $trimmed.Contains('-->')) { $inHtmlComment = $true }
        } elseif ($trimmed[0] -eq [char]35) {
            $isProse = $false
        } elseif ($trimmed[0] -eq [char]62) {
            $isProse = $false
        } elseif ($trimmed[0] -eq [char]124) {
            $isProse = $false
        } elseif ($line.Length -gt 0 -and ($line[0] -eq [char]32 -or $line[0] -eq [char]9)) {
            $isProse = $false
        } elseif ($trimmed -match '^([-*+]|[0-9]+[.)])(\s|$)') {
            $isProse = $false
        } elseif ($trimmed -match '^\[[^\]]+\]:\s*\S') {
            $isProse = $false
        } elseif ($trimmed -match $fencePattern) {
            $isProse = $false
        } elseif (Test-MetadataLine -Text $trimmed) {
            $isProse = $false
        } else {
            $isProse = $true
        }
        if ($isProse) {
            if ($runCount -eq 0) { $runStart = $i + 1 }
            $runCount++
        } else {
            if ($runCount -ge 2) {
                Add-Finding -Level 'WARN' -Gid 'G12' -Rel $rel -Line $runStart -Message ('paragraph spans ' + $runCount + ' physical lines; write one paragraph per line')
            }
            $runCount = 0
        }
    }
    if ($runCount -ge 2) {
        Add-Finding -Level 'WARN' -Gid 'G12' -Rel $rel -Line $runStart -Message ('paragraph spans ' + $runCount + ' physical lines; write one paragraph per line')
    }
}

$pitfallDir = Join-Path $rootFull 'docs/engineering/pitfalls'
if ([System.IO.Directory]::Exists($pitfallDir)) {
    $numbers = New-Object System.Collections.Generic.List[int]
    foreach ($pitPath in [System.IO.Directory]::GetFiles($pitfallDir, '*.md', [System.IO.SearchOption]::TopDirectoryOnly)) {
        $pitName = [System.IO.Path]::GetFileName($pitPath)
        if ($pitName -ceq '000-template.md') { continue }
        if ($pitName -cmatch '^([0-9]{3})-.+\.md$') {
            [void]$numbers.Add([int]$matches[1])
        }
    }
    if ($numbers.Count -gt 0) {
        $pitNumbers = $numbers.ToArray()
        [Array]::Sort($pitNumbers)
        $expectedNumber = 1
        $numberingOk = $true
        foreach ($num in $pitNumbers) {
            if ($num -ne $expectedNumber) { $numberingOk = $false; break }
            $expectedNumber++
        }
        if (-not $numberingOk) {
            $shown = @()
            foreach ($num in $pitNumbers) { $shown += ('{0:D3}' -f $num) }
            Add-Finding -Level 'ERROR' -Gid 'G11' -Rel 'docs/engineering/pitfalls' -Line 0 -Message ('pitfall numbers must be continuous 1..N, found: ' + ($shown -join ','))
        }
    }
}

$toolsDir = Join-Path $rootFull 'tools'
if ([System.IO.Directory]::Exists($toolsDir)) {
    $executableFiles = New-Object System.Collections.Generic.List[string]
    foreach ($psFile in [System.IO.Directory]::GetFiles($toolsDir, '*.ps1', [System.IO.SearchOption]::AllDirectories)) {
        [void]$executableFiles.Add($psFile)
    }
    $hooksDir = Join-Path $toolsDir 'git-hooks'
    if ([System.IO.Directory]::Exists($hooksDir)) {
        foreach ($hookFile in [System.IO.Directory]::GetFiles($hooksDir, '*', [System.IO.SearchOption]::TopDirectoryOnly)) {
            if ([System.IO.Path]::GetExtension($hookFile).Length -eq 0) {
                [void]$executableFiles.Add($hookFile)
            }
        }
    }
    $seenFiles = [hashtable]::new([System.StringComparer]::OrdinalIgnoreCase)
    foreach ($executableFile in $executableFiles) {
        if ($seenFiles.ContainsKey($executableFile)) { continue }
        $seenFiles[$executableFile] = 1
        $scriptRel = Get-RelPath -FullPath $executableFile
        if (Test-ExcludedPath -Rel $scriptRel) { continue }
        $scriptBytes = [System.IO.File]::ReadAllBytes($executableFile)
        $nonAsciiIndex = -1
        for ($k = 0; $k -lt $scriptBytes.Length; $k++) {
            if ($scriptBytes[$k] -ge 128) { $nonAsciiIndex = $k; break }
        }
        if ($nonAsciiIndex -ge 0) {
            $asciiLine = 1
            for ($k = 0; $k -lt $nonAsciiIndex; $k++) {
                if ($scriptBytes[$k] -eq 10) { $asciiLine++ }
            }
            Add-Finding -Level 'ERROR' -Gid 'G9' -Rel $scriptRel -Line $asciiLine -Message 'executable script contains a non-ASCII byte'
        }
    }
}

if ($registryUsable) {
    foreach ($registeredPath in $limitOrder) {
        $registeredFull = Join-Path $rootFull ($registeredPath.Replace('/', [System.IO.Path]::DirectorySeparatorChar))
        if (-not [System.IO.File]::Exists($registeredFull)) {
            Add-Finding -Level 'WARN' -Gid 'G7' -Rel $registeredPath -Line 0 -Message 'registered in doc-budgets.json but the file does not exist (registry drift)'
        }
    }
}

Write-ReportAndExit -FileCount $fileCount
