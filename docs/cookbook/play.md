# cookbook：玩一局

本页给出启动本地对局、无 UI 自动对局、桌面操作、结算与重开的步骤；编辑器打开工程与 `--smoke` 自检见 [run](run.md)，AOT 导出与启动计时口径见 [aot-export](aot-export.md)。

## 1. 启动

编辑器（开发时）：按 [run](run.md) 第 1–2 节打开工程并 F5，Output 出现 `M0.1 START` 后自动进入 `res://LocalGame.tscn`。

导出产物：先按 [aot-export](aot-export.md) 导出 `build/windows/Mahjong.exe` 并把 `content/` 复制到 exe 旁，直接运行该 exe。

两种方式的区别只在宿主（编辑器 JIT / AOT 独立 exe），对局行为、场景与操作相同；导出 exe 的 stdout 在首帧完整渲染后会打印 `LOCAL READY`，口径见 [aot-export](aot-export.md) 的启动计时。

## 2. 无 UI 自动对局

导出的独立 exe 带 `--autoplay` 时不切 UI、不加载 `LocalGame.tscn`，直接用内容与查表装配 4 Bot 会话跑完 8 局：

```sh
build/windows/Mahjong.exe --log-file build/aot-smoke-autoplay.log --autoplay
```

编辑器工程内的等价命令（命令行参数要放在 `--` 之后）：

```sh
"D:\Program Files\Godot_v4.7.2-stable_mono_win64\Godot_v4.7.2-stable_mono_win64_console.exe" --headless --path src/Mahjong.Client -- --autoplay
```

stdout 顺序是 `M0.1 START` → `AUTOPLAY READY` → `GAME OK rounds=8`，随后退出码 0；stderr 只允许 AOT 冒烟记录的两行引擎证书库噪声。装配或推进失败时打印 `AUTOPLAY FAILED: <异常类型>: <消息>` 并退出 1，不打印 `GAME OK`。

种子与节奏固定：`seed = 20261031`、10Hz、上限 1,000,000 tick；轮数来自事件计数，成功条件还要求 8 次 `RoundStarted`/8 次 `RoundEnded`、收到 `GameEnded`、四家总分和为 0、名次是 0..3 的排列。

用时口径：进程启动 → `AUTOPLAY READY` 为 AOT 启动耗时（阈值 <3000ms），进程启动 → `GAME OK` 为 8 局总耗时（阈值 <5000ms）；重复 3 次取中位的方式与断言在 [aot-export](aot-export.md)。编辑器 headless 跑出的墙钟只作功能验证，性能基线以导出 exe 的进程墙钟为准。

## 3. 本地对局操作

1. 轮到自己且状态行为 `轮到你 - 请选择一张牌` 时点手牌选中（黄色边框），再点同一张或点 `出牌` 出牌。
2. 出现可点的 `碰/杠/胡/过` 时点对应按钮；禁用按钮不可点；响应窗口状态行为 `响应窗口 - 碰/杠/过` 或 `响应窗口 - 过`。
3. 手牌展示按 `HandOrdering.SortedForDisplay` 排序，选中与出牌都用真实槽位；鬼牌红框、选中黄框。
4. 自己的手牌、四家弃牌/副露、余牌数、分数与当前轮到谁都在牌桌上；他人手牌只显示张数。
5. 超时未操作由会话托管：出牌请求按手牌槽位升序自动打第一张合法牌，自摸请求按 `过`，响应窗口未应答记 `TimedOut`（不参与仲裁）。

## 4. 局结算

一局结束弹 `第 n 局结束`：标题是刚结束的局号，正文是 `流局` 或 `赢家 {名}`、`动作 {动作名}` 与四家 `{名} ±n  总分 ±n`。

- `继续`：关闭结算层，从下一 tick 继续（下一局在 `RoundEnded` 同 tick 已开始，弹层期间只是暂停渲染推进）。
- `重开`：新建会话，见第 6 节。
- `返回`：退出进程。

## 5. 整局结算与名次

8 局结束弹 `牌局结束`：正文为四家总分与 `第1名` 到 `第4名` 名次；`继续` 不可见，`重开`/`返回` 可用。

名次由 `RoundRanking.Rank` 计算：总分降序、同分座位号升序，见 [scoring](../subsystems/scoring.md) §6。

## 6. 返回与重开

- `返回`：任意结算层上点 `返回` 调 `GetTree().Quit()` 退出；编辑器里也可用 F8/停止按钮。
- `重开`：`restartCount` 递增，新会话 seed = `20261031 + restartCount`；同一重开次数、同一操作序列可复现同一局。
- 重开不复用旧会话：清空选中、隐藏结算层、重置累加器并重新 `Start()`。
