# T-4 Bot（实施规格）

状态：见任务表
依赖：[T-2](T-2-技能与响应.md)、[T-3](T-3-计分与轮次.md)
对应 master：[M0.2](../M0.2-单机可玩闭环.md)
验收：单次决策合法、确定、<1ms；4 Bot 8 局集成验收归 T-5。

## 1. API

落点 `src/Mahjong.Core/Bot/`，命名空间 `Mahjong.Core.Bot`，不新增工程；类型契约、构造校验与异常边界见 [bot.md](../../subsystems/bot.md)，本节只留任务口径与验收锚点。

````csharp
// 示意/伪代码：签名摘要，省略实现体与 XML doc，不可直接编译。
public readonly record struct BotDecision(SkillId? Skill, Tile? Card);

public sealed class BotContext
{
    public BotContext(
        GameState state,
        PlayerState self,
        IReadOnlyList<SkillId> availableSkills,
        IRandomSource random,
        Tile? targetCard = null,
        Tile? lastDrawn = null);

    public GameState State { get; }
    public PlayerState Self { get; }
    public Tile? TargetCard { get; }
    public Tile? LastDrawn { get; }
    public IReadOnlyList<Tile> Hand { get; }
    public IReadOnlyList<SkillId> AvailableSkills { get; }
    public IRandomSource Random { get; }
}

public interface IBotPolicy { BotDecision Decide(BotContext context); }

public sealed class SimpleBotPolicy : IBotPolicy
{
    public SimpleBotPolicy(IWinChecker wins, ICardAndSkillVerifier verifier);
    public BotDecision Decide(BotContext context);
}
````

- 验收锚点：`BotDecision` 三种形态（技能动作、出牌、过；技能动作 `Card` 为 null）；`BotContext` 构造即校验（null 四参数、`self` 同座位同一引用、技能已定义且不重复），`Hand` 是实时只读视图、`AvailableSkills` 是构造快照、`TargetCard`/`LastDrawn` 不校验物理 id；`SimpleBotPolicy` 的 `wins` 只做 null 校验、不保存；无合法动作抛 `InvalidOperationException` 且不改局面。

## 2. 策略（M0.2 只要求合法）

落点同 §1；决策顺序、出牌分支与异常语境见 [bot.md](../../subsystems/bot.md) §2–§3。

- 验收锚点：自己回合 七对自摸 → 普通自摸 → 出牌；弃牌响应窗口 杠 → 碰 → 过；技能动作必须 ∈ `AvailableSkills` 且每个非「过」选择先经 `ICardAndSkillVerifier`；出牌不受技能列表约束；自己回合不做暗杠/补杠。
- 约束：不消费 `BotContext.Random`、不 sleep、不读时钟、不做 IO、无静态可变状态；同一 context 两次 `Decide` 同结果。
- 会话层对 Bot 请求固定延迟 300ms（可配）归 T-5，策略本身不等。

## 3. 测试

落点 `src/Mahjong.Tests/Bot/`：`BotContextTests` 17、`SimpleBotPolicyDecisionTests` 30、`SimpleBotPolicySafetyTests` 8、`BotPolicyDeterminismTests` 5、`SimpleBotPolicyTableTests` 4、`SimpleBotPolicyBenchmarkTests` 1，合计 65；`Support/BotTestSupport.cs` 与 `Support/BotGameDriver.cs` 只提供共享支撑与单局驱动、不含用例。

- 上下文：null 四参数、同座位不同引用、未定义技能（0/5/255）、重复技能、空技能 + 两可选参数 null、`Hand` 实时视图与不排序、`AvailableSkills` 快照、属性暴露输入引用、构造不改局面。
- 决策矩阵：构造 null 两参数、`Decide` null、七对/普通胡/七对优先、胡牌但技能未列 → 出牌、`LastDrawn` 优先/鬼牌跳过/不在手跳过/槽位升序跳过鬼牌、`LastDrawn` 为 Type 31 时合法性随 verifier 鬼牌牌种变化（无鬼出 T31、joker=31 回退槽位 0）、无合法出牌抛、响应 `Kong > Pong`/仅杠/仅碰/无合法 → 过/技能空 → 过/目标为鬼 → 过/墙空拒绝杠、目标牌合法性随 verifier 鬼牌牌种变化（Type 31 与 Type 0 两组对照）、自己回合暗杠经 verifier 合法但策略仍出牌、响应不返回出牌、非自己回合无目标抛、技能顺序不影响、自己回合带目标仍走自己回合。胡牌分支用可编程 `IWinChecker` stub 钉顺序，真实查表「能胡必胡」在 `SimpleBotPolicyTableTests`（类级 `[Collection("table-catalog")]`）。
- 安全：五类决策前后 `StateHash` + 全区域快照 + 牌数一致；返回的出牌在手且经 verifier、技能经 verifier 且 ∈ 列表；过只在响应窗口；不消费随机（计数 RNG 0 次）；同一策略实例跨 context 复用；seed 1/42/20261029 各走完整局，84 次弃牌每步合法、全程 136 张守恒。
- 确定性：测试本地驱动（不引入会话）同 seed 终局 `StateHash` 与弃牌物理 id 序列一致、不同 seed 不同、终局 84 弃牌/墙 0/四家各 13/136 守恒。
- 性能：真实发牌局面、手牌 14、`LastDrawn` 为手牌末张、技能列表空；预热 200、采样 1000 次 `Stopwatch.GetTimestamp`，断言 median 与 p99 < 1ms；具体耗时机器相关，不作验收判据，也不在文档记录单次采样值。

## 实施记录

日期：2026-10-31

实际改动：

- 代码（`Mahjong.Core/Bot` 新增 4 个文件）：`BotDecision.cs`、`BotContext.cs`、`IBotPolicy.cs`、`SimpleBotPolicy.cs`。
- 测试（`Mahjong.Tests/Bot` 新增 6 个用例文件 + 2 个支撑文件）：`BotContextTests.cs`、`SimpleBotPolicyDecisionTests.cs`、`SimpleBotPolicySafetyTests.cs`、`BotPolicyDeterminismTests.cs`、`SimpleBotPolicyTableTests.cs`、`SimpleBotPolicyBenchmarkTests.cs`、`Support/BotTestSupport.cs`、`Support/BotGameDriver.cs`；新增 65 条用例（基线 731 → 796）。敌对审核后补 4 条（鬼牌牌种对照 2、自己回合禁杠 1、LastDrawn Type 31 对照 1），并删脆调用序断言 1 处、技能顺序断言改集合相等、删 support 死代码。
- 文档：本篇 §1–§3 按实现重写并补实施记录；新建 [bot.md](../../subsystems/bot.md)；[architecture.md](../../architecture.md) 组成表/核心循环/子系统页；[M0.2 master](../M0.2-单机可玩闭环.md) 任务表与结果行；[Note](../../../.agents/notes/implemented/feature/2026-10-31-simple-bot-policy.md) 与 [台账提额 Note](../../../.agents/notes/implemented/process/2026-10-31-mutation-ledger-budget-raise.md)；[变异台账](../../reference/mutation-ledger.md) 追加 bot 条目；[docs/AGENTS.md](../../AGENTS.md) 预算表 reference 8 KB→16 KB 与 doc-budgets/baseline 双登记。

验证（真跑，2026-10-31）：

| 命令 | 结果 |
|---|---|
| `dotnet build Mahjong.sln -warnaserror` | exit 0，0 警告 0 错误 |
| `dotnet test Mahjong.sln` | exit 0，796 通过 / 0 失败 / 0 跳过 / 796 总计（基线 731，净增 65） |
| `dotnet test Mahjong.sln --no-build --filter "FullyQualifiedName~Mahjong.Tests.Bot"` | exit 0，65 通过 / 0 失败 / 65 总计 |
| `dotnet test Mahjong.sln --no-build --filter FullyQualifiedName~WinChecker` | exit 0，89 通过 / 0 失败（T-1/T-2 无回退） |
| `dotnet test Mahjong.sln --no-build --filter "FullyQualifiedName~SimpleBotPolicyBenchmarkTests"` | 1 通过；median 与 p99 < 1ms 门槛通过（预热 200 / 采样 1000；单次采样、机器相关，不作验收判据） |
| `pwsh -File tools/doc-gate/doc-gate.ps1 -Fail` | exit 0、0 errors；本任务新增与修改文档 0 新告警（files/warnings 含 gitignored `.reviews/`，计数随审核产物浮动） |
| `git status --porcelain` | 本任务预期文件：代码/测试归 `src/Mahjong.Core/Bot`、`src/Mahjong.Tests/Bot`；文档侧为本篇、bot.md、architecture、M0.2 master、mutation-ledger、两篇 Note、docs/AGENTS.md 预算表、doc-budgets/baseline；无越界 |

偏差与后续：

- `BotContext.AvailableSkills` 是构造时复制的只读快照（`Array.AsReadOnly`），调用方之后改自己的列表不影响已建上下文；`Hand` 保持实时视图。契约 §4 只要求「构造后不可变」，快照是该要求的落地方式。
- `SimpleBotPolicy` 构造的 `wins` 只做 null 校验、不保存：策略不知道鬼牌牌种，合法性唯一权威是 verifier（内部已注入 `IWinChecker` 与鬼牌）；XML doc 与 [Note](../../../.agents/notes/implemented/feature/2026-10-31-simple-bot-policy.md) 写同一口径。
- 决策矩阵的胡牌分支用可编程 `IWinChecker` stub 钉住「七对优先于普通自摸」与技能门槛；真实查表「能胡必胡」在 `SimpleBotPolicyTableTests`（4 例）。
- 自己回合不做暗杠/补杠：M0.2 只做「胡 → 出牌」，杠的完整动作树与补牌流程归 T-5。
- 台账 T-4 共登记 23 条 bot 变异（ML-028–ML-050），含审核提出的测试缺口条目；台账预算按 [台账提额 Note](../../../.agents/notes/implemented/process/2026-10-31-mutation-ledger-budget-raise.md) 提到 16384。
- 后续：会话构造 `BotContext`、300ms 请求延迟、4 Bot 8 局驱动、座位过滤归 T-5；AI 牌效表与强策略归 M4。
- 未跑：变异矩阵未跑（条目已登记台账，执行由用户批次统一安排）；commit/push 未做（Lead 统一提交）。

敌对审核：

- review-core：首轮 PASS（P0=0、P1=0、P2=4）→ delta 复审 PASS（P0=0、P1=0，P2 全关闭；sha256 证明仅两处 XML doc 注释变化、行为零变化）。
- review-tests：首轮 PASS（P0=0、P1=0、P2=8）→ delta 复审 PASS（P0=0、P1=0；P2-1/2/3/4/5/6 关闭，P2-7 接受风险，P2-8 文档已修）→ 补测后 P2-9 关闭（ML-050 判死闸回填，delta2 复核）。
- review-docs：首轮 PASS（P0=0、P1=0、P2=7）→ delta 复审 PASS（P0=0、P1=0，P2-1～P2-7 全部关闭；补测后 796/65/89、逐类 17/30/8/5/4/1 与文档一致，台账 ML-001–ML-050 连续且 T-4 23 条闸名全部可解析，预算 16384 双登记一致，doc-gate 0 errors）。
- 变异台账：ML-001–ML-050 共 50 条；T-4 登记 ML-028–ML-050 共 23 条；已登记、未跑（用户批次统一执行）。
