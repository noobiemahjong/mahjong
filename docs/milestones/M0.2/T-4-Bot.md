# T-4 Bot（实施规格）

状态：见任务表
依赖：T-2、T-3
对应 master：[M0.2](../M0.2-单机可玩闭环.md)
验收：4 Bot 8 局自动完成；合法、确定、单次决策 <1ms。

## 1. API

`Mahjong.Core.Bot`：

````csharp
public readonly record struct BotDecision(SkillId? Skill, Tile? Card);
public sealed class BotContext
{
    public GameState State { get; }
    public PlayerState Self { get; }
    public Tile? TargetCard { get; }
    public Tile? LastDrawn { get; }
    public IReadOnlyList<Tile> Hand { get; }
    public IReadOnlyList<SkillId> AvailableSkills { get; }
    public IRandomSource Random { get; }
}
public interface IBotPolicy { BotDecision Decide(BotContext context); }
public sealed class SimpleBotPolicy : IBotPolicy { public SimpleBotPolicy(IWinChecker wins, ICardAndSkillVerifier verifier); }
````

## 2. 策略（M0.2 只要求合法）

1. 若可自摸/七对胡 → 胡。
2. 响应窗口（只处理碰/杠）：能杠则杠；否则能碰则碰；都可选时 `Kong > Pong`；否则过。自摸在摸牌阶段直接胡。
3. 出牌：优先 `LastDrawn`（若合法）；否则手牌第一张合法牌；不排序、不查 AI 表。
4. 所有选择都必须先经 `ICardAndSkillVerifier`；不得直接改状态。
5. 随机：仅用于同等候选时的 tie-break；使用注入 `IRandomSource`，同 seed 同结果。
6. 延迟：策略本身不 sleep；会话层对 Bot 请求固定延迟 300ms（可配）。

## 3. 测试

- 单次 Decide 合法、无非法动作、牌数守恒；4 Bot 8 局自动完成归 T-5 集成验收。
- 同 seed 两次对局最终 StateHash 一致；不同 seed 不同。
- 能胡必胡；能碰/杠时按优先级；单次 Decide <1ms（Stopwatch 采样）。

## 实施记录

开工后在此追加：日期、实际改动、验证命令与结果、偏差/后续。
