# T-4 Bot（实施规格）

状态：未开工
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
    public IReadOnlyList<Tile> Hand { get; }
    public IReadOnlyList<SkillId> AvailableSkills { get; }
    public IRandomSource Random { get; }
}
public interface IBotPolicy { BotDecision Decide(BotContext context); }
public sealed class SimpleBotPolicy : IBotPolicy { public SimpleBotPolicy(IWinChecker wins); }
````

## 2. 策略（M0.2 只要求合法）

1. 若可自摸/七对胡 → 胡。
2. 响应窗口：能胡先胡；否则能杠则杠；否则能碰则碰；都可选时按 `Kong > Pong`；否则过。
3. 出牌：优先 `LastDrawn`（若合法）；否则手牌第一张合法牌；不排序、不查 AI 表。
4. 所有选择都必须先经 `ICardAndSkillVerifier`；不得直接改状态。
5. 随机：仅用于同等候选时的 tie-break；使用注入 `IRandomSource`，同 seed 同结果。
6. 延迟：策略本身不 sleep；会话层对 Bot 请求固定延迟 300ms（可配）。

## 3. 测试

- 4 Bot 8 局自动完成；无非法动作、无死循环、牌数守恒。
- 同 seed 两次对局最终 StateHash 一致；不同 seed 不同。
- 能胡必胡；能碰/杠时按优先级；单次 Decide <1ms（Stopwatch 采样）。
