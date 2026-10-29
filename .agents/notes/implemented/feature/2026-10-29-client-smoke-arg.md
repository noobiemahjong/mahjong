# Agent Note: 客户端 --smoke 判定读两路命令行参数

Status: implemented — T-1 的空客户端冒烟入口按此工作。

## Problem

T-1 规格写「`OS.GetCmdlineArgs()` 含 `--smoke`」。Godot 4.7.2 实测：以 `-- --smoke` 启动时 `OS.GetCmdlineArgs()` 是空数组，参数只在 `OS.GetCmdlineUserArgs()`；导出版直接 `Mahjong.exe --smoke` 时参数在 `OS.GetCmdlineArgs()`。只读一路会漏掉另一种启动方式，冒烟进程不退出，T-6 的导出冒烟脚本会挂住。

## Decision

`Startup` 取 `OS.GetCmdlineArgs()` 与 `OS.GetCmdlineUserArgs()` 的并集判定 `--smoke`；命中即打印 `M0.1 SMOKE OK`，第 30 帧调用 `GetTree().Quit()`。

## Alternatives considered

- **只读 `OS.GetCmdlineUserArgs()`**：导出版 `Mahjong.exe --smoke` 没有 `--`，会漏判。
- **只读 `OS.GetCmdlineArgs()`**：CLI/编辑器的 `-- --smoke` 漏判，进程不退出。
- **用项目设置或环境变量开关冒烟**：要改 `project.godot` 或额外传环境，脚本更脆。

## Consequences

- T-1 的 `godot --headless -- --smoke` 与 T-6 的 `Mahjong.exe --smoke` 共用同一判定。
- 判定只认精确参数名；`--smoke=1` 一类写法不生效，需要时另起 Note。
