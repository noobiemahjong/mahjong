# tools/git-hooks/tests/run.ps1
# Exercise the commit-msg hook against throwaway git repositories created under
# the system temp directory. The working repository is never modified.
#
# Usage:  pwsh -NoProfile -File tools/git-hooks/tests/run.ps1
# Exit:   0 = every case passed, 1 = at least one case failed.
#
# The file is pure ASCII on purpose (doc-gate G9); the CJK fixture is built from
# Unicode code points, so no non-ASCII byte appears in this source file.

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$here = $PSScriptRoot
$repoRoot = (Resolve-Path -LiteralPath (Join-Path $here '../../..')).Path
$hooksDir = Join-Path $repoRoot 'tools/git-hooks'
$hookPath = Join-Path $hooksDir 'commit-msg'
$hooksPathForGit = $hooksDir -replace '\\', '/'
$lf = [string][char]10

if (-not (Test-Path -LiteralPath $hookPath -PathType Leaf)) {
    Write-Host ('FAIL setup: hook not found at ' + $hookPath)
    exit 1
}

$tmpRoot = Join-Path ([System.IO.Path]::GetTempPath()) ('mahjong-git-hooks-' + [guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $tmpRoot -Force | Out-Null
$script:results = @()

function Write-Utf8Lf {
    param(
        [Parameter(Mandatory)][string]$Path,
        [Parameter(Mandatory)][AllowEmptyString()][string]$Text
    )
    $encoding = New-Object System.Text.UTF8Encoding($false)
    [System.IO.File]::WriteAllText($Path, $Text, $encoding)
}

function Invoke-Git {
    param(
        [Parameter(Mandatory)][string]$Repo,
        [Parameter(Mandatory)][string[]]$GitArgs
    )
    $output = & git -C $Repo @GitArgs 2>&1 | Out-String
    $code = $LASTEXITCODE
    return [pscustomobject]@{ ExitCode = $code; Output = $output }
}

function Add-Result {
    param(
        [Parameter(Mandatory)][string]$Name,
        [Parameter(Mandatory)][bool]$Passed,
        [Parameter(Mandatory)][AllowEmptyString()][string]$Detail
    )
    $script:results += [pscustomobject]@{ Name = $Name; Passed = $Passed; Detail = $Detail }
    if ($Passed) {
        Write-Host ('PASS ' + $Name + '  [' + $Detail + ']')
    }
    else {
        Write-Host ('FAIL ' + $Name + '  ' + $Detail)
    }
}

function Initialize-Repo {
    param([Parameter(Mandatory)][string]$Path)
    New-Item -ItemType Directory -Path $Path -Force | Out-Null
    $init = Invoke-Git $Path @('init', '--quiet')
    if ($init.ExitCode -ne 0) { throw ('git init failed in ' + $Path) }
    $null = Invoke-Git $Path @('config', 'user.name', 'Hook Test')
    $null = Invoke-Git $Path @('config', 'user.email', 'hook-test@example.invalid')
    $null = Invoke-Git $Path @('config', 'commit.gpgsign', 'false')
    # Seed one commit before enabling the hook so HEAD always exists.
    Write-Utf8Lf (Join-Path $Path 'seed.txt') ('seed' + $lf)
    $null = Invoke-Git $Path @('add', 'seed.txt')
    $seed = Invoke-Git $Path @('commit', '--no-gpg-sign', '--quiet', '-m', 'chore: seed repository')
    if ($seed.ExitCode -ne 0) { throw ('seed commit failed in ' + $Path) }
    $cfg = Invoke-Git $Path @('config', 'core.hooksPath', $hooksPathForGit)
    if ($cfg.ExitCode -ne 0) { throw ('setting core.hooksPath failed in ' + $Path) }
}

function Test-Commit {
    param(
        [Parameter(Mandatory)][string]$Name,
        [Parameter(Mandatory)][string]$Repo,
        [Parameter(Mandatory)][AllowEmptyString()][string]$Message,
        [Parameter(Mandatory)][bool]$ExpectPass,
        [string[]]$ExpectPatterns = @(),
        [switch]$AllowEmptyMessage
    )
    $msgFile = Join-Path $tmpRoot ('msg-' + $Name + '.txt')
    Write-Utf8Lf $msgFile $Message
    $headBefore = (Invoke-Git $Repo @('rev-parse', 'HEAD')).Output.Trim()
    $commitArgs = @('commit', '--no-gpg-sign', '--allow-empty', '--quiet', '-F', $msgFile)
    if ($AllowEmptyMessage) {
        $commitArgs = @('commit', '--no-gpg-sign', '--allow-empty', '--allow-empty-message', '--quiet', '-F', $msgFile)
    }
    $run = Invoke-Git $Repo $commitArgs
    $headAfter = (Invoke-Git $Repo @('rev-parse', 'HEAD')).Output.Trim()

    $problems = @()
    if ($ExpectPass) {
        if ($run.ExitCode -ne 0) { $problems += ('expected exit 0, got ' + $run.ExitCode) }
        if ($headBefore -eq $headAfter) { $problems += 'HEAD did not advance' }
    }
    else {
        if ($run.ExitCode -eq 0) { $problems += 'expected a non-zero exit, got 0' }
        if ($headBefore -ne $headAfter) { $problems += 'HEAD advanced although the message is invalid' }
        foreach ($pattern in $ExpectPatterns) {
            if ($run.Output -notmatch [regex]::Escape($pattern)) { $problems += ('missing reason text: ' + $pattern) }
        }
    }

    $detail = 'exit=' + $run.ExitCode
    if ($problems.Count -gt 0) { $detail = $problems -join '; ' }
    Add-Result -Name $Name -Passed ($problems.Count -eq 0) -Detail $detail

    $reasons = @()
    foreach ($line in $run.Output.Split([char]10)) {
        $trimmed = $line.Trim()
        if ($trimmed.StartsWith('commit-msg:')) { $reasons += $trimmed }
    }
    if ($reasons.Count -gt 0) { Write-Host ('      reason: ' + $reasons[0]) }
}

function Get-ShEnvironment {
    # Resolve a POSIX shell and the PATH entries Git gives its hooks, so the
    # direct-sh check sees the same coreutils (od/awk/sed/...) that a real hook
    # invocation gets from Git for Windows.
    $prefixDirs = @()
    $exe = $null
    try {
        $execPath = (& git --exec-path 2>$null | Out-String).Trim()
        if (-not [string]::IsNullOrEmpty($execPath)) {
            $gitRoot = Split-Path -Parent (Split-Path -Parent (Split-Path -Parent $execPath))
            foreach ($rel in @('usr/bin', 'mingw64/bin', 'bin')) {
                $full = Join-Path $gitRoot $rel
                if (Test-Path -LiteralPath $full -PathType Container) { $prefixDirs += $full }
            }
            foreach ($rel in @('usr/bin/sh.exe', 'bin/sh.exe', 'usr/bin/sh', 'bin/sh')) {
                $full = Join-Path $gitRoot $rel
                if (Test-Path -LiteralPath $full -PathType Leaf) { $exe = $full; break }
            }
        }
    }
    catch {
        # Fall through to the PATH lookup below.
    }
    if ($null -eq $exe) {
        $cmd = Get-Command sh -ErrorAction SilentlyContinue
        if ($null -ne $cmd) { $exe = $cmd.Source }
    }
    if ($null -ne $exe) {
        $exeDir = Split-Path -Parent $exe
        if (-not [string]::IsNullOrEmpty($exeDir) -and ($prefixDirs -notcontains $exeDir)) {
            $prefixDirs = @($exeDir) + $prefixDirs
        }
    }
    return [pscustomobject]@{ Exe = $exe; PathPrefix = ($prefixDirs -join ';') }
}

function Invoke-DirectSh {
    param(
        [Parameter(Mandatory)][string]$ShExe,
        [Parameter(Mandatory)][AllowEmptyString()][string]$PathPrefix,
        [Parameter(Mandatory)][string]$MessageFile
    )
    $savedPath = $env:PATH
    try {
        if (-not [string]::IsNullOrEmpty($PathPrefix)) { $env:PATH = $PathPrefix + ';' + $savedPath }
        $output = & $ShExe $hookPath $MessageFile 2>&1 | Out-String
        $code = $LASTEXITCODE
    }
    finally {
        $env:PATH = $savedPath
    }
    return [pscustomobject]@{ ExitCode = $code; Output = $output }
}

function Test-DirectSh {
    param(
        [Parameter(Mandatory)][string]$ShExe,
        [Parameter(Mandatory)][AllowEmptyString()][string]$PathPrefix,
        [Parameter(Mandatory)][hashtable]$Case
    )
    $msgFile = Join-Path $tmpRoot ('direct-' + $Case.Name + '.txt')
    Write-Utf8Lf $msgFile $Case.Message
    $run = Invoke-DirectSh -ShExe $ShExe -PathPrefix $PathPrefix -MessageFile $msgFile
    $out = $run.Output
    $code = $run.ExitCode
    $problems = @()
    if ($Case.Expect -eq 0) {
        if ($code -ne 0) { $problems += ('expected exit 0, got ' + $code) }
    }
    else {
        if ($code -eq 0) { $problems += 'expected a non-zero exit, got 0' }
        if (-not [string]::IsNullOrEmpty($Case.Pattern) -and $out -notmatch [regex]::Escape($Case.Pattern)) {
            $problems += ('missing reason text: ' + $Case.Pattern)
        }
    }
    $detail = 'exit=' + $code
    if ($problems.Count -gt 0) { $detail = $problems -join '; ' }
    Add-Result -Name $Case.Name -Passed ($problems.Count -eq 0) -Detail $detail
}

# Fixtures. The CJK subject is 20 characters (60 UTF-8 bytes): a byte-based
# length check would reject it, a character-based one accepts it.
$cjkCodePoints = @(0x4FEE, 0x590D, 0x65AD, 0x7EBF, 0x91CD, 0x8FDE, 0x540E, 0x72B6, 0x6001, 0x673A, 0x5361, 0x6B7B, 0x548C, 0x56DE, 0x653E, 0x9519, 0x4F4D, 0x7684, 0x95EE, 0x9898)
$cjkSubject = -join ($cjkCodePoints | ForEach-Object { [char]$_ })
$cjkChars = $cjkSubject.Length
$cjkBytes = [System.Text.Encoding]::UTF8.GetByteCount($cjkSubject)

$goodAsciiBody = @(
    '- keep the subject inside the conventional commit shape',
    '- body lines are counted, blank lines are not'
) -join $lf
$goodAscii = 'feat(core): add tile parser' + $lf + $lf + $goodAsciiBody + $lf

$goodCjkBody = @(
    '- the subject above is 20 characters and 60 UTF-8 bytes',
    '- a byte-based limit would reject it, a character-based limit accepts it'
) -join $lf
$goodCjk = 'fix(session): ' + $cjkSubject + $lf + $lf + $goodCjkBody + $lf

$badLong = 'feat: ' + ('a' * 56) + $lf

$badBodyLines = @('docs: update the milestone task table', '')
for ($i = 1; $i -le 11; $i++) {
    $badBodyLines += ('- non-empty body line ' + $i)
}
$badBody = ($badBodyLines -join $lf) + $lf

$badType = 'chorex: not an allowed conventional commit type' + $lf
$badEmpty = ''

$script:results = @()

try {
    Write-Host ''
    Write-Host '== fixture sanity =='
    Add-Result -Name 'fixture-cjk-meaningful' -Passed ($cjkChars -le 50 -and $cjkBytes -gt 50) -Detail ('chars=' + $cjkChars + ' bytes=' + $cjkBytes)

    Write-Host ''
    Write-Host '== git commit cases (hook dispatched by git) =='
    $repoA = Join-Path $tmpRoot 'repo-a'
    $repoB = Join-Path $tmpRoot 'repo-b'
    Initialize-Repo $repoA
    Initialize-Repo $repoB
    Write-Host ('      repo A: ' + $repoA)
    Write-Host ('      repo B: ' + $repoB)

    Test-Commit -Name 'good-ascii' -Repo $repoA -Message $goodAscii -ExpectPass $true
    Test-Commit -Name 'good-cjk-title' -Repo $repoA -Message $goodCjk -ExpectPass $true
    Test-Commit -Name 'bad-title-over-50-chars' -Repo $repoB -Message $badLong -ExpectPass $false -ExpectPatterns @('50 characters')
    Test-Commit -Name 'bad-body-11-lines' -Repo $repoB -Message $badBody -ExpectPass $false -ExpectPatterns @('non-empty lines', 'limit is 10')
    Test-Commit -Name 'bad-unknown-type' -Repo $repoB -Message $badType -ExpectPass $false -ExpectPatterns @('does not match the required shape')
    Test-Commit -Name 'bad-empty-message' -Repo $repoB -Message $badEmpty -ExpectPass $false -ExpectPatterns @('empty') -AllowEmptyMessage

    Write-Host ''
    Write-Host '== direct shell cases (hook invoked without git) =='
    $shEnv = Get-ShEnvironment
    if ($null -eq $shEnv.Exe) {
        Add-Result -Name 'direct-sh' -Passed $true -Detail 'SKIP: no sh executable on PATH or next to git'
    }
    else {
        Write-Host ('      sh: ' + $shEnv.Exe)
        $directCases = @(
            @{ Name = 'direct-good-ascii'; Message = $goodAscii; Expect = 0; Pattern = '' },
            @{ Name = 'direct-good-cjk'; Message = $goodCjk; Expect = 0; Pattern = '' },
            @{ Name = 'direct-bad-long'; Message = $badLong; Expect = 1; Pattern = '50 characters' },
            @{ Name = 'direct-bad-body'; Message = $badBody; Expect = 1; Pattern = 'non-empty lines' },
            @{ Name = 'direct-bad-empty'; Message = $badEmpty; Expect = 1; Pattern = 'empty' }
        )
        foreach ($case in $directCases) {
            Test-DirectSh -ShExe $shEnv.Exe -PathPrefix $shEnv.PathPrefix -Case $case
        }

        $missing = Join-Path $tmpRoot 'direct-missing-message.txt'
        $run = Invoke-DirectSh -ShExe $shEnv.Exe -PathPrefix $shEnv.PathPrefix -MessageFile $missing
        $out = $run.Output
        $code = $run.ExitCode
        $problems = @()
        if ($code -eq 0) { $problems += 'expected a non-zero exit, got 0' }
        if ($out -notmatch [regex]::Escape('does not exist')) { $problems += 'missing reason text: does not exist' }
        $detail = 'exit=' + $code
        if ($problems.Count -gt 0) { $detail = $problems -join '; ' }
        Add-Result -Name 'direct-missing-file' -Passed ($problems.Count -eq 0) -Detail $detail
    }
}
finally {
    if (Test-Path -LiteralPath $tmpRoot) {
        Remove-Item -LiteralPath $tmpRoot -Recurse -Force -ErrorAction SilentlyContinue
    }
}

Write-Host ''
$failed = @($script:results | Where-Object { -not $_.Passed })
$passed = @($script:results | Where-Object { $_.Passed })
Write-Host ('SUMMARY: ' + $passed.Count + ' passed, ' + $failed.Count + ' failed, ' + $script:results.Count + ' total')
if ($failed.Count -gt 0) {
    Write-Host 'RESULT: FAIL'
    exit 1
}
Write-Host 'RESULT: PASS'
exit 0
