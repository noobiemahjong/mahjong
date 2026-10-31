# cookbook：运行客户端

本页给出用 Godot 编辑器打开 `src/Mahjong.Client` 并运行 `Startup.tscn` 的编号步骤、期望输出与 MCP 插件注意点；构建前置见 [build](build.md)，导出产物运行见 [aot-export](aot-export.md)。

## 0. 前置

| 项 | 要求 |
|---|---|
| Godot | 4.7.2 .NET（mono）版，本机 `D:\Program Files\Godot_v4.7.2-stable_mono_win64\` |
| .NET SDK | `10.0.401`（[global.json](../../global.json) 锁定） |
| 工程 | 仓库内的 `src/Mahjong.Client/project.godot` |

Godot 标准版（非 mono）跑不了 C# 工程；缺 .NET 版时先装，编辑器启动报 C# 不可用即属此项。

## 1. 打开工程

1. 启动 `Godot_v4.7.2-stable_mono_win64.exe`。
2. Project Manager 里点 Import，选中 `src/Mahjong.Client/project.godot`；等价的命令行是 `& "D:\Program Files\Godot_v4.7.2-stable_mono_win64\Godot_v4.7.2-stable_mono_win64.exe" --path E:\learn\mahjong\src\Mahjong.Client --editor`。
3. 首次打开会导入资源并在 `.godot/` 生成缓存，等底部 MSBuild 输出结束；C# 编译失败时用 `dotnet build Mahjong.sln -warnaserror` 复现同样的错误。

## 2. 运行 Startup.tscn

1. FileSystem 面板双击 `res://Startup.tscn`：工程主场景，`application/run/main_scene` 已指向它，根节点 `Startup` 挂 `Startup.cs`。
2. 按 F5（Run Project）或点右上角 ▶。
3. 期望 Output 打印 `M0.1 START`，随后自动切到 `res://LocalGame.tscn`：1280×720 中文牌桌，Info 首行 `第 n/8 局  余牌 w  你 score`、第二行状态提示，四家区域、自己手牌与 Actions 按钮（出牌/碰/杠/胡/过）可见。
4. 停止：F8 或工具栏停止按钮；对局中也可点 `返回` 退出进程。
5. 中文由 `SystemFont` 运行时回退渲染，依赖操作系统 CJK 字体；随包终版字体归 M4。

## 3. 本地对局操作

1. 轮到自己且状态行为 `轮到你 - 请选择一张牌` 时点手牌选中（黄色边框），再点同一张或点 `出牌` 出牌。
2. 出现可点的 `碰/杠/胡/过` 时点对应按钮；禁用按钮不可点；响应窗口状态行为 `响应窗口 - 碰/杠/过` 或 `响应窗口 - 过`。
3. 一局结束弹 `第 n 局结束`：`继续` 继续，`重开` 用新 seed 重开，`返回` 退出。
4. 8 局结束弹 `牌局结束`：四家总分与 `第1名`…`第4名` 名次；`重开`/`返回` 可用，`继续` 不可见。
5. 重开次数 `restartCount` 递增、seed = 20261031 + restartCount，同一重开次数对局可复现。
6. 结算层标题与正文为中文（`流局`/`赢家 {名}`、`动作 {动作名}`、`{名} ±n  总分 ±n`）。

## 4. 普通启动与 --smoke 的差异

| 模式 | 触发 | stdout | 退出 |
|---|---|---|---|
| 普通启动 | 编辑器直接运行 | `M0.1 START`，随后进 `LocalGame` 并加载内容/查表 | 常驻；停止按钮或 `返回` |
| `--smoke` | 命令行参数带 `--smoke` | `M0.1 START`、`CONTENT OK <64 位 hex>`、`TABLES OK normalWin=<n> windWin=<n> dragonWin=<n> normalAi=<n> windAi=<n> dragonAi=<n>`、`M0.1 SMOKE OK` | 30 帧后自动退出 0 |

`--smoke` 加载 `content/` 与 6 个查表并打印 contentHash 与各表 key 数；失败时 stderr 打印 `M0.1 SMOKE FAILED: <异常类型>: <消息>` 并退出 1。普通启动进对局后加载失败时 Output 红字 `local game content load failed: ...` 并退出 1。

不开编辑器的等价自检（退出码 0、stderr 为空即通过）：

```sh
"D:\Program Files\Godot_v4.7.2-stable_mono_win64\Godot_v4.7.2-stable_mono_win64_console.exe" --headless --path E:/learn/mahjong/src/Mahjong.Client -- --smoke
```

编辑器内试冒烟可在 Project Settings → Editor → Run → Main Run Args 填 `--smoke`，用完清空，避免把本机调试参数写进 `project.godot`。

## 5. 内容目录怎么找

`ContentPaths.Resolve()` 依次尝试：环境变量 `MAHJONG_CONTENT_DIR`（设了但目录不存在时直接报错，不回退）→ 可执行文件旁的 `content/` → 从当前工作目录逐级向上找 `content/`，遇到含 `global.json` 或 `.git` 的目录即停。

普通启动进对局与 `--smoke` 都走这条解析；编辑器里工作目录是工程目录，向上命中仓库根 `content/`；导出产物则把 `content/` 复制到 exe 旁边（见 [aot-export](aot-export.md)）。要指向别的内容根，设 `MAHJONG_CONTENT_DIR`。

## 6. MCP 插件注意点

`src/Mahjong.Client/addons/godot_ai/` 被 [.gitignore](../../.gitignore) 忽略，只在装了 Godot MCP 的本机存在；插件启用项与 `[autoload] _mcp_game_helper` 写进 `project.godot` 后属本机状态，入库前要剥掉，理由见 [Note](../../.agents/notes/implemented/process/2026-10-29-godot-mcp-plugin-not-tracked.md)。

编辑器重启会把这些项写回 `project.godot` 并改 `Startup.tscn` 的 `uid`/`unique_id`：`git status` 出现这两处改动时不要提交；导出前必须确认两项已剥掉，否则导出包会带上 `_mcp_game_helper`。

需要 MCP 时在 Project Settings → Plugins 临时启用 `godot_ai`，用完关掉。
