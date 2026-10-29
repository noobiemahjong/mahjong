# Agent Note: godot_ai MCP 插件只在本机，不入库

Status: implemented — 本仓的 Godot MCP 通道按此留在工作区外。

## Problem

Godot 侧 MCP 依赖编辑器插件 godot_ai（313 文件、2.7MB）；安装时它会往 `project.godot` 写 `[editor_plugins] enabled` 与 `[autoload] _mcp_game_helper`。两项若入库，仓库存入第三方工具，且 autoload 会进导出包：T-6 的 AOT 客户端会带上只在调试器在场时才有意义的 helper。

## Decision

1. `.gitignore` 忽略 `src/Mahjong.Client/addons/godot_ai/`，插件文件只留在本机。
2. `project.godot` 不保留 `_mcp_game_helper` autoload 与 godot_ai 插件启用项；引擎自己补的 `"C#"` 特征保留。
3. 需要 MCP 时在本机编辑器 Project Settings 里启用插件，用完或导出前关闭。

## Alternatives considered

- **连插件一起入库**：clone 即用 MCP，但仓库多 2.7MB 第三方工具，导出包会带调试 autoload，还要在导出预设里维护排除规则。
- **入库但在导出预设排除 addon**：autoload 仍指向被排除的路径，导出或启动会报错。
- **只忽略 addon 目录、保留 project.godot 的 autoload/插件项**：新 clone 打开工程即缺插件与 autoload，报错。

## Consequences

- 仓库不承诺开箱即有 Godot MCP；每个开发者/agent 自己装插件并临时启用。
- `project.godot` 是入库文件，编辑器重启若再把 MCP 项写回来，要在提交前剥掉。
- T-6 导出前复核 `project.godot` 无 MCP 项，导出产物不含 `_mcp_game_helper`。
