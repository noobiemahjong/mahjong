# cookbook：构建与验证

本页给出构建、单测、文档门禁与钩子自检的可复制命令、期望输出与失败处置；命令定义见 [development](../development.md)，门禁规则见 [docs/AGENTS.md](../AGENTS.md)。命令都在仓库根执行，路径相对仓库根。

## 0. 前置

| 项 | 要求 |
|---|---|
| 系统 | Windows 10/11 |
| .NET SDK | `10.0.401`，由 [global.json](../../global.json) 锁定；`dotnet --list-sdks` 能看到该版本 |
| PowerShell | 7 以上（`pwsh`），门禁与钩子测试用它跑 |
| Git | 能执行 `git config`（安装 commit-msg 钩子） |

`dotnet` 缺 `10.0.401` 时会被 global.json 拒绝构建，装对应 SDK 后重跑；`rollForward` 为 `latestPatch`，只接受同一 feature band 的补丁版本。

## 1. 构建（L1）

```sh
dotnet build Mahjong.sln -warnaserror
```

期望：末段依次出现 `已成功生成。`、`0 个警告`、`0 个错误`；退出码 0。[Directory.Build.props](../../Directory.Build.props) 已设 `TreatWarningsAsErrors`，`-warnaserror` 是显式重申。

失败处置：按首条 `error` 或 `warning` 的 `文件(行,列)` 定位工程；增量结果可疑时 `dotnet build Mahjong.sln --no-incremental` 重跑；warning 一律当错误修，不压 `NoWarn`。

## 2. 单测（L1）

```sh
dotnet test Mahjong.sln
```

期望：末行形如 `失败:     0，通过: <N>，已跳过:     0，总计: <N>`；退出码 0。用例数随代码变化，以当次输出为准。

失败处置：`dotnet test Mahjong.sln --filter FullyQualifiedName~<类名>` 缩到单类；`-l "console;verbosity=detailed"` 展开用例输出；只跑基准组用 `--filter Category=Benchmark`（口径见 [perf-baseline-method](../../.agents/notes/implemented/testing/2026-10-29-perf-baseline-method.md)）。

## 3. 基准（L2）

```sh
dotnet test Mahjong.sln --filter Category=Benchmark -l "console;verbosity=detailed"
```

期望：5 个基准用例全部通过、退出码 0；每个用例在 `标准输出消息` 下打印自己的基准行，T-7 的两组是：

```text
M0.2-T7 tick p99 round <n>: p50=<ns> p99=<ns> p99.9=<ns> ns (1000000 steps)
M0.2-T7 tick p99: <ns> ns (median of 3 rounds; threshold 1000000 ns)
M0.2-T7 memory: game1Peak=<bytes> game2Peak=<bytes> growth=<bytes> allowance=<bytes> workingSet=<bytes>
```

判据：tick 基准三轮各自断言 `p99 < 1000000` ns，内存基准断言 `game2Peak <= game1Peak + allowance`；任一超限用例变红、退出码非 0。两个口径（p99 单步含两次 `Stopwatch.GetTimestamp` 开销故为步耗时上界、内存只含托管堆采样峰值不含引擎）见 [Note 基线口径](../../.agents/notes/implemented/feature/2026-10-31-autoplay-and-perf-baseline.md)。

单跑一组用 `--filter FullyQualifiedName~TickEngineP99BenchmarkTests` 或 `~FourBotsMemoryBenchmarkTests`；基准用例同属 `[Collection("benchmark")]`，与既有 tick/冷加载基准串行，同进程内不要并发跑第二份 `dotnet test`，否则耗时与峰值不可比。

失败处置：p99 超限先排除机器负载（并行构建、另一份测试、GC 抖动）再查调度器改动；内存超限查新增静态缓存与未置空的会话引用，允许额度为 `game1Peak / 4 + 4 MiB`。

## 4. 文档门禁（L2）

```sh
pwsh -NoProfile -File tools/doc-gate/doc-gate.ps1 -Fail
```

期望：末两行 `doc-gate: <N> files, 0 errors, <W> warnings` 与 `doc-gate: OK`；退出码 0。ERROR 配合 `-Fail` 才退出 1，WARN 永不阻断；每条发现按 `<ERROR|WARN> <Gid> <relpath>:<line> <message>` 打印。

失败处置：按 G 编号查 [docs/AGENTS.md](../AGENTS.md) 门禁表；预算类 G7 先下沉迁移、再精简、最后才提额；改门禁规则要同批改夹具与规则表。

## 5. 门禁夹具自检（L2）

```sh
pwsh -NoProfile -File tools/doc-gate/tests/run.ps1
```

期望：每例一行 `PASS <case> (G<n>)`，末行 `doc-gate tests: 6 passed, 0 failed`；退出码 0。改 `doc-gate.ps1`、规则表或夹具后必跑。

失败处置：夹具扫描根必须自带最小合法 `doc-budgets.json` 与 `doc-budgets-baseline.txt`；新增夹具按 [doc-gate README](../../tools/doc-gate/README.md) 的「夹具」节加 bad/good 根与用例行。

## 6. commit-msg 钩子自检（L2）

```sh
git config core.hooksPath tools/git-hooks
pwsh -NoProfile -File tools/git-hooks/tests/run.ps1
```

`git config` 每克隆执行一次，成功时无输出。测试期望：`SUMMARY: 13 passed, 0 failed, 13 total` 与 `RESULT: PASS`；退出码 0。机器上没有 `sh` 时直连用例打印 `SKIP`，仍计入通过。

失败处置：钩子由 Git 自带 `sh` 执行，`git --exec-path` 异常的 Git for Windows 会整体失败；钩子规则见 [git-hooks README](../../tools/git-hooks/README.md)。

## 7. 一次跑完

```sh
dotnet build Mahjong.sln -warnaserror && dotnet test Mahjong.sln && pwsh -NoProfile -File tools/doc-gate/doc-gate.ps1 -Fail && pwsh -NoProfile -File tools/git-hooks/tests/run.ps1
```

期望：四段输出分别如上，整体退出码 0；任何一段失败即停，先修再往下跑。

运行客户端见 [run](run.md)；AOT 导出与冒烟见 [aot-export](aot-export.md)。
