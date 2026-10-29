# Agent Note: AOT 冒烟工具的判据与导出工具链

Status: implemented — AOT 冒烟按「原生镜像 + 内容三标记 + 精确 stderr 白名单」判红。

## Problem

Windows 导出的客户端可能仍是带 coreclr/hostfxr/GodotSharp 的 JIT 产物，也可能把只在编辑器调试器通道里有意义的 MCP helper 打进 pck；这两种情况进程都能启动，只靠「能起」不能证明 NativeAOT 与内容可用。内容目录不在 res:// 内，导出后是否复制、contentHash 是否与 manifest 一致、6 个查表能否解析，都需要在独立 exe 上验证。

## Decision

- 单一入口 `tools/aot-smoke/aot-smoke.ps1`：前置自检 → 清 build/windows → Godot 绝对路径导出 → 复制 content/ → 产物断言 → 3 次 `--smoke` → 打印中位。
- 判红条件：导出日志出现 `ERROR:`；数据目录递归出现 coreclr.dll/hostfxr.dll/GodotSharp.dll；导出目录字节流出现 `_mcp_game_helper`；冒烟退出码非 0；stdout 缺 `CONTENT OK <64hex>`、`TABLES OK`、`M0.1 SMOKE OK`；contentHash 与 content/manifest.json 不一致；stderr 出现白名单外的行。
- 启动耗时以「进程启动→首次打印 CONTENT OK」为终点，3 次取中位；Startup 先打印 CONTENT OK 再加载查表，该数字含内容哈希校验、不含查表解析。
- `src/Mahjong.Client/project.godot` 设 `dotnet/project/solution_directory="../../"`：Godot .NET 导出从该目录扫描 *.sln 并按 csproj 绝对路径匹配，根 Mahjong.sln 被选中，不新增项目级 sln。
- `Mahjong.Client.csproj` 设 `WarningsNotAsErrors=IL2104;IL3053`：publish 的 ILC 阶段对 GodotSharp 聚合出这两条告警，全局 TreatWarningsAsErrors 会把它升级为 MSB3077 并让导出失败。
- `export_presets.cfg` 的 `exclude_filter="addons/godot_ai/*"`：本机 Godot MCP 插件不入导出包，产物扫描才能零命中。
- stderr 白名单只含本机 Godot 4.7.2 导出启动时的两行证书存储错误：整段大小写敏感匹配、恰好两行且固定顺序，其余任何字节判红。
- 冒烟进程传 `--log-file build/aot-smoke-run.log`：避免与在跑的编辑器争用 `user://logs/godot.log`。

## Alternatives considered

- 只断言「exe 能起、退出码 0」：JIT 产物与坏内容都能通过，等于没有 AOT 验证。
- stderr 严格 0 字节：本机 Godot 4.7.2 引擎固定写证书存储错误，与本仓内容无关；严格 0 会让冒烟永久红并掩盖真实回归。
- 用 `dotnet publish` 直出替代 Godot 导出：绕过 pck/资源打包与 Godot 宿主链接，不是发布产物。
- 在编辑器里 `--headless --path ... -- --smoke` 跑：验证的是编辑器 Debug 运行，不覆盖裁剪与原生镜像。
- 新增 `src/Mahjong.Client/Mahjong.Client.sln`：Godot 默认可直接找到，但仓库出现两个解决方案；solution_directory 匹配根 sln 已满足要求。
- 全局 `NoWarn` 掉 IL2104/IL3053：会把本仓程序集的同类告警一起吞掉；`WarningsNotAsErrors` 保留可见性。

## Consequences

- 冒烟绿表示导出的原生 exe 能加载 content/ 并解析 6 个查表；编辑器 Debug 运行仍不验证裁剪，改客户端代码后要重跑本脚本。
- stderr 白名单是整段精确匹配、绑定 Godot 版本与本机；升级 Godot 或换机器要复核那两行，任何额外字节立即判红。
- 导出数据目录按 assembly_name 命名（本机 `data_Mahjong.Client_windows_x86_64`），脚本用 glob `data_*_windows_x86_64` 断言，不硬编码。
- 导出耗时含 NativeAOT 编译：本机增量 publish 约 10 s，冷构建明显更慢；脚本给导出 900 s、单次冒烟 60 s 上限。
- 本机启动耗时中位 444 ms（3 次 610/443/444），基线数字记在 M0.1 结果。
