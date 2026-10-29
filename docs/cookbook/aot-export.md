# cookbook：AOT 导出与冒烟

本页给出 Windows 空客户端 NativeAOT 导出、产物断言与冒烟步骤；一键脚本 `tools/aot-smoke/aot-smoke.ps1` 是权威实现，手动步骤与它一一对应。配方与已知坑见 [godot-aot](../reference/godot-aot.md)。

## 0. 前置自检

| 项 | 检查 |
|---|---|
| .NET SDK | `dotnet --list-sdks` 含 `10.0.401` |
| Windows SDK | 注册表 `HKLM\SOFTWARE\Microsoft\Windows Kits\Installed Roots` 的 `KitsRoot10` 下有 `Include` 目录（本机 `E:\Windows Kits\10\`） |
| MSVC C++ | `vswhere -latest -requires Microsoft.VisualStudio.Component.VC.Tools.x86.x64 -property installationPath` 有输出 |
| Godot | `D:\Program Files\Godot_v4.7.2-stable_mono_win64\Godot_v4.7.2-stable_mono_win64_console.exe` 存在 |
| 导出模板 | `%APPDATA%\Godot\export_templates\4.7.2.stable.mono\windows_release_x86_64.exe` 存在 |
| 工程配置 | `src/Mahjong.Client/project.godot` 无 `_mcp_game_helper` 与 `[editor_plugins]`，且有 `dotnet/project/solution_directory` |

缺任一项都停下补齐；脚本打印 `[aot-smoke] MISSING: ...` 后退出 1，不导出。

## 1. 一键跑

```sh
pwsh -NoProfile -File tools/aot-smoke/aot-smoke.ps1
```

可选参数：`-RepoRoot`（默认脚本上两级）、`-GodotExe`、`-TimeoutSeconds`（单次冒烟超时，默认 60）。

脚本顺序：前置自检 → 清并重建 `build/windows/` → 导出 → 复制 `content/` → 产物断言 → 3 次 `--smoke`。

期望 stdout 末段每次一行 `[aot-smoke] AOT STARTUP MS run=<n> value=<ms>`，随后 `[aot-smoke] AOT STARTUP MEDIAN MS value=<ms>` 与 `[aot-smoke] AOT SMOKE OK`，退出码 0；全过程写入 `build/aot-smoke.log`。

失败时打印 `[aot-smoke] FAIL: <原因>` 与导出日志末 40 行，退出 1。

## 2. 手动步骤

`<repo>` 是仓库根绝对路径；Godot CLI 的相对路径基准不可靠，路径一律写绝对。在 PowerShell 里执行。

1. 清空并重建导出目录（Godot 导出不会自建目标目录，缺目录会 exit 1）：

```powershell
Remove-Item -LiteralPath <repo>/build/windows -Recurse -Force -ErrorAction SilentlyContinue
New-Item -ItemType Directory <repo>/build/windows -Force | Out-Null
```

2. 导出（预设名 `Windows Desktop`，来自 `src/Mahjong.Client/export_presets.cfg`；命令行传的绝对导出路径覆盖预设里的 `export_path`）：

```powershell
& "D:\Program Files\Godot_v4.7.2-stable_mono_win64\Godot_v4.7.2-stable_mono_win64_console.exe" --headless --path <repo>/src/Mahjong.Client --export-release "Windows Desktop" <repo>/build/windows/Mahjong.exe
```

期望退出码 0，输出无 `ERROR:` 行。

3. 复制内容：先 `New-Item -ItemType Directory <repo>/build/windows/content -Force`，再把 `<repo>/content/` 整个复制进去；`rules.json`、`manifest.json` 与 6 个 `tables/*.bytes` 都要在，复制后表文件数要与源一致。

4. 断言产物：数据目录 `build/windows/data_Mahjong.Client_windows_x86_64/`（`data_` + `project.godot` 的 `dotnet/project/assembly_name` + `_windows_x86_64`）内没有 `coreclr.dll`、`hostfxr.dll`、`GodotSharp.dll`，且导出的 exe 与数据文件不含 `_mcp_game_helper` 字节串（pck 内嵌在 exe 里）。

5. 冒烟：`<repo>/build/windows/Mahjong.exe --log-file <repo>/build/aot-smoke-run.log --smoke`，单次 60 秒超时；`--log-file` 把引擎日志引到 `build/`，避免与已打开的编辑器抢 `user://` 日志文件。

6. 判定：退出码 0、stderr 只出现下面「引擎噪声」的两行、stdout 含 `CONTENT OK <64 位小写 hex>`（必须等于 `content/manifest.json` 的 `contentHash`）、`TABLES OK ` 与 `M0.1 SMOKE OK`。

7. 连跑 3 次，取「进程启动 → `CONTENT OK` 打印」的中位数，即 AOT 启动耗时；口径见 [perf-baseline-method](../../.agents/notes/implemented/testing/2026-10-29-perf-baseline-method.md)，计时命令就在本页第 4 节。

## 3. 判定口径

- 通过要同时满足：退出码 0、stderr 除引擎噪声外为空、`CONTENT OK` 的 hash 与 manifest 一致、`TABLES OK` 与 `M0.1 SMOKE OK` 都出现。
- AOT 证据是数据目录里没有三个托管运行库 dll；编辑器里跑的是 JIT Debug，不能当 AOT 证据。
- 导出产物出现 `_mcp_game_helper` 说明 `project.godot` 的 MCP 项没剥掉，见 [run](run.md) 第 5 节。
### 引擎噪声

本机 Godot 4.7.2 导出版启动时固定向 stderr 写这两行证书库错误，与内容、AOT 与冒烟标记无关；脚本逐行精确放行，其它任何 stderr 行判失败：

```text
ERROR: Failed to read the root certificate store.
   at: get_system_ca_certificates (platform/windows/os_windows.cpp:2582)
```

## 4. 基线复现

三项基线按「3 个独立进程各跑一次，取各进程中位的中位」测。数字记在 [M0.1 结果](../milestones/M0.1-工程底座与内核骨架.md) 的基线表；口径与边界见 [perf-baseline-method](../../.agents/notes/implemented/testing/2026-10-29-perf-baseline-method.md)。

### tick 与查表冷加载

```sh
dotnet test Mahjong.sln --filter Category=Benchmark -l "console;verbosity=detailed"
dotnet test Mahjong.sln --filter "FullyQualifiedName~TableColdLoadBenchmarkTests" -l "console;verbosity=detailed"
```

第一条同时打印 tick 与冷加载；第二条每进程只跑冷加载，报告注明口径。期望输出：

```text
M0.1-T5 tick bench round <n>: <ns> ns/step (<ms> ms, 1000000 steps)
M0.1-T5 tick bench: <ns> ns/step (median of 3; total <ms> ms)
M0.1-T6 table cold load round <n>: <ms> ms (new TableCatalog.Load)
M0.1-T6 table cold load: <ms> ms (median of 3)
```

### AOT 启动

先按 §1 或 §2 产出 `build/windows/Mahjong.exe`，再用这段 PowerShell 计时（3 个独立进程，停表在 stdout 首次出现 `CONTENT OK`，取中位）；把 `<repo>` 换成仓库根绝对路径：

```powershell
function Measure-AotStart {
    param([string]$Exe, [string]$LogFile)
    $psi = [System.Diagnostics.ProcessStartInfo]::new()
    $psi.FileName = $Exe
    $psi.Arguments = "--log-file `"$LogFile`" --smoke"
    $psi.RedirectStandardOutput = $true
    $psi.WorkingDirectory = Split-Path -Parent $Exe
    $watch = [System.Diagnostics.Stopwatch]::StartNew()
    $process = [System.Diagnostics.Process]::Start($psi)
    $elapsed = -1
    while ($null -ne ($line = $process.StandardOutput.ReadLine())) {
        if ($elapsed -lt 0 -and $line.Contains("CONTENT OK ")) { $elapsed = [int]$watch.ElapsedMilliseconds }
    }
    if (-not $process.WaitForExit(60000)) { $process.Kill($true); throw "timed out after 60 s" }
    if ($elapsed -lt 0) { throw ("no CONTENT OK line; exit=" + $process.ExitCode) }
    return $elapsed
}
$exe = "<repo>/build/windows/Mahjong.exe"
$times = @()
foreach ($run in 1..3) {
    $ms = Measure-AotStart -Exe $exe -LogFile "<repo>/build/aot-smoke-run.log"
    Write-Host ("run " + $run + ": " + $ms + " ms")
    $times += $ms
}
Write-Host ("median: " + ($times | Sort-Object)[1] + " ms")
```

期望输出 3 行 `run <n>: <ms> ms` 与一行 `median: <ms> ms`；stderr 不重定向、噪声直接打印，判据在 §1 脚本。
## 5. 失败处置

| 现象 | 处置 |
|---|---|
| 步骤 0 打印 MISSING | 按缺项装齐 SDK / Windows SDK / MSVC / Godot / 模板，或先剥掉 `project.godot` 的 MCP 项 |
| 导出 exit 1 且日志含导出路径不存在 | 先建 `build/windows/` 再导出（第 1 步两条命令） |
| 导出非 0 或日志有 `ERROR:` | 先 `dotnet build Mahjong.sln -warnaserror` 排除编译错误；再确认 `dotnet/project/solution_directory` 与预设名 |
| 数据目录出现托管 dll | 检查 `Mahjong.Client.csproj` 的 `PublishAot` 与 4 个 `TrimmerRootAssembly`，重跑导出 |
| exe 退出 1 且 stderr 有 `M0.1 SMOKE FAILED` | 内容目录不在 exe 旁或 `content/tables` 缺失，按第 3 步补齐后重跑 |
| 60 秒超时 | 冒烟分支没走到 30 帧退出，检查 `Startup.cs` 的 `--smoke` 分支与 `GetTree().Quit()` |
| exe 启动即崩且无 stdout | 裁剪把 GodotSharp 的构造参数裁空，确认 4 个 `TrimmerRootAssembly` 齐全 |

`.github/workflows/ci.yml` 只跑构建、单测、门禁与钩子；AOT 冒烟在开发机跑。
