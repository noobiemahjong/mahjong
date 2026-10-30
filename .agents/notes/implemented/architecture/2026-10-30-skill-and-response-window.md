# Agent Note: 技能动作、二次校验与弃牌响应窗口

Status: implemented — 技能走 Validate/Commit 两段式、校验器单点收口、响应窗口只产出结算且不接 IRequestRegistry。

## Problem

碰、三种杠、自摸、七对要能被校验并落地，弃牌后要向最多 3 家征询碰/杠并选出唯一胜者写进事件流。规格原文留了几处会让实现分叉的空档：仲裁只写「杠 > 碰；同一 tick 按座位号升序；跨 tick 先到先得」，没规定跨 tick 时杠是否仍压过碰；`arrivalSeq` 没定义单位，「同一 tick 内按座位号升序」就无法判；`TrySubmit` 只收技能，「过」没有入口；DESIGN §5.3 的「首个合法应答获胜」与仲裁键冲突。响应窗口还是 `IRequestRegistry` 的天然候选：M0.1 的注册表只做超时/取消结算，窗口若顺手实现它，就要自己决定取消谁，并把会话的流与时钟耦合进 Core。以上任一处含糊，状态哈希与回放就会分叉。

## Decision

1. 技能两段式：`Validate` 只读并返回布尔；`Commit` 先完整复校验、再一次性改状态，失败抛 `InvalidOperationException` 且状态不变。
2. 职责切分：`Skill` 只判自己这一招（碰/杠/自摸/七对）；跨动作口径（座位、牌在手中、非鬼、张数 13/14、目标牌是最后弃牌、技能值未定义）归 `CardAndSkillVerifier`；服务端与本地会话对每个提交动作都过它一次。
3. 仲裁键固定为「优先级（杠 > 碰）→ `arrivalSeq` 升序 → 座位升序」：杠在任何 tick 都压过碰，跨 tick 先到先得只在同优先级内成立。
4. `arrivalSeq` 是到达批次：同一逻辑 tick 的应答必须传同一个值，跨 tick 单调递增；迟到判据是 `arrivalSeq > deadlineTick`，窗口不读墙钟。
5. 「过」是显式应答，走 `TryPass`（对应 `CommandVerb.Pass`）：不计胜者，但同样占用一次应答且会判迟到。
6. 窗口只产出 `ResponseResult` 与逐座 `RequestOutcome`，不实现 `IRequestRegistry`；注册表与超时挂接留给 T-5 的会话适配，这是有意的接缝。
7. 三种杠在墙空时 `Validate=false`、`Commit` 抛且状态不变；「牌墙空即流局」由流程层判，规则层不制造半改状态。
8. 自摸不进响应窗口；`Commit` 只复校验，分数与局结束归 T-3/T-5。

## Alternatives considered

- **窗口实现 `IRequestRegistry`**：少一层适配，但仲裁与取消判定被塞进 Core；注册表 `BeginTick` 按 tick 结算超时，窗口按应答批次计时，两者语义不同；T-5 还要按座位过滤流，窗口无权决定。放弃。
- **保留 DESIGN §5.3「首个合法应答获胜」**：表达不了「杠后到仍压过碰」，同 tick 多家合法响应也没有稳定顺序，回放不可复现；该条按本 Note 收窄。
- **`arrivalSeq` 用单调整数（到达计数）**：同 tick 内不同到达时刻会引入亚 tick 顺序，跨端与回放无法复现；必须用逻辑 tick 批次。
- **只保留 `TrySubmit`，用「不提交」表示过**：窗口分不清「还在思考」与「明确放弃」，`Close` 时 Pass 会被记成 `TimedOut`，与协议 `CommandVerb.Pass` 不一致。
- **把全部校验写进 `Skill.Validate`**：每个技能重复座位/张数/鬼牌逻辑，出牌（无技能）又没有落点；校验器单点收口更好测。
- **技能类改用 `SkillAction` 等新名、元数据保留 `Skill`**：协议技能需要稳定短名；元数据改名 `SkillDefinition` 只波及 Core 与三个测试文件，代价更小。

## Consequences

- `content/skills.json` 的元数据类型改名 `SkillDefinition`，`ContentCatalog.Skills`/`GetSkill` 的类型随之改；技能 id 1–4 与名称不变。
- 窗口状态不参与 `StateHash`：哈希只看模型字段，仲裁结果由调用方写事件流；同输入序列重复跑得到同一 `Outcomes`。
- T-5 需要一个适配器：把窗口的 `Requests` 挂到 `IRequestRegistry`、把 `Close` 产出的 `RequestOutcome` 转成 `RequestClosed` 并做座位过滤。
- 迟到、重复应答、Close 后提交都返回 false 且不改状态；`Close` 幂等。
- 仲裁措辞的唯一权威是 [T-2 §2](../../../../docs/milestones/M0.2/T-2-技能与响应.md)，DESIGN §5.3 与本 Note 同步收窄。
