# Agent Note: --autoplay 启动模式与 T-7 性能基线口径

Status: implemented — `Startup` 的 `--autoplay` 分支无 UI 跑完 8 局；AOT 启动、8 局总耗时与内存三条基线按进程墙钟和托管堆采样判定。

## Problem

M0.2 §7 要求 AOT 独立 exe 完成对局、启动 <3s、tick p99 <1ms、8 局逻辑模拟 <5s、连续两局无内存持续增长。这些验收点都需要一个不依赖 UI 与人工点击的进程级出口：M0.1 的 `--smoke` 只加载内容与查表，导出产物的裁剪问题要到真正跑一局规则/Bot/会话代码时才暴露；靠编辑器手工点局既不可脚本化，也不验证 AOT。

同一批指标还缺口径：p99 是否含计时开销、内存取托管堆还是进程工作集、GUI 启动算到 `_Ready` 还是首帧、8 局耗时含不含进程启动与内容加载。口径不写死，数字之间不可比，也无法判断回归来自代码还是测法。

## Decision

1. `--autoplay` 落现有 `Startup` 的启动分支（契约裁定 D-T7-1，见 [T-7 实施记录](../../../../docs/milestones/M0.2/T-7-收口.md)）：与 `--smoke` 同源读两路命令行参数，二者同时给出时 `--autoplay` 优先；不新建场景/工程/独立 exe，不调用 `OpenLocalGame`，`--smoke` 分支零改动。装配复用 `ContentPaths.Resolve` → `ContentCatalog.Load` → `TableCatalog.Load` → `LocalSessionFactory.CreateFourBots`（seed 20261031、10Hz、步数上限 1,000,000），同步单线程推进到 `IsGameOver`，每步立即 `DrainEvents` 计数。
2. 成功判定来自事件与终局 payload，不是流程走完：8 次 `RoundStarted`、8 次 `RoundEnded`、收到 `GameEnded`、四家总分和为 0、名次是 0..3 的排列；全部满足才打印 `GAME OK rounds=<事件计数>` 并退出 0，任一不满足打印 `AUTOPLAY FAILED` 退出 1（不打印成功标记）。
3. GUI 启动口径的终点是首帧可操作，不是 `_Ready`（契约裁定 D-T7-2）：`LocalGame._Process` 首次完整走完「清理选中 → 投影 → `ClickRouter.BeginFrame` → `Table.Render` → 登记结算层按钮 → 排空指针 → 推进 tick」之后打印一次 `LOCAL READY`；加载失败早退路径不打印。`AUTOPLAY READY` 是逻辑就绪（会话已 `Start`、未推进 tick），二者不能互相顶替。
4. tick p99 口径（契约裁定 D-T7-4）：Continue-only 动作树 1,000,000 次 `MoveNext`、3 轮，每步前后各取一次 `Stopwatch.GetTimestamp`，nearest-rank 分位取各轮 p99 后中位，阈值 <1,000,000 ns。单步样本含两次计时调用开销，是步耗时上界；Windows QPC 约 100 ns 量化会让 p50 常见 0，分位按计时器精度解读。
5. 内存口径（契约裁定 D-T7-5）：同进程连续两场完整 8 局、同 seed、10Hz，每 25 tick 采样一次 `GC.GetTotalMemory(forceFullCollection: false)` 取本场峰值，第一场结束后置空会话引用并 `GC.Collect`/`WaitForPendingFinalizers`/`GC.Collect` 再跑第二场；判定是第二场峰值 ≤ 第一场峰值 + `first/4 + 4 MiB`（相等算通过）。只断言托管堆采样峰值，`PeakWorkingSet64` 仅作参考不参与断言。
6. 性能数字只认 AOT 导出 exe 的进程墙钟（契约裁定 D-T7-17）：8 局总耗时 = 进程 launch → `GAME OK`（含启动与内容加载），AOT 启动 = 进程 launch → `LOCAL READY`（GUI）或 `AUTOPLAY READY`（autoplay 分支），各跑 3 次取中位，阈值 <5000ms 与 <3000ms。编辑器 headless JIT 的墙钟只作功能证据。

## Alternatives considered

- **新场景/新工程/独立 exe 承载自动对局**：要新增 `.tscn`、工程文件、AOT 配置与 root 清单，导出物翻倍，入口判定逻辑还要复制一份；收口阶段不扩大工程面。
- **给 `--smoke` 加 `--games=N` 复用冒烟分支**：`--smoke` 是 M0.1 冻结的回归口径，改语义等于改基线，回归对照失效。
- **由 aot-smoke 脚本用外部注入数据驱动完整对局**：脚本只能启动进程，对局推进必须在客户端进程内发生，绕不开一个启动分支。
- **GUI 启动口径取 `AUTOPLAY READY`**：省事，但绕过首帧渲染、字体回退与控件登记，正是启动回归容易坏的地方；只在 GUI 计时在本机不稳定时才允许降级，且必须在实施记录留偏差与数字。
- **p99 改批量计时（N 步一测再相除）**：摊薄单步长尾，p99 失去意义；变异也测不出单步毛刺。
- **内存改量 `Process.PrivateMemorySize64` 或工作集**：混入引擎、GPU 与托管碎片，抖动大且不受代码分配控制；改为托管堆采样并注明不含引擎内存。
- **阈值按实跑余量放宽（p99 实测 100ns、8 局远低于 5000ms）**：阈值是验收契约，余量大不是放松理由（契约裁定 D-T7-11）。

## Consequences

- AOT 验收有三条进程级出口：`--smoke`（M0.1 回归）、`--autoplay`（完整 8 局）、GUI 普通启动（首帧标记）；`tools/aot-smoke` 统一断言退出码、stdout 标记、stderr 白名单与三条用时阈值。
- 客户端没有单元测试宿主：autoplay 的五项校验、`LOCAL READY` 的时机、脚本阈值都只能靠 AOT 实跑或静态核对判死，台账相应条目登记为测试缺口。
- 数字必须带口径一起写：p99 含计时开销、内存只含托管堆、GUI 计时需杀进程故退出码不参与断言；结果页与 cookbook 同时写，避免被当成绝对硬件指标。
- stdout 标记进入脚本契约：新增任何 stdout 行都会让 aot-smoke 的整段断言变红，加标记须同批改脚本与 cookbook。
- 相关落点：`--smoke` 的参数判定见 [客户端 --smoke 参数](2026-10-29-client-smoke-arg.md)，客户端类型契约见 [client.md](../../../../docs/subsystems/client.md)，导出与断言步骤见 [cookbook/aot-export](../../../../docs/cookbook/aot-export.md)。
