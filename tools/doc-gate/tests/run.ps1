# Tests for tools/doc-gate: every case has a bad fixture that must fail with
# the expected rule id and a good fixture that must pass.
Set-StrictMode -Version 2.0
$ErrorActionPreference = 'Stop'
$PSNativeCommandUseErrorActionPreference = $false

$testsDir = $PSScriptRoot
$gate = Join-Path (Split-Path -Parent $testsDir) 'doc-gate.ps1'
$fixtures = Join-Path $testsDir 'fixtures'

$pwshExe = 'pwsh'
$pwshCandidate = Join-Path $PSHOME 'pwsh.exe'
if (Test-Path -LiteralPath $pwshCandidate) { $pwshExe = $pwshCandidate }

$cases = @(
    @{ Name = 'bad-link'; Gid = 'G1' },
    @{ Name = 'bom'; Gid = 'G2' },
    @{ Name = 'crlf'; Gid = 'G3' },
    @{ Name = 'over-budget'; Gid = 'G7' },
    @{ Name = 'bad-note'; Gid = 'G10' },
    @{ Name = 'pitfall-numbering'; Gid = 'G11' }
)

function Invoke-Gate {
    param([string]$ScanRoot)
    $output = & $pwshExe -NoProfile -File $gate -Root $ScanRoot -Fail 2>&1
    $exitCode = $LASTEXITCODE
    $lines = @()
    if ($null -ne $output) { $lines = @($output | ForEach-Object { [string]$_ }) }
    return [pscustomobject]@{ ExitCode = $exitCode; Lines = $lines }
}

$passed = 0
$failed = 0
foreach ($case in $cases) {
    $caseName = [string]$case.Name
    $gid = [string]$case.Gid
    $badDir = Join-Path (Join-Path $fixtures $caseName) 'bad'
    $goodDir = Join-Path (Join-Path $fixtures $caseName) 'good'
    $problems = New-Object System.Collections.ArrayList
    if (-not (Test-Path -LiteralPath $gate)) { [void]$problems.Add('gate script not found: ' + $gate) }
    if (-not (Test-Path -LiteralPath $badDir)) { [void]$problems.Add('bad fixture not found: ' + $badDir) }
    if (-not (Test-Path -LiteralPath $goodDir)) { [void]$problems.Add('good fixture not found: ' + $goodDir) }
    $bad = $null
    $good = $null
    if ($problems.Count -eq 0) {
        $bad = Invoke-Gate -ScanRoot $badDir
        if ($bad.ExitCode -ne 1) { [void]$problems.Add('bad fixture exit code is ' + $bad.ExitCode + ', expected 1') }
        $badHits = @($bad.Lines | Where-Object { $_ -match ('^ERROR ' + $gid + ' ') })
        if ($badHits.Count -eq 0) { [void]$problems.Add('bad fixture output has no ERROR ' + $gid) }
        $good = Invoke-Gate -ScanRoot $goodDir
        if ($good.ExitCode -ne 0) { [void]$problems.Add('good fixture exit code is ' + $good.ExitCode + ', expected 0') }
        $goodErrors = @($good.Lines | Where-Object { $_ -match '^ERROR ' })
        if ($goodErrors.Count -gt 0) { [void]$problems.Add('good fixture output contains ERROR lines') }
    }
    if ($problems.Count -eq 0) {
        $passed++
        Write-Output ('PASS ' + $caseName + ' (' + $gid + ')')
    } else {
        $failed++
        Write-Output ('FAIL ' + $caseName + ' (' + $gid + ')')
        foreach ($problem in $problems) { Write-Output ('  - ' + $problem) }
        if ($null -ne $bad) {
            foreach ($line in $bad.Lines) {
                if ($line -match '^ERROR ') { Write-Output ('    bad output: ' + $line) }
            }
        }
        if ($null -ne $good) {
            foreach ($line in $good.Lines) {
                if ($line -match '^ERROR ') { Write-Output ('    good output: ' + $line) }
            }
        }
    }
}

Write-Output ('doc-gate tests: ' + $passed + ' passed, ' + $failed + ' failed')
if ($failed -gt 0) { exit 1 }
exit 0
