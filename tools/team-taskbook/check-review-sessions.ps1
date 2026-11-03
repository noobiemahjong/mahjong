#!/usr/bin/env pwsh
# Review-session expiry checker (lead standard: review is opened per unit x round).
# Reads the DSH session projection cache and reports steps / average context / verdict.
# ASCII-only source and output; non-ASCII titles are escaped as \uXXXX.
#
# Usage:
#   pwsh -File tools/team-taskbook/check-review-sessions.ps1 -List 15
#   pwsh -File tools/team-taskbook/check-review-sessions.ps1 <session-id-or-prefix> [...]
#
# Options:
#   -Dir <path>     cache dir (default: <home>/.dsh/storages/session_projcache/sessions)
#   -Steps <n>      step limit for one review session (default 80)
#   -AvgCtxK <n>    average context limit in K tokens per step (default 200)
#   -List <n>       check the N most recently modified sessions
#   -Json           machine-readable output
#
# Exit codes: 0 = every checked session OK; 1 = at least one expired; 2 = usage/IO error.
# Note: "same product, 4th round" cannot be derived from a session file; check it by hand.

[CmdletBinding()]
param(
    [Parameter(Position = 0, ValueFromRemainingArguments = $true)]
    [string[]] $SessionId,
    [string] $Dir,
    [int] $Steps = 80,
    [int] $AvgCtxK = 200,
    [int] $List = 0,
    [switch] $Json
)

$ErrorActionPreference = 'Stop'

if (-not $Dir) {
    $Dir = Join-Path $HOME '.dsh/storages/session_projcache/sessions'
}
if ($Steps -le 0 -or $AvgCtxK -le 0 -or $List -lt 0) {
    [Console]::Error.WriteLine('bad numeric option: -Steps and -AvgCtxK must be > 0, -List >= 0')
    exit 2
}

function ConvertTo-AsciiText {
    param([string] $Text)
    if ($null -eq $Text) { return '' }
    $sb = New-Object System.Text.StringBuilder
    foreach ($ch in $Text.ToCharArray()) {
        $code = [int][char]$ch
        if ($code -ge 0x20 -and $code -le 0x7e) { [void]$sb.Append($ch) }
        elseif ($code -eq 9 -or $code -eq 10 -or $code -eq 13) { [void]$sb.Append(' ') }
        else { [void]$sb.Append('\u' + $code.ToString('x4')) }
    }
    return $sb.ToString()
}

if (-not (Test-Path -LiteralPath $Dir -PathType Container)) {
    [Console]::Error.WriteLine('cannot read dir: ' + (ConvertTo-AsciiText $Dir))
    exit 2
}

$entries = @(Get-ChildItem -LiteralPath $Dir -Filter '*.json' -File -ErrorAction Stop | Select-Object -ExpandProperty Name)

function Resolve-Entry {
    param([string] $Want)
    $want = $Want -replace '\.json$', ''
    $hits = New-Object System.Collections.Generic.List[string]
    foreach ($e in $entries) {
        $base = [System.IO.Path]::GetFileNameWithoutExtension($e)
        $short = $base -replace '^session-', ''
        $hit = $false
        if ($base -eq $want) { $hit = $true }
        elseif ($short -eq $want) { $hit = $true }
        elseif ($base -eq ('session-' + $want)) { $hit = $true }
        elseif ($short.StartsWith($want, [System.StringComparison]::Ordinal)) { $hit = $true }
        elseif ($base.StartsWith($want, [System.StringComparison]::Ordinal)) { $hit = $true }
        if ($hit) { $hits.Add($e) }
    }
    if ($hits.Count -eq 1) { return @{ file = $hits[0] } }
    if ($hits.Count -gt 1) { return @{ ambiguous = $hits.ToArray() } }
    return @{ missing = $true }
}

function Read-SessionMetrics {
    param([string] $FileName)
    $full = Join-Path $Dir $FileName
    $j = Get-Content -LiteralPath $full -Raw -Encoding UTF8 | ConvertFrom-Json
    $rows = $j.rows
    if ($null -eq $rows -and $null -ne $j.record) { $rows = $j.record.rows }
    $ss = $null
    $tu = $null
    if ($null -ne $rows -and $null -ne $rows.sessionStats) { $ss = $rows.sessionStats.val }
    if ($null -ne $rows -and $null -ne $rows.tokenUsage) { $tu = $rows.tokenUsage.val.totals }
    $stepCount = 0
    $turns = 0
    $ctx = [int64]0
    $out = [int64]0
    $title = ''
    if ($null -ne $ss) {
        if ($null -ne $ss.steps) { $stepCount = [int]$ss.steps }
        if ($null -ne $ss.turns) { $turns = [int]$ss.turns }
    }
    if ($null -ne $tu) {
        if ($null -ne $tu.cacheReadTokens) { $ctx += [int64]$tu.cacheReadTokens }
        if ($null -ne $tu.uncachedInputTokens) { $ctx += [int64]$tu.uncachedInputTokens }
        if ($null -ne $tu.outputTokens) { $out = [int64]$tu.outputTokens }
    }
    if ($null -ne $rows -and $null -ne $rows.title -and $null -ne $rows.title.val) {
        $title = (@($rows.title.val) | ForEach-Object { [string]$_ }) -join ''
    }
    $avgK = 0
    if ($stepCount -gt 0) { $avgK = [int][math]::Round(([double]$ctx / [double]$stepCount) / 1024.0) }
    $reasons = New-Object System.Collections.Generic.List[string]
    $noData = ($stepCount -le 0)
    if ($stepCount -gt $Steps) { $reasons.Add('steps>' + $Steps) }
    if ($stepCount -gt 0 -and $avgK -gt $AvgCtxK) { $reasons.Add('avg_ctx_k>' + $AvgCtxK) }
    $status = 'OK'
    if ($noData) { $status = 'NO_DATA' } elseif ($reasons.Count -gt 0) { $status = 'EXPIRED' }
    $baseName = [System.IO.Path]::GetFileNameWithoutExtension($FileName)
    $short = $baseName -replace '^session-', ''
    if ($short.Length -gt 8) { $short = $short.Substring(0, 8) }
    $mtime = (Get-Item -LiteralPath $full).LastWriteTimeUtc
    return [pscustomobject]@{
        id = $baseName
        short = $short
        steps = $stepCount
        turns = $turns
        avgK = $avgK
        out = $out
        title = $title
        status = $status
        expired = (-not $noData -and $reasons.Count -gt 0)
        reasons = $reasons.ToArray()
        mtime = $mtime.ToString('o')
    }
}

if ($SessionId.Count -eq 0 -and $List -le 0) {
    [Console]::Error.WriteLine('usage: check-review-sessions.ps1 (-List N | <id-or-prefix>...) [-Dir P] [-Steps 80] [-AvgCtxK 200] [-Json]')
    exit 2
}

$picked = New-Object System.Collections.Generic.List[string]
if ($List -gt 0) {
    $recent = @(Get-ChildItem -LiteralPath $Dir -Filter '*.json' -File | Sort-Object LastWriteTime -Descending | Select-Object -First $List)
    foreach ($f in $recent) { $picked.Add($f.Name) }
}
$exitCode = 0
foreach ($id in @($SessionId)) {
    if ([string]::IsNullOrWhiteSpace($id)) { continue }
    $res = Resolve-Entry -Want $id
    if ($res.missing) {
        [Console]::Error.WriteLine('missing session: ' + (ConvertTo-AsciiText $id))
        $exitCode = 2
        continue
    }
    if ($res.ambiguous) {
        [Console]::Error.WriteLine('ambiguous prefix: ' + (ConvertTo-AsciiText $id) + ' -> ' + (ConvertTo-AsciiText (($res.ambiguous | Select-Object -First 8) -join ',')))
        $exitCode = 2
        continue
    }
    $picked.Add($res.file)
}
if ($picked.Count -eq 0) { exit $exitCode }

$results = New-Object System.Collections.Generic.List[object]
foreach ($f in $picked) {
    try {
        $results.Add((Read-SessionMetrics -FileName $f))
    } catch {
        [Console]::Error.WriteLine('bad record: ' + (ConvertTo-AsciiText $f) + ' (' + (ConvertTo-AsciiText $_.Exception.Message) + ')')
        $exitCode = 2
    }
}

if ($Json) {
    $payload = [pscustomobject]@{
        thresholds = [pscustomobject]@{ steps = $Steps; avgCtxK = $AvgCtxK }
        sessions = $results.ToArray()
    }
    $payload | ConvertTo-Json -Depth 6
} else {
    [Console]::Out.WriteLine('thresholds: steps<=' + $Steps + ' avg_ctx<=' + $AvgCtxK + 'K  (same product 4th round: check by hand)')
    [Console]::Out.WriteLine('id        steps turns  avg_ctx    out  verdict   reasons / title')
    foreach ($r in $results) {
        $avgTxt = [string]$r.avgK + 'K'
        $outTxt = [string][math]::Round($r.out / 1024.0) + 'K'
        $line = '{0,-9}{1,5}{2,6}{3,9}{4,6}  {5,-8}' -f $r.short, $r.steps, $r.turns, $avgTxt, $outTxt, $r.status
        $reason = ''
        if ($r.expired) { $reason = ($r.reasons -join ',') }
        elseif ($r.status -eq 'NO_DATA') { $reason = 'no steps recorded' }
        $titleTxt = ''
        if ($r.title) { $titleTxt = (ConvertTo-AsciiText $r.title); if ($titleTxt.Length -gt 60) { $titleTxt = $titleTxt.Substring(0, 60) } }
        $line = $line + '  ' + $reason
        if ($titleTxt) { $line = $line + '  ' + $titleTxt }
        [Console]::Out.WriteLine($line)
    }
    $bad = @($results | Where-Object { $_.expired }).Count
    [Console]::Out.WriteLine('')
    [Console]::Out.WriteLine('summary: ' + $results.Count + ' session(s), ' + $bad + ' expired')
    if ($bad -gt 0) { [Console]::Out.WriteLine('action: open a NEW review session for the affected units; do not continue in the old one.') }
}
$expiredCount = @($results | Where-Object { $_.expired }).Count
if ($expiredCount -gt 0 -and $exitCode -eq 0) { $exitCode = 1 }
exit $exitCode
