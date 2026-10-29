# Agent Note: 性能基线的测量口径

Status: implemented — 三项基线固定为「多轮中位 + 独立进程复跑」，只记录不设阈值。

## Problem

M0.1 要记录 tick 每步耗时、查表冷加载耗时、AOT 启动耗时，三项都随机器与并发活动大幅波动，单次计时不可比；口径不写清时，后来的人无法判断两次数字是否同一测法，也无法判断变慢来自代码还是机器。

## Decision

三项各定固定口径，数字记录在 [M0.1 结果](../../../../docs/milestones/M0.1-工程底座与内核骨架.md)，本笔记只定方法与边界。

- tick：`TickEngineBenchmarkTests`（[`src/Mahjong.Tests/Flow`](../../../../src/Mahjong.Tests/Flow/TickEngineBenchmarkTests.cs)）在一个进程内跑 1,000,000 次 `MoveNext`，3 轮取中位；再用 3 个独立进程各跑一次，报三进程中位。它量的是调度器 `Step` 的内层循环开销，不含真实规则动作。
- 查表冷加载：`TableColdLoadBenchmarkTests`（[`src/Mahjong.Tests/Content/Tables`](../../../../src/Mahjong.Tests/Content/Tables/TableColdLoadBenchmarkTests.cs)）每轮新建 `TableCatalog.Load(<repo>/content/tables)` 读 6 个文件，进程内 3 轮取中位；再用 3 个独立进程各跑一次，同时报各进程首轮（进程冷）与进程内中位。
- AOT 启动：运行 `build/windows/Mahjong.exe --smoke`，从启动进程到 stdout 出现 `CONTENT OK` 行的墙钟毫秒，3 个独立进程取中位；量的是「双击 exe 到内容可读」的用户路径，含 Godot 引擎初始化，止于内容加载，其后的查表加载（`TABLES OK`）不计入。采样时 stdout/stderr 原样落盘，stderr 不参与耗时口径。
- 两条基准测试同属 `[Collection("benchmark")]`，彼此串行：查表加载会分配数百 MB 的字典，与 tick 计时并行会挤 CPU 并触发 GC 暂停，把负载记进 tick 数字；该属性只改 xUnit 的调度分组，不改计时逻辑、轮次与断言。
- 基准只断言加载成功、步数正确、耗时为正；不断言任何绝对时间。

### 冷加载的边界

- 「冷」指进程内没有已解析的表：每轮都重新读文件、重建字典、重新解析 MsgPack，不复用 `TableCatalog` 实例。
- 不含 OS 文件缓存的冷读：Windows 普通用户没有可靠的丢页缓存手段，文件在首次读取后留在 page cache，这里的冷加载只去掉进程级状态与 JIT 影响；含磁盘冷读的数字需要重启或内存清理工具，不在本轮口径内。
- 进程内首轮与后续轮次的差值来自 JIT 与堆预热，报告时写清引用的是哪一种。

### 抖动处理

- 每个指标 3 轮取中位抑制单轮毛刺；三个独立进程再取中位，避免把一次 JIT/预热状态当成机器性能。
- 测量时记录当时的 CPU 负载与并发活动（并行构建、AOT 导出、扫描）；波动超出口径预期时先看负载，不改实现让数字好看。
- 基准用例随全量测试一起执行，耗时只在 `-l "console;verbosity=detailed"` 下由 `ITestOutputHelper` 输出，通过时不污染常规输出。

### 不设阈值

M0.1 只记录基线，不设硬阈值；tick 与启动阈值归 M0.2 决定（见 [T-6 规格](../../../../docs/milestones/M0.1/T-6-基线收口.md) §2）。在采样次数与机器条件不足以定阈值前把绝对时间写进 CI，只会把负载抖动变成红灯。

## Alternatives considered

- 设绝对阈值卡 CI：阈值会被负载抖动触发，逼后续改动为过门禁调数字；等 M0.2 定了真实预算再引入。
- 只做单轮计时：毛刺直接进结论，无法区分负载与真实回归。
- 只在一个进程内测 3 次：第 2/3 次复用 JIT 与堆状态，把进程冷启动的开销藏掉。
- 引入 BenchmarkDotNet：为本阶段一条命令的基线引入额外依赖与更长的运行时间，收益不抵成本。
- 改实现（内联、特化）让数字好看：测量只记录现状，优化要有单独动机与验证。

## Consequences

- 三项基线以区间与中位呈现，跨机器或跨版本比较前先对齐口径。
- 查表冷加载保留两种数字（进程冷首轮、进程内中位），引用时注明是哪一种。
- 新增基准沿用同样口径并入 `[Collection("benchmark")]`，否则会与既有基准抢 CPU 与 GC。

## 当前记录

本轮数字记录在 [M0.1 结果](../../../../docs/milestones/M0.1-工程底座与内核骨架.md)，采样条件见上文「抖动处理」；复现命令见 [cookbook/build](../../../../docs/cookbook/build.md) 与 [cookbook/aot-export](../../../../docs/cookbook/aot-export.md)。
