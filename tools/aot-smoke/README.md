# aot-smoke（客户端 AOT 导出与冒烟）

Windows 释放导出 + NativeAOT 独立 exe 冒烟。对应 [T-6 §1](../../docs/milestones/M0.1/T-6-基线收口.md)；脚本与输出标记纯 ASCII，本文件只写用法与判据。

## 前置

| 项 | 判据 |
|---|---|
| .NET SDK | `dotnet --list-sdks` 含 `10.0.401`（[global.json](../../global.json)） |
| Windows SDK | 注册表 `HKLM\SOFTWARE\Microsoft\Windows Kits\Installed Roots` 的 `KitsRoot10` 存在且含 `Include\<version>` |
| MSVC | `vswhere -latest -requires Microsoft.VisualStudio.Component.VC.Tools.x86.x64` 有输出 |
| Godot | 4.7.2 mono 控制台 exe；`%APPDATA%` 下 `4.7.2.stable.mono` 模板 |
| 工程 | `project.godot` 无 `autoload/_mcp_game_helper`、无 `[editor_plugins]`，且有 `dotnet/project/solution_directory` |

缺任一项打印 `MISSING:` 并退出 1，不导出、不伪造 AOT 通过。

## 用法

```powershell
pwsh -File tools/aot-smoke/aot-smoke.ps1
pwsh -File tools/aot-smoke/aot-smoke.ps1 -RepoRoot E:\learn\mahjong -GodotExe "D:\Program Files\Godot_v4.7.2-stable_mono_win64\Godot_v4.7.2-stable_mono_win64_console.exe" -TimeoutSeconds 60
```

参数：`-RepoRoot`（默认脚本上两级）、`-GodotExe`（本机默认路径）、`-TimeoutSeconds`（单次冒烟超时，默认 60）。脚本自行清空 `build/windows`。

## 产出与证据

| 路径 | 内容 |
|---|---|
| `build/windows/Mahjong.exe` | 导出 exe，pck 内嵌 |
| `build/windows/data_<assembly>_windows_x86_64/` | NativeAOT 原生库与 pdb（本机 `Mahjong.Client`） |
| `build/windows/content/` | 内容目录副本（`rules.json` + `tables/`） |
| `build/aot-smoke.log` | 全流程日志 |
| `build/aot-smoke-export.log` | Godot 导出输出 |
| `build/aot-smoke-run.log` | 冒烟进程引擎日志（`--log-file` 指定） |

## 退出码

| 码 | 含义 |
|---|---|
| 0 | 前置、导出、产物断言与 3 次冒烟全部通过 |
| 1 | 任一前置缺失、导出失败、AOT/MCP 产物断言失败或冒烟断言失败；原因在 `build/aot-smoke.log` |

## 断言口径

1. 导出退出码 0，导出日志无 `ERROR:` 行，`data_*_windows_x86_64` 恰有一个。
2. 数据目录递归无 `coreclr.dll`、`hostfxr.dll`、`GodotSharp.dll`（即 JIT 产物）。
3. 导出目录内非 `content/` 文件逐字节扫描无 `_mcp_game_helper`（pck 内嵌在 exe；`export_presets.cfg` 的 `exclude_filter` 排除 `addons/godot_ai/*`）。
4. 每次冒烟：退出码 0；stdout 含 `CONTENT OK <64 位小写 hex>`（等于 `content/manifest.json` 的 `contentHash`）、`TABLES OK ...`、`M0.1 SMOKE OK`；stderr 为空，或整段恰好是下节「引擎噪声」（大小写敏感、恰好两行、固定顺序）。
5. 计时到「进程启动 → 首次打印 `CONTENT OK`」，3 次取中位，输出 `AOT STARTUP MS run=<n> value=<ms>` 与 `AOT STARTUP MEDIAN MS value=<ms>`。

## 已知引擎噪声

Godot 4.7.2 导出版在本机启动时固定向 stderr 写这一条证书存储错误，与内容/AOT 无关；脚本对整段 stderr 做大小写敏感的全串匹配，恰好这两行且固定顺序才放行，其它任何字节判红：

```text
ERROR: Failed to read the root certificate store.
   at: get_system_ca_certificates (platform/windows/os_windows.cpp:2582)
```

编辑器占用 `user://logs/godot.log` 时会有两条日志写入错误；脚本用 `--log-file` 把日志引到 `build/`，与编辑器是否运行无关。

## 失败处置

- 前置缺失：按 `MISSING:` 补齐；缺 SDK/模板不要绕过。
- 导出失败：看 `build/aot-smoke-export.log` 尾部与 `%APPDATA%\Godot\mono\build_logs\<hash>_ExportRelease\msbuild_issues.csv`；常见：AOT/裁剪告警被 `TreatWarningsAsErrors` 升级、模板缺失、solution 未找到。
- 产物断言失败：先复核 `project.godot` 是否被编辑器写回 MCP 项（`git checkout -- src/Mahjong.Client/project.godot src/Mahjong.Client/Startup.tscn`）。
- 冒烟断言失败：日志含最后一次 stdout/stderr；`CONTENT OK` 缺失查内容复制与 `contentHash`，stderr 出现新行查引擎版本变化。
