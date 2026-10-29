<#
.SYNOPSIS
    Export the Godot client for Windows and smoke-test the exported NativeAOT build.

.DESCRIPTION
    M0.1 T-6 baseline close-out tool (docs/milestones/M0.1/T-6-*.md section 1).

    Steps:
      0. AOT toolchain preflight: dotnet SDK 10.0.401, Windows SDK KitsRoot10 from the
         registry, vswhere MSVC x86/x64 tools, the Godot console executable, the
         4.7.2.stable.mono export templates, and no Godot MCP write-back in project.godot.
      1. Clean <repo>/build/windows.
      2. Godot release export with absolute paths; the export log stays under build/.
      3. Copy <repo>/content (rules.json + tables/) next to the exported executable.
      4. Assert the export is a NativeAOT image (no coreclr.dll, hostfxr.dll,
         GodotSharp.dll) and carries no MCP game helper (the pck is embedded in the exe).
      5. Run Mahjong.exe --smoke three times, 60 s timeout per run by default.
      6. Assert CONTENT OK <64 lowercase hex> (equal to content/manifest.json contentHash),
         TABLES OK and M0.1 SMOKE OK, exit code 0, and stderr either empty or exactly the
         documented two-line engine certificate-store error (case-sensitive whole-text match,
         fixed order); record the time from process start to the CONTENT OK line and print
         the median.
      7. On any failure print the export log tail plus the last stdout/stderr and exit 1.

    The script and its markers are ASCII only. Exit code 0 means every assertion passed.

.PARAMETER RepoRoot
    Repository root. Defaults to the grandparent directory of this script.

.PARAMETER GodotExe
    Godot 4.7.2 mono console executable used for the headless export.

.PARAMETER TimeoutSeconds
    Timeout in seconds for one --smoke run.
#>
[CmdletBinding()]
param(
    [string]$RepoRoot = (Split-Path -Parent (Split-Path -Parent $PSScriptRoot)),
    [string]$GodotExe = 'D:\Program Files\Godot_v4.7.2-stable_mono_win64\Godot_v4.7.2-stable_mono_win64_console.exe',
    [int]$TimeoutSeconds = 60
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$script:RepoRoot = (Resolve-Path -LiteralPath $RepoRoot).Path
$script:BuildDir = Join-Path $script:RepoRoot 'build'
$script:WindowsDir = Join-Path $script:BuildDir 'windows'
$script:ExePath = Join-Path $script:WindowsDir 'Mahjong.exe'
$script:ClientDir = Join-Path $script:RepoRoot 'src\Mahjong.Client'
$script:ContentDir = Join-Path $script:RepoRoot 'content'
$script:LogPath = Join-Path $script:BuildDir 'aot-smoke.log'
$script:ExportLogPath = Join-Path $script:BuildDir 'aot-smoke-export.log'
$script:SmokeLogPath = Join-Path $script:BuildDir 'aot-smoke-run.log'
$script:ExportTimeoutSeconds = 900
$script:RunCount = 3
$script:AsciiNeedle = '_mcp_game_helper'
$script:_lastStdout = ''
$script:_lastStderr = ''

# Godot 4.7.2 exported builds on this host print exactly this two-line engine error while
# loading the Windows certificate store at startup. It is unrelated to content, AOT and the
# smoke markers. The whole stderr text must match this anchored, case-sensitive pattern
# (exactly two lines in this order, trailing CRLF included); any other byte fails the run.
$script:EngineStderrPattern = '^ERROR: Failed to read the root certificate store\.\r\n   at: get_system_ca_certificates \(platform/windows/os_windows\.cpp:[0-9]+\)\r\n\z'

New-Item -ItemType Directory -Path $script:BuildDir -Force | Out-Null
Set-Content -LiteralPath $script:LogPath -Value ('aot-smoke log ' + (Get-Date -Format 'yyyy-MM-dd HH:mm:ss')) -Encoding utf8

function Write-Log {
    param([string]$Message)

    $line = '[aot-smoke] ' + $Message
    Add-Content -LiteralPath $script:LogPath -Value $line -Encoding utf8
    Write-Host $line
}

function Stop-Smoke {
    param([string]$Message)

    Write-Log ('FAIL: ' + $Message)

    if (Test-Path -LiteralPath $script:ExportLogPath) {
        Write-Log 'export log tail (40 lines):'
        foreach ($line in @(Get-Content -LiteralPath $script:ExportLogPath -Tail 40 -Encoding utf8)) {
            Write-Log ('  export | ' + $line)
        }
    }

    if ($script:_lastStdout.Length -gt 0) {
        Write-Log 'last stdout:'
        foreach ($line in ($script:_lastStdout -split "`n")) {
            Write-Log ('  stdout | ' + $line)
        }
    }

    if ($script:_lastStderr.Length -gt 0) {
        Write-Log 'last stderr:'
        foreach ($line in ($script:_lastStderr -split "`n")) {
            Write-Log ('  stderr | ' + $line)
        }
    }

    exit 1
}

function Test-FileContainsAscii {
    param(
        [Parameter(Mandatory = $true)][string]$Path,
        [Parameter(Mandatory = $true)][string]$Needle
    )

    # Latin1 maps every byte to one char, so IndexOf finds an ASCII byte sequence.
    $encoding = [System.Text.Encoding]::Latin1
    $stream = [System.IO.File]::OpenRead($Path)
    try {
        $chunkSize = 4MB
        $buffer = New-Object byte[] ($chunkSize + $Needle.Length)
        $carry = 0
        while (($read = $stream.Read($buffer, $carry, $chunkSize)) -gt 0) {
            $total = $carry + $read
            $text = $encoding.GetString($buffer, 0, $total)
            if ($text.IndexOf($Needle, [System.StringComparison]::Ordinal) -ge 0) {
                return $true
            }

            $carry = [Math]::Min($Needle.Length - 1, $total)
            if ($carry -gt 0) {
                [System.Array]::Copy($buffer, $total - $carry, $buffer, 0, $carry)
            }
        }
    }
    finally {
        $stream.Dispose()
    }

    return $false
}

function New-GodotProcessStartInfo {
    param(
        [string]$FileName,
        [string[]]$Arguments,
        [string]$WorkingDirectory
    )

    $psi = [System.Diagnostics.ProcessStartInfo]::new()
    $psi.FileName = $FileName
    $psi.UseShellExecute = $false
    $psi.RedirectStandardOutput = $true
    $psi.RedirectStandardError = $true
    $psi.WorkingDirectory = $WorkingDirectory
    foreach ($argument in $Arguments) {
        [void]$psi.ArgumentList.Add($argument)
    }

    return $psi
}

Write-Log ('repo root: ' + $script:RepoRoot)
Write-Log ('godot exe: ' + $GodotExe)

# ---------------------------------------------------------------- step 0
Write-Log 'step 0: AOT toolchain preflight'
$missing = New-Object 'System.Collections.Generic.List[string]'

$dotnet = Get-Command dotnet -ErrorAction SilentlyContinue
if ($null -eq $dotnet) {
    $missing.Add('dotnet CLI is not on PATH')
}
else {
    $sdkLines = @(& dotnet --list-sdks)
    $sdkLine = ($sdkLines | Where-Object { $_ -match '^\s*10\.0\.401\s+\[' } | Select-Object -First 1)
    if ($null -eq $sdkLine) {
        $missing.Add('dotnet --list-sdks does not list 10.0.401 (global.json requires it)')
    }
    else {
        Write-Log ('dotnet SDK: ' + $sdkLine.Trim())
    }
}

$kitsRoot10 = $null
try {
    $kitsRoot10 = [string](Get-ItemProperty -LiteralPath 'HKLM:\SOFTWARE\Microsoft\Windows Kits\Installed Roots' -ErrorAction Stop).KitsRoot10
}
catch {
    $missing.Add('registry HKLM\SOFTWARE\Microsoft\Windows Kits\Installed Roots does not expose KitsRoot10')
}

if (-not [string]::IsNullOrWhiteSpace($kitsRoot10)) {
    Write-Log ('Windows Kits KitsRoot10: ' + $kitsRoot10)
    $sdkIncludeDir = Join-Path $kitsRoot10 'Include'
    if (-not (Test-Path -LiteralPath $sdkIncludeDir -PathType Container)) {
        $missing.Add('Windows SDK Include directory missing under KitsRoot10: ' + $sdkIncludeDir)
    }
    else {
        $sdkVersions = @(Get-ChildItem -LiteralPath $sdkIncludeDir -Directory | Select-Object -ExpandProperty Name)
        Write-Log ('Windows SDK versions: ' + ($sdkVersions -join ', '))
    }
}

$programFilesX86 = [System.Environment]::GetEnvironmentVariable('ProgramFiles(x86)')
if ([string]::IsNullOrWhiteSpace($programFilesX86)) {
    $programFilesX86 = 'C:\Program Files (x86)'
}

$vswhere = Join-Path $programFilesX86 'Microsoft Visual Studio\Installer\vswhere.exe'
if (-not (Test-Path -LiteralPath $vswhere -PathType Leaf)) {
    $missing.Add('vswhere.exe not found: ' + $vswhere)
}
else {
    $vsPathLines = @(& $vswhere -latest -requires Microsoft.VisualStudio.Component.VC.Tools.x86.x64 -property installationPath)
    $vsPath = ($vsPathLines -join "`n").Trim()
    if ($vsPath.Length -eq 0) {
        $missing.Add('vswhere found no Visual Studio with Microsoft.VisualStudio.Component.VC.Tools.x86.x64')
    }
    else {
        Write-Log ('vswhere MSVC x86/x64 tools: ' + $vsPath)
        $msvcRoot = Join-Path $vsPath 'VC\Tools\MSVC'
        if (Test-Path -LiteralPath $msvcRoot -PathType Container) {
            $msvcVersions = @(Get-ChildItem -LiteralPath $msvcRoot -Directory | Select-Object -ExpandProperty Name)
            Write-Log ('MSVC toolset versions: ' + ($msvcVersions -join ', '))
        }
    }
}

if (-not (Test-Path -LiteralPath $GodotExe -PathType Leaf)) {
    $missing.Add('Godot console executable not found: ' + $GodotExe)
}
else {
    Write-Log 'Godot console executable: found'
}

$appData = [System.Environment]::GetEnvironmentVariable('APPDATA')
if ([string]::IsNullOrWhiteSpace($appData)) {
    $missing.Add('APPDATA is not set; cannot locate the Godot export templates')
}
else {
    $templateDir = Join-Path $appData 'Godot\export_templates\4.7.2.stable.mono'
    if (-not (Test-Path -LiteralPath $templateDir -PathType Container)) {
        $missing.Add('Godot export template directory not found: ' + $templateDir)
    }
    else {
        Write-Log ('Godot export templates: ' + $templateDir)
        $templateExe = Join-Path $templateDir 'windows_release_x86_64.exe'
        if (-not (Test-Path -LiteralPath $templateExe -PathType Leaf)) {
            $missing.Add('release template not found: ' + $templateExe)
        }
    }
}

$projectGodotPath = Join-Path $script:ClientDir 'project.godot'
if (-not (Test-Path -LiteralPath $projectGodotPath -PathType Leaf)) {
    $missing.Add('project.godot not found: ' + $projectGodotPath)
}
else {
    $projectGodot = Get-Content -LiteralPath $projectGodotPath -Raw -Encoding utf8
    if ($projectGodot -match '_mcp_game_helper' -or $projectGodot -match '(?m)^\[editor_plugins\]') {
        $missing.Add('project.godot carries Godot MCP entries (autoload/_mcp_game_helper or [editor_plugins]); run: git -C <repo> checkout -- src/Mahjong.Client/project.godot src/Mahjong.Client/Startup.tscn')
    }
    else {
        Write-Log 'project.godot: no MCP autoload or editor plugin entry'
    }

    if ($projectGodot -notmatch 'project/solution_directory') {
        $missing.Add('project.godot is missing dotnet/project/solution_directory, so the Godot .NET export cannot find Mahjong.sln')
    }
}

if ($missing.Count -gt 0) {
    foreach ($item in $missing) {
        Write-Log ('MISSING: ' + $item)
    }

    Write-Log 'preflight failed; the AOT export was not attempted'
    exit 1
}

Write-Log 'preflight OK'

# ---------------------------------------------------------------- step 1
Write-Log 'step 1: clean build/windows'
if (Test-Path -LiteralPath $script:WindowsDir) {
    Remove-Item -LiteralPath $script:WindowsDir -Recurse -Force
}

New-Item -ItemType Directory -Path $script:WindowsDir -Force | Out-Null
Write-Log ('clean target: ' + $script:WindowsDir)

# ---------------------------------------------------------------- step 2
Write-Log 'step 2: godot release export'
$exportPsi = New-GodotProcessStartInfo -FileName $GodotExe -WorkingDirectory $script:RepoRoot -Arguments @(
    '--headless',
    '--path', $script:ClientDir,
    '--export-release', 'Windows Desktop',
    $script:ExePath
)
$exportProcess = [System.Diagnostics.Process]::Start($exportPsi)
$exportOutTask = $exportProcess.StandardOutput.ReadToEndAsync()
$exportErrTask = $exportProcess.StandardError.ReadToEndAsync()
if (-not $exportProcess.WaitForExit($script:ExportTimeoutSeconds * 1000)) {
    try {
        $exportProcess.Kill($true)
    }
    catch {
    }

    Stop-Smoke ('godot export timed out after ' + $script:ExportTimeoutSeconds + ' s')
}

$exportOut = $exportOutTask.GetAwaiter().GetResult()
$exportErr = $exportErrTask.GetAwaiter().GetResult()
$exportText = (($exportOut + "`n" + $exportErr).Trim())
Set-Content -LiteralPath $script:ExportLogPath -Value $exportText -Encoding utf8
Write-Log ('export exit code: ' + $exportProcess.ExitCode)
Write-Log ('export log: ' + $script:ExportLogPath)
if ($exportProcess.ExitCode -ne 0) {
    Stop-Smoke ('godot export exited with code ' + $exportProcess.ExitCode)
}

$exportErrors = @($exportText -split "`n" | Where-Object { $_ -match 'ERROR:' })
if ($exportErrors.Count -gt 0) {
    Stop-Smoke ('godot export log contains ' + $exportErrors.Count + ' ERROR line(s); first: ' + $exportErrors[0])
}

if (-not (Test-Path -LiteralPath $script:ExePath -PathType Leaf)) {
    Stop-Smoke ('exported executable not found: ' + $script:ExePath)
}

# The data directory is named after dotnet/project/assembly_name (Mahjong.Client), not the exe name.
$dataDirs = @(Get-ChildItem -LiteralPath $script:WindowsDir -Directory -Filter 'data_*_windows_x86_64')
if ($dataDirs.Count -ne 1) {
    Stop-Smoke ('expected exactly one data_*_windows_x86_64 directory under ' + $script:WindowsDir + ', found ' + $dataDirs.Count)
}

$dataDir = $dataDirs[0].FullName
Write-Log ('export data directory: ' + $dataDirs[0].Name)
Write-Log ('exported executable: ' + $script:ExePath)

# ---------------------------------------------------------------- step 3
Write-Log 'step 3: copy content/ next to the exported executable'
$targetContent = Join-Path $script:WindowsDir 'content'
$requiredContent = @(
    (Join-Path $script:ContentDir 'rules.json'),
    (Join-Path $script:ContentDir 'manifest.json')
)
foreach ($required in $requiredContent) {
    if (-not (Test-Path -LiteralPath $required -PathType Leaf)) {
        Stop-Smoke ('required content file missing in the repository: ' + $required)
    }
}

$sourceTables = Join-Path $script:ContentDir 'tables'
$sourceTableCount = @(Get-ChildItem -LiteralPath $sourceTables -Filter '*.bytes' -File -ErrorAction SilentlyContinue).Count
if ($sourceTableCount -eq 0) {
    Stop-Smoke ('no .bytes table files under ' + $sourceTables)
}

New-Item -ItemType Directory -Path $targetContent -Force | Out-Null
Copy-Item -Path (Join-Path $script:ContentDir '*') -Destination $targetContent -Recurse -Force
$targetTableCount = @(Get-ChildItem -LiteralPath (Join-Path $targetContent 'tables') -Filter '*.bytes' -File).Count
if ($targetTableCount -ne $sourceTableCount) {
    Stop-Smoke ('content copy is incomplete: source tables ' + $sourceTableCount + ', copied tables ' + $targetTableCount)
}

if (-not (Test-Path -LiteralPath (Join-Path $targetContent 'rules.json') -PathType Leaf)) {
    Stop-Smoke 'rules.json was not copied into the export'
}

Write-Log ('content copied: rules.json + manifest.json + ' + $targetTableCount + ' table file(s)')

# ---------------------------------------------------------------- step 4
Write-Log 'step 4: assert NativeAOT image and no MCP game helper'
foreach ($forbiddenName in @('coreclr.dll', 'hostfxr.dll', 'GodotSharp.dll')) {
    $hits = @(Get-ChildItem -LiteralPath $dataDir -Recurse -File -Filter $forbiddenName -ErrorAction SilentlyContinue)
    if ($hits.Count -gt 0) {
        Stop-Smoke ('forbidden runtime file in the export data directory: ' + $hits[0].FullName)
    }
}

Write-Log 'no coreclr.dll, hostfxr.dll or GodotSharp.dll in the export data directory'

$scannedFiles = 0
$scannedBytes = 0
foreach ($file in @(Get-ChildItem -LiteralPath $script:WindowsDir -Recurse -File)) {
    if ($file.FullName.StartsWith($targetContent, [System.StringComparison]::OrdinalIgnoreCase)) {
        continue
    }

    if (Test-FileContainsAscii -Path $file.FullName -Needle $script:AsciiNeedle) {
        Stop-Smoke ('exported file contains ' + $script:AsciiNeedle + ': ' + $file.FullName)
    }

    $scannedFiles++
    $scannedBytes += $file.Length
}

Write-Log ('scanned ' + $scannedFiles + ' exported file(s), ' + $scannedBytes + ' bytes: ' + $script:AsciiNeedle + ' not found (the pck is embedded in ' + [System.IO.Path]::GetFileName($script:ExePath) + ')')

# ---------------------------------------------------------------- step 5 / 6
$manifestPath = Join-Path $script:ContentDir 'manifest.json'
$manifest = Get-Content -LiteralPath $manifestPath -Raw -Encoding utf8 | ConvertFrom-Json
$expectedHash = [string]$manifest.contentHash
if ($expectedHash -notmatch '^[0-9a-f]{64}$') {
    Stop-Smoke ('content/manifest.json contentHash is not 64 lowercase hex: ' + $expectedHash)
}

Write-Log ('expected contentHash: ' + $expectedHash)
Write-Log ('step 5: run --smoke ' + $script:RunCount + ' time(s), timeout ' + $TimeoutSeconds + ' s per run')
$timings = New-Object 'System.Collections.Generic.List[int]'

for ($run = 1; $run -le $script:RunCount; $run++) {
    # --log-file keeps the engine log out of the user:// profile shared with an open editor,
    # which would otherwise add file-rotation errors to stderr on this host.
    $psi = New-GodotProcessStartInfo -FileName $script:ExePath -WorkingDirectory $script:WindowsDir -Arguments @(
        '--log-file', $script:SmokeLogPath,
        '--smoke'
    )
    $stopwatch = [System.Diagnostics.Stopwatch]::StartNew()
    $process = [System.Diagnostics.Process]::Start($psi)
    $stderrTask = $process.StandardError.ReadToEndAsync()
    $lines = New-Object 'System.Collections.Generic.List[string]'
    $firstContentOkMs = -1
    $timedOut = $false

    try {
        $lineTask = $process.StandardOutput.ReadLineAsync()
        while ($true) {
            $remaining = ($TimeoutSeconds * 1000) - [int]$stopwatch.ElapsedMilliseconds
            if ($remaining -le 0) {
                $timedOut = $true
                break
            }

            $finished = $lineTask.Wait($remaining)
            if (-not $finished) {
                $timedOut = $true
                break
            }

            $line = $lineTask.Result
            if ($null -eq $line) {
                break
            }

            $lines.Add($line)
            if ($firstContentOkMs -lt 0 -and $line.Contains('CONTENT OK ')) {
                $firstContentOkMs = [int]$stopwatch.ElapsedMilliseconds
            }

            $lineTask = $process.StandardOutput.ReadLineAsync()
        }
    }
    finally {
        if ($timedOut) {
            try {
                $process.Kill($true)
            }
            catch {
            }
        }
    }

    [void]$process.WaitForExit(5000)
    $stdoutText = ($lines -join "`n")
    $stderrText = ''
    try {
        $stderrText = $stderrTask.GetAwaiter().GetResult()
    }
    catch {
        $stderrText = ''
    }

    $script:_lastStdout = $stdoutText
    $script:_lastStderr = $stderrText
    $exitCode = -1
    if ($process.HasExited) {
        $exitCode = $process.ExitCode
    }

    Write-Log ('run ' + $run + ': exit=' + $exitCode + ' timedOut=' + $timedOut + ' firstContentOkMs=' + $firstContentOkMs + ' stdoutLines=' + $lines.Count + ' stderrBytes=' + $stderrText.Length)
    foreach ($line in $lines) {
        Write-Log ('  stdout | ' + $line)
    }

    # Log stderr verbatim first, then require the whole text to match the documented pattern.
    foreach ($stderrLine in ($stderrText -split "`r?`n")) {
        if ($stderrLine.Length -gt 0) {
            Write-Log ('  stderr | ' + $stderrLine)
        }
    }

    if ($timedOut) {
        Stop-Smoke ('smoke run ' + $run + ' timed out after ' + $TimeoutSeconds + ' s')
    }

    if ($exitCode -ne 0) {
        Stop-Smoke ('smoke run ' + $run + ' exited with code ' + $exitCode)
    }

    if ($stderrText.Length -gt 0) {
        if (-not ($stderrText -cmatch $script:EngineStderrPattern)) {
            Stop-Smoke ('smoke run ' + $run + ' wrote ' + $stderrText.Length + ' stderr byte(s) that are not exactly the documented two-line certificate-store error')
        }

        Write-Log ('engine-stderr: matched the documented two-line certificate-store error exactly (' + $stderrText.Length + ' byte(s))')
    }

    if ($firstContentOkMs -lt 0) {
        Stop-Smoke ('smoke run ' + $run + ' never printed CONTENT OK')
    }

    if ($stdoutText -notmatch 'CONTENT OK ([0-9a-f]{64})') {
        Stop-Smoke ('smoke run ' + $run + ' has no "CONTENT OK <64 hex>" line')
    }

    $actualHash = $Matches[1]
    if ($actualHash -ne $expectedHash) {
        Stop-Smoke ('contentHash mismatch: manifest ' + $expectedHash + ', exported build ' + $actualHash)
    }

    if (-not $stdoutText.Contains('TABLES OK ')) {
        Stop-Smoke ('smoke run ' + $run + ' did not print the TABLES OK line')
    }

    if (-not $stdoutText.Contains('M0.1 SMOKE OK')) {
        Stop-Smoke ('smoke run ' + $run + ' did not print M0.1 SMOKE OK')
    }

    $timings.Add($firstContentOkMs)
    Write-Log ('AOT STARTUP MS run=' + $run + ' value=' + $firstContentOkMs)
}

$sortedTimings = @($timings | Sort-Object)
$median = $sortedTimings[[int][Math]::Floor($sortedTimings.Count / 2)]
Write-Log ('AOT STARTUP MEDIAN MS value=' + $median)
Write-Log 'AOT SMOKE OK'
exit 0
