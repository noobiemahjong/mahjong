# cookbook：AOT 导出与冒烟

本页给出 Windows 客户端 NativeAOT 导出、产物断言与三类独立 exe 运行（`--smoke`、`--autoplay`、GUI 启动）的步骤与阈值；一键脚本 `tools/aot-smoke/aot-smoke.ps1` 是权威实现，手动步骤与它一一对应。AOT 配方与已知坑见 [godot-aot](../reference/godot-aot.md)，客户端启动分支与标记见 [client](../subsystems/client.md)，最近一次基线与数字见 [M0.2 结果](../milestones/M0.2-单机可玩闭环.md) 的 T-7 行。

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

可选参数：`-RepoRoot`（默认脚本上两级）、`-GodotExe`、`-TimeoutSeconds`（单次运行超时，默认 60；导出超时固定 900）。脚本顺序：前置自检 → 清并重建 `build/windows/` → 导出 → 复制 `content/` → 产物断言 → `--smoke` ×3 → `--autoplay` ×3 → GUI 启动 ×3。全过程写 `build/aot-smoke.log`，导出、smoke、autoplay 与 GUI 的引擎日志分别写 `build/aot-smoke-export.log`、`build/aot-smoke-run.log`、`build/aot-smoke-autoplay.log`、`build/aot-smoke-gui.log`（`--log-file` 让引擎日志落 `build/`，避免与已打开的编辑器抢 `user://` 日志文件）。

期望末段（每行带 `[aot-smoke]` 前缀，按出现顺序）：

```text
AOT STARTUP MS run=<n> value=<ms>
AOT STARTUP MEDIAN MS value=<ms>
AOT AUTOPLAY READY MS run=<n> value=<ms>
AOT AUTOPLAY TOTAL MS run=<n> value=<ms>
AOT AUTOPLAY STARTUP MEDIAN MS value=<ms>
AOT AUTOPLAY TOTAL MEDIAN MS value=<ms>
AOT AUTOPLAY OK
AOT GUI STARTUP MS run=<n> value=<ms>
AOT GUI STARTUP MEDIAN MS value=<ms>
AOT SMOKE OK
```

退出码 0 = 前置、导出、产物断言与三类运行全部通过；1 = 任一断言失败，原因在 `build/aot-smoke.log` 的 `FAIL: ...` 与最后一轮 stdout/stderr 里。

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

期望退出码 0、输出无 `ERROR:` 行，且 `build/windows/` 下恰好一个 `data_*_windows_x86_64` 目录。

3. 复制内容：先 `New-Item -ItemType Directory <repo>/build/windows/content -Force`，再把 `<repo>/content/` 整个复制进去；`rules.json`、`manifest.json` 与 6 个 `tables/*.bytes` 都要在，复制后表文件数要与源一致。

4. 断言产物：数据目录 `build/windows/data_Mahjong.Client_windows_x86_64/`（`data_` + `project.godot` 的 `dotnet/project/assembly_name` + `_windows_x86_64`）内没有 `coreclr.dll`、`hostfxr.dll`、`GodotSharp.dll`；导出的非内容文件不含 `_mcp_game_helper` 字节串（pck 内嵌在 exe 里）。

5. `--smoke` ×3：`build/windows/Mahjong.exe --log-file build/aot-smoke-run.log --smoke`，单次 60 秒超时；计时终点是 stdout 首次出现 `CONTENT OK`。

6. `--autoplay` ×3：`build/windows/Mahjong.exe --log-file build/aot-smoke-autoplay.log --autoplay`，单次 60 秒超时；计时终点是 `AUTOPLAY READY` 与 `GAME OK`。

7. GUI 启动 ×3：`build/windows/Mahjong.exe --log-file build/aot-smoke-gui.log`（无其它参数），等 stdout 出现 `LOCAL READY`（60 秒超时）后记录 launch 耗时并结束进程，该路径退出码不参与断言；判据与 3 次取中位见 §3。

## 3. 判定口径

- 三类运行都要同时满足退出码（GUI 除外）、stdout 标记与 stderr 白名单；stderr 只允许 §3.1 的两行引擎噪声，多一个字节即失败。
- `--smoke`：退出码 0；stdout 含 `CONTENT OK <64 位小写 hex>`（必须等于 `content/manifest.json` 的 `contentHash`）、`TABLES OK ` 与 `M0.1 SMOKE OK`；打印 `AOT STARTUP MS` 与中位。
- `--autoplay`：退出码 0；stdout 恰 1 行 `AUTOPLAY READY`、恰 1 行整行 `GAME OK rounds=8`，且不出现 `LOCAL READY`（证明跳过 UI）；每次运行都断言 launch→`AUTOPLAY READY` <3000ms、launch→`GAME OK` <5000ms；3 次中位另打印。
- GUI：整段 stdout 恰 1 行 `LOCAL READY` 且不超时；3 次中位 launch→`LOCAL READY` <3000ms；进程由脚本结束，退出码不参与断言。
- AOT 证据是数据目录没有三个托管运行库 dll、exe 可独立跑完对局；编辑器里跑的是 JIT Debug，不能当 AOT 证据，headless JIT 墙钟只作功能验证，数字只认导出 exe 的进程墙钟（口径见 [Note 基线口径](../../.agents/notes/implemented/feature/2026-10-31-autoplay-and-perf-baseline.md)）。

### 3.1 引擎噪声

本机 Godot 4.7.2 导出版启动时固定向 stderr 写这两行证书库错误（本机 125 字节），与内容、AOT 与冒烟标记无关；脚本整段精确匹配（行尾 `\r\n`、`os_windows.cpp` 的行号按 `[0-9]+` 通配），其它任何 stderr 行判失败：

```text
ERROR: Failed to read the root certificate store.
   at: get_system_ca_certificates (platform/windows/os_windows.cpp:2582)
```

## 4. 证据与复现

- 脚本日志与引擎日志都在 gitignored 的 `build/`；归档时把 `build/aot-smoke.log` 的 MARKER 段与各类 `value=` 行摘到 `.reviews/<任务>/evidence/`，结果页只引用结论数字。
- 基线数字记在 [M0.2 结果](../milestones/M0.2-单机可玩闭环.md) 的 T-7 行；tick p99 与内存基准的复现见 [cookbook/build](build.md) §3；导出前先 `dotnet build Mahjong.sln -warnaserror`，CI 只跑构建/单测/门禁/钩子，AOT 冒烟在开发机跑。

## 5. 失败处置

| 现象 | 处置 |
|---|---|
| 步骤 0 打印 MISSING | 按缺项装齐 SDK / Windows SDK / MSVC / Godot / 模板，或先剥掉 `project.godot` 的 MCP 项 |
| 导出 exit 1 且日志含导出路径不存在 | 先建 `build/windows/` 再导出（第 1 步两条命令） |
| 导出非 0 或日志有 `ERROR:` | 先 `dotnet build Mahjong.sln -warnaserror` 排除编译错误；再确认 `dotnet/project/solution_directory` 与预设名，细节看 `%APPDATA%\Godot\mono\build_logs` |
| 数据目录出现托管 dll | 检查 `Mahjong.Client.csproj` 的 `PublishAot` 与 4 个 `TrimmerRootAssembly`，重跑导出 |
| 导出产物含 `_mcp_game_helper` | `project.godot` 的 MCP 项没剥掉，见 [run](run.md) 第 5 节 |
| `--smoke` 退出 1 且 stderr 有 `M0.1 SMOKE FAILED` | 内容目录不在 exe 旁或 `content/tables` 缺失，按第 3 步补齐后重跑 |
| `--autoplay` 退出 1 或只有 `AUTOPLAY FAILED` | 看失败原因（内容加载、步数上限 1,000,000、八局校验）；`GAME OK` 不出现即失败 |
| GUI 等不到 `LOCAL READY` | 首帧没走完（内容加载失败早退、场景/控件缺失）；看 `build/aot-smoke-gui.log` 与 stdout 末行 |
| 60 秒超时 | 冒烟分支没走到 30 帧退出、autoplay 未收敛或 GUI 首帧未渲染，检查 `Startup`/`LocalGame` 与 `GetTree().Quit()` |
| exe 启动即崩且无 stdout | 裁剪把 GodotSharp 的构造参数裁空，确认 4 个 `TrimmerRootAssembly` 齐全 |
