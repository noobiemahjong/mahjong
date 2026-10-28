# 麻将（Godot 重写）设计稿

> 状态：**v1.0 基线**（D1–D18 已拍板，2026-10-28）
> 版本记录：v0.1 初稿 → v0.2 引入 net10 + AOT → v1.0 补齐 D4–D18（tick/托管/观战/回放分发/人数/移动端/服务端 JIT）
> 目标引擎：Godot 4.7.2（mono / .NET 10，与 Ursa-Client 同形态：net10.0 + NativeAOT）
> 前置阅读：旧项目资料（`legacy/ARCHITECTURE-unity-legacy.md`）、SGS 参考实现（`E:/learn/sgs`）、GameEventEngine（`E:/learn/GameEventEngine`）
> 治理：根 [AGENTS.md](../AGENTS.md) · 文档标准 [docs/AGENTS.md](AGENTS.md) · 当前实现 [architecture.md](architecture.md) · 决策 [.agents/notes/](../.agents/notes/README.md)
> 本文档只写设计，不含实现代码。

---

## 0. 决策记录

| 编号 | 决策 | 结论 | 影响面 |
| --- | --- | --- | --- |
| D0 | 引擎 | Godot 4.7.2 mono，C# | 全项目 |
| D0 | 权威模型 | 服务端权威，客户端同构镜像 | 网络/规则 |
| D0 | 重连/回放/观战 | 统一为“每座位一份会话流”的三种消费者 | 网络/回放 |
| D0 | 游戏循环 | tick 化 Action 树（GameEventEngine 演进版），去除 await / 阻塞等待 | 规则/流程 |
| D1 | 回放 | **全景回放**（录制权威端全景流，赛后播放） | 回放 |
| D2 | 观战 | **默认身份盲**（等同“没有手牌的玩家”），全知观战作为私有房可选开关 | 观战/协议 |
| D3 | checkpoint | **v1 不做**，整段重放；格式预留扩展位 | 存储/重连 |
| D4 | 逻辑 tick | **10Hz**（100ms/tick） | 计时/协议 |
| D5 | 断线托管 | 断线 **30s** 后 Bot 接管；玩家回来**立即交还** | 会话 |
| D6 | 托管范围 | 玩家超时未归 → **整局托管**，Bot 打完本场 | 会话 |
| D7 | 观战 | 上限 **8**；公网允许身份盲观战；全知观战仅私有房开关 | 观战 |
| D8 | 回放分发 | 服务器保留**最近 30 局**；赛后客户端下载；支持选轮与快进快退 | 回放/服务端 |
| D9 | 人数 | **固定 4 人**；不足用 Bot 补位 | 会话/UI |
| D10 | Bot 表 | 先沿用旧表跑通（已迁移到 content/tables/，见 TABLES.md），M4 再评估重写生成器 | 内容/算法 |
| D11 | 音频 | v1 仅中文（旧项目只有 Mandarin，男/女两套，共 83 个 mp3） | 内容 |
| D12 | 目标框架 | **net10.0**；**客户端 AOT、服务器普通 JIT 发布** | 全项目 |
| D13 | 空房 | 4 个真人全部离线 → **立即销毁房间**（不保留、不等托管） | 会话/服务端 |
| D14 | 回放下载鉴权 | **一次性 token**，先简单做 | 回放/服务端 |
| D15 | 观战交互 | **v1 只读**，不做聊天；语音另用外部工具 | 观战 |
| D16 | 排障日志 | 不额外落“对局日志”，**回放文件即排障材料** | 服务端 |
| D17 | Bot 基准 | 实现后实测基线，**M4 再定优化目标**（现在不拍数字） | 内容/算法 |
| D18 | 移动端 | **完整实现之后再做**（旧项目本来就是移动端为主），但 v1 约束就要提前遵守 | 表现层/构建 |

> 本表只保留结论；每条决策的 why、放弃的方案与后果见 [.agents/notes/](../.agents/notes/README.md)。

D1–D18 的推论：

- D1 决定了**权威端需要录制一份不受座位过滤的事件流**；它只在服务端存在，赛后落盘，绝不下发给在线客户端。
- D2 决定了**观战连接使用与玩家相同的流格式**，只是“可见性策略”不同，不需要单独的协议分支。
- D3 决定了重连时**从头重放本人流**，客户端镜像必须支持“无渲染快速追帧”。
- D4 决定了所有计时、超时、动画预算都以 100ms 为粒度；表现层不得假设更细的逻辑时间。
- D5/D6 决定了座位状态机：`Online → Grace(30s) → Bot → (玩家回来) Online`，且 Bot 一旦接管就打到本场结束（除非玩家回来）。
- D8 决定了回放文件必须由服务端持久化并可按需下载，客户端只做本地缓存。
- D12 决定了共享内核（Core）必须 AOT/裁剪安全；服务器自身代码可以放松（JIT 发布）。
- D13 决定了“房间”的寿命与真人数量绑定：真人全走 ⇒ 房间连同观战者一起销毁。
- D18 决定了 v1 的 UI/输入/内存策略必须按移动端能接受的标准来做，不能先写一套桌面专用交互。

---

## 1. 目标与非目标

### 1.1 目标（v1）

1. 单人：1 真人 + 3 Bot，完整 8 局，可暂停/退出。
2. 局域网：UDP 广播发现 + IP 直连；房主进程内嵌权威服务器；断线重连；座位保留与 Bot 托管。
3. 公网：同一套权威服务器可独立部署（无头 console / docker），客户端按房间码加入。
4. 回放：赛后全景回放，支持播放/暂停/倍速/拖动进度。
5. 观战：对局中可加入观战（身份盲），赛后可选全景回放。
6. 复用：旧项目的规则语义、牌表、Bot 策略、美术与音频素材、BMFont 字体。

### 1.2 非目标（v1 明确不做）

| 项 | 说明 |
| --- | --- |
| 官方匹配/排位 | 只做房间码与局域网发现 |
| 反作弊（对抗恶意客户端） | 服务端仍做全量校验，但不做加密/混淆/行为分析 |
| 跨版本联机 | 版本三元组不匹配直接拒绝 |
| 中途换人/补位 | 只允许原座位重连；观战另走连接 |
| 主机迁移 | 房主退出即房间结束（公网服务器无此问题） |
| 移动端 | v1 桌面优先；C#+Godot 移动端导出后续评估 |
| checkpoint / 秒开回放 | v1 不实现，回放拖动用“重放到最近索引”实现 |
| 录像上传/云存档 | v1 本地文件 |

---

## 2. 术语

| 术语 | 含义 |
| --- | --- |
| Session | 一场对局（8 局），一个权威会话实例 |
| Round | 一局（发牌到胡牌/流局） |
| Flow | 规则流程编排（触发器 + Action 树），服务端与客户端共用 |
| Mirror | 客户端镜像：以流为输入，跑同一套 Flow 得到与权威端一致的可视状态 |
| SessionStream | 权威端为某个消费者产出的事件流（每座位一份 / 观战一份 / 全景一份） |
| Seat | 0..3 的座位号，同时是玩家身份 |
| Zone | 牌区：牌墙 / 手牌 / 副露 / 弃牌 / 亮牌 / 结算展示 |
| Slot | 某牌区内的位置下标（插入序，稳定） |
| TileRef | (zone, slot, tileId)；tileId = -1 表示对该消费者不可见 |
| Reveal | 把某个 TileRef 的身份揭示给某条流 |
| PendingRequest | 服务端正在等待某个座位作答的请求（含 deadline tick） |
| RequestGroup | 同时向多个座位发问、取首个合法响应的请求组（碰/杠/胡竞速） |

---

## 3. 总体架构

### 3.1 分层

```
┌───────────────────────────────────────────────────────────────┐
│ Godot 客户端（C#）                                             │
│  Views / ViewModels / Input / Audio / Replay Player            │
│  Mirror（跑 Flow，消费 SessionStream）                          │
└───────────────▲───────────────────────────────┬───────────────┘
                │ SessionStream（每座位）        │ Command（本座位）
┌───────────────┴───────────────────────────────▼───────────────┐
│ 传输层（ITransport）：TCP/WebSocket（v1）；LAN 发现走 UDP       │
└───────────────▲───────────────────────────────┬───────────────┘
                │                               │
┌───────────────┴───────────────────────────────▼───────────────┐
│ 权威会话（SessionHost）                                        │
│  Session × N，单逻辑线程 tick 驱动                              │
│  Flow（触发器 + Action 树） + Rules + Bot + 请求/超时/托管       │
│  产出：每座位流 + 观战流 + 全景录制流                            │
└───────────────────────────────────────────────────────────────┘
```

**关键约定：**

1. 权威端是唯一的状态拥有者；客户端不产生权威状态。
2. 客户端镜像跑**同一套 Flow**（不是事件 apply 层），靠流中的“答案/揭示/同步值”推进——与 SGS 的镜像模型一致。
3. 所有非确定性都由服务端决定并写入流；客户端从不自己掷骰子。
4. 权威端不等待渲染；表现层永远不阻塞逻辑。

### 3.2 程序集与目标框架

| 程序集 | 目标框架 | 职责 | 依赖 |
| --- | --- | --- | --- |
| Mahjong.Core | net10.0 | 引擎无关内核：牌/区域/规则/Flow/协议/内容/查表 | 无（纯 C#，不引用 Godot） |
| Mahjong.Session | net10.0 | 权威会话：座位、流产出、请求/超时/托管、回放录制与写盘 | Core |
| Mahjong.Client | net10.0（Godot.NET.Sdk/4.7.2 + PublishAot） | Godot 宿主：视图/输入/音频/Mirror/回放播放器 | Core |
| Mahjong.Server | net10.0（普通 JIT 发布） | 无头宿主：房间、传输、发现、HTTP（房间码/回放下载） | Session |
| Mahjong.Tests | net10.0 | xUnit：规则/流程/金标回放/断线/多房间隔离/AOT 冒烟 | Core / Session |

> **目标框架统一 net10.0**。依据：Ursa-Client 实测 Godot 4.7.2 宿主运行时为 .NET 10.0.9，`TargetFramework=net10.0`、`PublishAot` 导出可跑通（其 `docs/reference/AOT导出与裁剪取证.md`）。本机已有 .NET SDK 10.0.401，**不需要 .NET 8**。
>
> 只拆 5 个工程：Core 是引擎无关内核；Session 只被 Server 引用；Client 只引用 Core。**AOT 需要 root 的程序集越少越好**（见 §3.4）。未来若 Core 过大，再按 Rules/Flow/Protocol 拆分并同步补 root。

### 3.3 为什么不用 Godot 的 MultiplayerAPI

- 它的 RPC 与节点路径/场景树绑定，重构成本高；
- 会话、重连、观战、版本校验都要自己实现，用不上它；
- 回合制对传输要求低，自研长度前缀协议更可控、更可测。

结论：**v1 使用自研协议 + TCP/WebSocket**；传输抽象为 `ITransport`，未来可换 LiteNetLib/ENet 而不动上层。

### 3.4 NativeAOT 与裁剪（D12：客户端 AOT，服务器 JIT）

**适用范围**：`Mahjong.Client`（Godot）使用 NativeAOT；`Mahjong.Server` 用普通 `dotnet publish`（JIT/自包含或框架依赖），不走裁剪。
但 `Mahjong.Core` 被客户端引用，**Core 必须始终 AOT 安全**；只有 Server 独占的代码（`Mahjong.Session`、传输、HTTP）可以放松约束。

客户端采用 Ursa-Client 已实测通过的配方（其 `src/Ursa-Client/Ursa-Client.csproj`）：

```xml
<PropertyGroup>
  <PublishAot>true</PublishAot>
</PropertyGroup>
<ItemGroup>
  <TrimmerRootAssembly Include="Mahjong.Client" />
  <TrimmerRootAssembly Include="Mahjong.Core" />
  <TrimmerRootAssembly Include="GodotSharp" />
</ItemGroup>
```

- Godot 4.7.2 的导出预设**没有** AOT 开关，只能写在 csproj；编辑器跑的是 Debug/JIT，开发体验不变。
- Godot 按名字从 .pck 动态加载脚本类型 ⇒ 宿主程序集必须**整程序集 root**；只 root 游戏程序集时，`ScriptPathAttribute` 等特性的构造参数会被裁空，引擎报 `native_name == StringName()`（Ursa 已实测定位）。
- 任何**新增的、会被按名字或反射加载的程序集**都必须补进 `TrimmerRootAssembly`，并重跑 AOT 冒烟。

**硬性约束（违反会在“导出后运行到某个功能”才炸）：**

1. 禁止 `Reflection.Emit`、`Assembly.Load`、运行期动态类型生成、用 `MakeGenericType` 构造未 root 的泛型；
2. **禁止反射式序列化**：不用 protobuf-net；网络包用**手写 codec**（显式 switch + 长度前缀）；内容 JSON 用 source-generated `JsonSerializerContext`；
3. MessagePack **只用于 Bot 查表**，且必须用 MPC 生成的 Formatter + `StaticCompositeResolver`，禁用 DynamicObjectResolver；若配起来烦，直接换手写二进制（推荐后者，见 §12）；
4. 依赖注入/装配用手写组合或源生成容器，不用反射容器；
5. 每个里程碑收尾必须跑一次 **客户端 AOT 冒烟**：`dotnet publish -p:PublishAot=true` → 断言产物无 `coreclr/hostfxr/GodotSharp.dll` → 跑独立 exe → 打一局本地对局并播放一段回放 → stderr 为空。

**服务器发布**：`dotnet publish -c Release`（可选 `--self-contained`），docker 镜像基于 `mcr.microsoft.com/dotnet/runtime`；不引入 AOT 的裁剪问题，启动时间与内存不是瓶颈。

> 定位：AOT 是客户端**发布加固**，不是安全边界。作弊面由服务端权威 + 可见性过滤解决（见 §6.4）。

---

## 4. 领域模型

### 4.1 牌与区域

| 概念 | 定义 |
| --- | --- |
| TileType | (suit, rank)，suit 属于 万/筒/条/风/箭；每种 4 张 |
| TileId | 1..136 的物理牌唯一编号；0 保留；-1 表示“对本消费者不可见” |
| ZoneId | ENUM：Wall / WallTail / Hand(seat) / Meld(seat) / Discard(seat) / Revealed / DealPresentation |
| Slot | Zone 内下标，**插入序**；插入只发生在尾部（Draw），移除按 Slot |
| TileRef | (zoneId, slot, tileId)；tileId 是“该消费者视角的已知身份”，可为 -1 |

**双序原则（重要）：**

- **流程序（flow order）**：Zone 的 Slot 顺序，协议只认这个；摸牌 append、弃牌 remove at slot、副露 append。
- **显示序（view order）**：UI 对手牌排序后的顺序，只存在于 ViewModel。
- 任何从 UI 发起的操作，必须先把显示序换算成流程序（按 Slot 引用）再发命令。
- 禁止在规则/协议层对 Zone 内元素排序。

### 4.2 可见性

```
VisibilityPolicy:
  - 玩家 P：P 自己的 Hand 全可见；所有公共区全可见；其他座位 Hand 仅可见数量
  - 观战（身份盲，默认）：没有自己的 Hand；其余同“玩家 P 但 P 无手牌”
  - 全景（仅回放/赛后）：所有 Zone 全可见
  - 规则性揭示：副露、胡牌亮手、流局亮手、被技能查看——按规则在事件时刻揭示
```

实现上把“谁能看见谁”作为**会话状态的一部分**（对应 SGS 的 `HandCardVisibility`），而不是散落在各个包里判断。

### 4.3 三份状态

| 状态 | 位置 | 内容 |
| --- | --- | --- |
| 权威状态 | 服务端 Session | 全部身份、牌墙顺序、RNG、计时、请求 |
| 镜像状态 | 客户端 Mirror | 同构 Flow 产生的状态，身份按可见性填充或 -1 |
| 展示状态 | ViewModel | 排序、动画、选中、提示；不参与逻辑 |

三者严禁互相复用引用；镜像状态是唯一可以被 UI 查询的“游戏状态”，UI 不直接读网络包。

---

## 5. 规则与流程（tick 引擎）

### 5.1 Action 树（GameEventEngine 演进）

保留父子 Action、优先级、可中断；去掉 await/UniTask/ABP。每个 Action 的 yield 返回以下状态之一：

| ActionState | 语义 | 何时满足 |
| --- | --- | --- |
| Continue | 本 tick 继续执行下一段 | 立即 |
| WaitTicks(n) | 等待 n 个逻辑 tick | tick 推进 |
| WaitUntil(predicate) | 每 tick 评估一次条件 | 条件为真 |
| WaitRequest(requestId) | 等待某个 PendingRequest 结算 | 应答/超时/取消 |
| AbortParents(depth) / AbortSiblings | 立即中断祖先/兄弟动作 | 立即 |
| **Complete(result)** | 以结果终止当前动作树（替代异常） | 立即 |

**新增 `Complete(result)` 是本设计对 GameEventEngine 的唯一语义扩展**：`RoundResult`（胡/流局/赢家/番种）和 `SessionResult`（整场结束/全部退出/牌墙耗尽）通过它向上返回，而不是抛异常。

### 5.2 调度器契约

```
每逻辑 tick（默认 10Hz）：
  1. 从命令队列取本 tick 之前到达的命令，按 (tick, seq) 排序应用
  2. 推进当前 Action：MoveNext 直到遇到阻塞态
  3. 结算 WaitRequest：已应答的解除阻塞；到 deadline 的产出“超时结果”
  4. 推进计时器（tick 粒度）
  5. 生成本 tick 事件批次（原子入流，带 seq）
  6. （调试构建）每 N tick 计算一次 StateHash 并与客户端比对
```

硬性规则：

- **单房间单逻辑线程**；所有 Session 由一个 `SessionHost` 轮询推进（10Hz 下几百个房间可跑单线程）。
- 规则/流程代码**禁止**接触网络、线程、Godot 类型、真实时钟。
- 时间只用 tick 表示；表现层自己把 tick 换算成毫秒做倒计时。
- 命令在 tick 边界成批应用，不允许“tick 中途插入外部命令”。

一个回合（简化时序，tick = 100ms）：

```
tick T      Flow: TurnStarted(A) → RequestOpen(A, {出牌, 技能}, deadline = T+150)
tick T      A 的镜像收到 RequestOpen → 解冻手牌、开始倒计时
tick T+3    Command(A, Discard{slot=7}) 到达 → 本 tick 校验 → Discard 事件入流
tick T+3    Flow: GroupOpen(候选 B/C/D, 抢碰/杠/胡, deadline = T+18)
tick T+4    Command(B, Pong) 到达 → 校验通过 → GroupResolved(B, Pong) → 其余候选 cancel
tick T+4    Flow: Meld(B) → RequestOpen(B, 强制出牌) → ...
tick T+150  若 A 一直未应答 → RequestClosed(Timeout) → Flow 自动打出最后一张
```

注意：每个 tick 内事件的先后顺序由 Flow 串行产生，**同一个 tick 的事件打包为一个原子批次**，客户端整批应用，避免“半个 tick”的可视状态。

### 5.3 请求模型

```
PendingRequest {
  requestId, seat, kind, allowedVerbs,
  deadlineTick, payloadRef, visibility(seat)
}
```

- 发出：Flow 调用 `Request(seat, ...)`，返回 requestId，Action `WaitRequest` 阻塞；
- 应答：客户端发 `Command{requestId, verb, payload, clientSeq}`；服务端校验座位/请求/合法性；
- 关闭：`RequestClosed{requestId, result}` 入流（仅目标座位可见），Action 恢复；
- 超时：到达 deadlineTick 时自动关闭，结果为 `Timeout`，由 Flow 决定“自动打最后一张/过”；
- 幂等：同一 requestId 的重复应答只取第一次，后续丢弃并记日志。

**RequestGroup（碰/杠/胡竞速）：**

- 同时向最多 3 个候选座位发问，deadline 相同；
- 首个**合法**应答获胜；其余座位收到 `RequestClosed{cancelled}`；
- 仲裁结果必须写入流（`GroupResolved{winner, result}`），因为客户端不参与仲裁；
- 同到达顺序的公平性：按服务端到达顺序；若同一 tick 内到达，按座位号升序（显式规则，便于测试）。

### 5.4 随机与确定性

- 只有服务端持有 RNG（PCG32，种子由时间/会话 id 派生）；
- **随机结果一律通过 `SyncValue` 进入流**：庄家、鬼牌、洗牌后第一次摸到的牌（其实由 Draw 事件天然给出）、Bot 的随机决策、超时的自动选择；
- 客户端**不拥有 RNG**，也不接收 seed（在线期间）；
- 洗牌顺序对客户端不可见、也不需要：牌身份通过 Reveal/Draw 事件逐个给出；
- 因此“客户端自己重跑”不会产生随机分歧。

### 5.5 Bot

- Bot 是服务端的一种 `IAnswerPolicy`：走与真人**完全相同**的请求/应答路径；
- Bot 的答案作为普通答案写进流，客户端不运行 Bot 代码；
- 断线托管 = 把该座位临时切到 Bot 策略，玩家回来再切回；
- 托管期间所有行为同样入流，回放/观战无差异。

### 5.6 异常策略

| 情况 | 处理 |
| --- | --- |
| 局终 / 牌墙耗尽 / 全场结束 | `ActionState.Complete(result)`，禁止异常 |
| 技能/动作被取消 | `AbortParents / AbortSiblings` |
| 客户端非法输入 | 拒绝 + 日志 + 计入异常计数；不抛异常、不断连接 |
| 超时 | `RequestResult.Timeout` |
| 违反不变式（null 牌、非法区域、状态机错乱） | 抛异常 → Session 边界捕获 → 落 dump → 结束该房间，不影响其他房间 |

> 这一条直接来自 SGS 的教训：SGS 用 `GameOverException` / `TriggerResultException` 做流程控制，导致状态半改、日志不完整、难以回放。tick 引擎必须把“流程中断”变成一等返回值。

---

## 6. 会话流与协议

### 6.1 中心抽象：一条流

```
权威 Session
  ├─ Stream(seat 0)  ──玩家 0 的连线
  ├─ Stream(seat 1)  ──玩家 1 的连线
  ├─ Stream(seat 2)
  ├─ Stream(seat 3)
  ├─ Stream(spectator) ──身份盲观战（可多条）
  └─ Stream(panorama)  ──全景录制（仅落盘，永不外发）
```

一条流 = 有版本头的、带单调 seq 的、已按可见性过滤的事件序列。消费方式有三种：实时下发、断线全量重放、文件回放。

**座位流与观战流的唯一区别是 VisibilityPolicy**；全景流不做过滤。

### 6.2 帧格式

```
[4B length][1B type][payload]
```

- payload 使用**手写二进制 codec**（显式 switch + 长度前缀，AOT 安全，见 §3.4）；**协议层不依赖 Unity/Godot 类型**；MessagePack 不用于实时协议；
- 每个流维护单调递增 `seq`（从 1 开始）；客户端定期 Ack（每 32 条或 200ms），服务端用于重连起点；
- 一个 tick 内产生的多条事件打包成一个 batch 帧，保证原子应用；
- 控制帧（Welcome/Resume/Ping）与事件帧共用连接但类型区分。

### 6.3 包清单

**服务端 → 客户端**

| 包 | 说明 |
| --- | --- |
| Welcome | selfSeat、房间设置、版本三元组、tick 率 |
| Batch | 本 tick 的事件/同步/揭示/状态变更（原子） |
| SyncValue | 权威值：庄家、鬼牌、座位在线状态、托管标记、其他随机结果 |
| Reveal | 单个或多个 TileRef 的身份揭示 |
| RequestOpen | 请求（仅目标座位可见），含 deadlineTick 与 allowedVerbs |
| RequestClosed | 请求结算（目标座位） |
| GroupResolved | 多人竞速结果（公开） |
| UIState | detached / attached（追帧用） |
| SessionEnd | 结束原因与最终结果、回放文件标识 |
| Pong | 心跳应答 |

**客户端 → 服务端**

| 包 | 说明 |
| --- | --- |
| Hello | 版本三元组、playerToken、昵称/头像 |
| Command | requestId、verb、payload、clientSeq（本座位） |
| Resume | playerToken、lastAckSeq |
| Ack | 已应用 seq |
| Ping | 心跳 |

> 注意：**没有 Ask 包**。问题由客户端镜像自己“问出来”（跑同一套 Flow），服务端只下发答案——这是 SGS 的关键设计，能省掉一半流量并保证两端流程一致。

### 6.4 可见性过滤规则

| 事件 | 玩家 P | 观战（身份盲） | 全景 |
| --- | --- | --- | --- |
| 自己摸牌 | 含 tileId | 无此事件 | 含 |
| 他人摸牌 | 仅数量变化 | 仅数量变化 | 含 tileId |
| 弃牌 | 含 tileId | 含 | 含 |
| 副露 | 含 | 含 | 含 |
| 胡牌/流局亮手 | 按规则揭示 | 按规则揭示 | 含 |
| 其他座位手牌内容 | 永不 | 永不 | 含 |
| 请求/答案 | 只下发给相关座位；答案广播给全员（不含私密 payload） | 同玩家 | 含 |

过滤在**流产出侧**完成（不是客户端过滤），避免“发出去再隐藏”的作弊面。

### 6.5 版本三元组

| 维度 | 内容 | 不匹配 |
| --- | --- | --- |
| protocolVersion | 帧与包结构 | 硬拒绝 |
| rulesVersion | 规则/流程语义 | 拒绝联机；回放拒绝播放 |
| contentHash | 牌表/Bot 表/配置的 sha256 清单 | 拒绝联机 |

握手失败返回 `Rejected{reason}`，客户端展示原因并引导升级。局域网发现包同样带三元组，列表内直接置灰不兼容房间。

---

## 7. 客户端镜像与表现层

### 7.1 Mirror 的职责

- 消费 SessionStream：应用答案/揭示/同步值，跑同一套 Flow，得到镜像状态；
- 对外只暴露**只读查询接口**给 ViewModel（手牌、副露、弃牌、当前座位、可选动作、待决请求、倒计时）；
- 不产生权威状态、不做最终校验（可做本地预校验用于灰化按钮）；
- 调试构建定期计算 StateHash 与服务端比对，不一致立即上报并触发全量重放。

### 7.2 追帧（断线重连 / 观战加入 / 回放）

```
UIState(detached)
  → 快速应用历史事件（不渲染、不等待动画）
  → UIState(attached) + 当前 PendingRequest
  → 正常渲染
```

- 追帧期间所有“等待表现”一律跳过；
- 追帧在逻辑线程上完成，完成后一次性把状态快照交给主线程渲染（避免逐条跨线程）；
- 目标：8 局全量重放 < 200ms。

### 7.3 表现与逻辑解耦

- Flow 通过 `IPresentationSink` 发出“表现意图”（发牌、摸牌、弃牌、碰杠、胡牌、结算）；
- 服务端注入 Dummy Sink；客户端注入 Godot Sink；回放注入同一个 Godot Sink + 虚拟时钟；
- Sink 的动画时长**不影响**逻辑；逻辑 tick 不等待动画；
- 动画期间玩家输入的处理：逻辑上请求已经打开，输入随时可接受；UI 可自行做“动画未结束不可点”的表现层限制，但该限制不能导致超时误判（deadline 由服务端控制）。
- **deadline 与动画的边界**：请求超时从服务端发出时开始算；因此发牌/开局等前置动画必须在第一个请求之前播完，单个动画时长应 ≤ 500ms，超时阈值（如 15s）必须显著大于动画总时长。

### 7.4 UI 技术栈

- Godot Control + Theme；牌桌用 Node2D + Sprite2D（旧项目是正交相机 + SpriteRenderer，映射直接）；
- 动画用 Godot Tween / AnimationPlayer 替代 DOTween；
- 字体沿用 BMFont（旧项目的 BitmapFontExporter 输出 .fnt，Godot 原生支持）；
- 音效沿用 MP3/OGG 与中文语音包，走 AudioStreamPlayer + 音量总线。

### 7.5 移动端提前约束（D18）

移动端不在 v1 交付范围，但旧项目本来就是移动端为主，v1 的实现不能把移动端路堵死：

- **布局**：全部 Control 用锚点/容器，不写死像素；至少覆盖 16:9 与 20:9 两种安全区；牌桌用“相对座位”布局（旧项目已有 `GetRelativeSeatIndex` 的思路）。
- **交互**：单击选中 / 再点提交；hover 只作为增强，不作为唯一手段（旧项目的“甩牌”手势保留为可选加速）。
- **输入抽象**：ViewModel 不假设鼠标，统一成 “pointer down/move/up + 手势识别”，桌面与触摸共用一套。
- **性能预算**：目标中端 Android 60fps；牌面走图集，单帧纹理切换数在 M1 前定预算（参考 Ursa 的“一屏指令涉及的纹理数”教训）。
- **内存**：Bot 表常驻内存需实测；大表按需/分块加载。
- **构建**：Android 导出 + AOT 链路作为 M5 验证，v1 不阻塞；iOS 更晚。

---

## 8. 回放（全景）

### 8.1 方案

**权威端在每场对局中额外录制一条 panorama 流**（含所有身份、所有座位视角的全部事件），赛后写入文件。回放端用与客户端相同的 Mirror + Godot Sink 播放这条流。

- 不依赖 seed 重建：身份在事件里直接给出，规则/洗牌算法变化不影响旧录像（只要 rulesVersion 匹配）；
- seed 仅作为**审计元数据**写在文件头（便于服务端复算），回放播放不使用它；
- 文件体积增加很小（每次摸牌多 2 字节 tileId，全场合计几 KB）。

> 备选方案（不采用）：只录玩家视角流 + seed，回放时用 seed 重建牌堆。缺点是对洗牌算法与内容版本强依赖、易碎（SGS 就是这条路，且用“文件末尾 16 字节”藏 seed，非常脆）。

### 8.2 文件格式

```
Header (定长 + 变长)
  magic        "MJRP"
  formatVer    1
  protocolVer  1
  rulesVer     1
  contentHash  32B
  seed         int64        （审计用）
  tickRate     10
  createdAt     int64
  playerCount  byte
  seats[]      { playerToken 哈希, 昵称, 头像 id, 是否 Bot }
  settings     房间设置
Body
  frame[]      与 SessionStream 完全同构的帧（panorama）
Index
  (tick, byteOffset)      每 5 秒一条（定位帧边界用）
  rounds[] { roundNo, startTick, firstFrameOffset, winnerSeat }   # 选轮用
  eventsTotal, ticksTotal
Trailer
  frameCount, sha256(body)
```

### 8.3 播放器与控制

- 复用客户端 Mirror；输入源换成 `FileStream + ReplayClock`；
- 回放界面：顶部 8 轮局列表（第几轮 / 赢家 / 分数变化）+ 中部时间轴 + 底部播放控制（速度档、上一轮/下一轮、±5s）；
- 基础控制：播放 / 暂停 / 0.5x–8x 倍速 / 逐帧（tick）前进 / 逐事件前进；
- **选轮**：回放 UI 显示 8 轮列表（第几轮、赢家、分数变化），点击第 N 轮 = 从头静默重放到该轮起点（不渲染），然后进入正常播放；
- **快进/快退**：`±5s` / `±1 轮` / 拖动进度条，全部实现为“从头静默重放到目标 tick 再继续播放”；
- 之所以敢这么做：8 局全量重放 < 200ms（D3 无 checkpoint）；Index 只用于定位帧边界，**不用于恢复状态**；
- 预留 `index.checkpoints` 扩展位：若未来单局事件量增长到重放 > 300ms，再按需加入每轮一次的状态快照（格式向后兼容）；
- 全景模式强制打开全部可见性，UI 标注“全景回放（含所有手牌）”；
- 版本不匹配：拒绝播放并提示所需版本；不做自动迁移。

### 8.4 录制、保留与下载

- 服务端在 `SessionEnd` 时把 panorama 流落盘到 `replays/`（局域网宿主为房主机器，公网为服务器）；
- **单机同样录制全景流**（本地 Session 直接写盘），保证单机对局也能回放；
- **保留策略（D8）**：服务器保留**最近 30 局**（可配置），环形淘汰；目录带 `manifest.json`（对局时间、房间、玩家、版本、文件大小），启动时校验并修复；
- **下载（D8/D14）**：对局结束后 `SessionEnded` 里下发 `replayId + downloadUrl + 一次性 token`；客户端通过 HTTP `GET` 下载（支持 `Range` 续传），落地到本地 `replays/` 后由本地回放器打开；
- 一次性 token 先做简单版：服务端内存表持有（replayId → token、过期时间、已使用标记），默认 **10 分钟有效、下载成功即失效**；不做账号绑定、不做分享链接；
- 局域网形态：房主进程同时提供这个 HTTP 端点（与房间发现/加入共用端口或独立端口）；
- 文件命名：`{yyyyMMdd-HHmmss}-{roomId}-{replayId}.mjrp`；文件大小预估：8 局全景流 ≈ 100–300KB（未压缩），Brotli 后 ≈ 30–100KB；
- 30 局 ≈ 3–10MB（压缩后），磁盘压力可忽略。

---

## 9. 观战（身份盲）

- 观战是**无座位连接**：不占 4 个座位、不参与超时、不影响对局；
- 服务端为每个观战者建立一条 `Stream(spectator)`，可见性 = “没有手牌的玩家”；
- 支持随时加入：先全量重放历史流，再转实时；加入时也走 `UIState(detached/attached)`；
- 观战者只能收，**v1 只读（D15）**：不能聊天、不能发任何 Command；沟通走外部语音（朋友局面对面或语音软件）；
- 数量上限 **8**（D7）；公网允许身份盲观战；房主可单独关闭观战；
- 规则性揭示（胡牌/流局亮手）观战者也照常看到，这是规则的一部分；
- **全知观战**作为 LAN/私有房可选开关（默认关闭）：打开后直接把 panorama 流发给观战者，UI 明确标注，且仅房主可开启；
- 公网服务器默认禁止全知观战。

---

## 10. 断线重连

### 10.1 时序

```
[服务端检测断线] 心跳超时（如 3s）→ SyncValue{seat, offline}
                 → 30s 宽限期（D5），期间仍给该座位重连机会
                 → 宽限期满：Bot 接管（D5）
                 → 座位保留到本场结束（D6：不判负、不换人）

[客户端重连]     Hello{token, 版本} → 校验版本与座位保留
                 → Welcome{selfSeat, settings}
                 → UIState(detached)
                 → 从 seq=1 重放本人流（v1 全量）
                 → 重放期间产生的实时事件追加在尾部
                 → UIState(attached) + 当前 PendingRequest（若有）
                 → 恢复交互

[超时未归]       Bot 持续托管整局（D6），直到玩家回来或本场结束
```

### 10.2 关键规则

1. **身份**：`playerToken` 是加入时服务端下发的随机 token（不是昵称），重连凭 token 认座。
2. **幂等**：客户端命令带 `clientSeq`；服务端对同一 requestId 只接受一次应答；重连后客户端以最新 `lastAckSeq` 继续。
3. **重放边界**：重放的历史流必须止于“当前已知的最后一个 seq”，之后的事件走实时；服务端在同一个发送队列里串行拼接，避免交错。
4. **待决请求**：重放结束后必须补发 `RequestOpen`（如果当前正等待该座位），否则玩家会看到“卡住”。
5. **托管交接（D5）**：玩家回来时若 Bot 正在等待该座位的请求，立即取消 Bot 应答、把请求交还玩家，deadline 重置为完整时长（避免“刚回来就超时”）。
6. **整理局托管（D6）**：宽限期满后 Bot 一直打到本场结束；期间任何时刻玩家重连都立即交还，不因为“已经过了几局”拒绝。
7. **边界情况**：
   - 对局已结束：返回 `SessionEnd` + 回放文件标识，不再恢复座位；
   - 重连时座位上已有新玩家：不可能（v1 不允许换人），只可能是 token 错误，拒绝；
   - 同一 token 重复连接：踢掉旧连接（最后一次连接生效）。

---

## 11. 联机与部署

### 11.1 三种形态

| 形态 | 权威服务器位置 | 传输 | 发现 |
| --- | --- | --- | --- |
| 单机 | **同一进程内**（本地 Session），TCP 都不需要 | 进程内队列 | 无 |
| 局域网 | 房主进程内（后台逻辑线程）或房主另开无头进程 | TCP（回环/内网） | UDP 广播 |
| 公网 | VPS 上的 Mahjong.Server（docker） | TCP/WebSocket | 房间码 |

> 单机也走“权威 Session + Mirror”这条路径（进程内传输），**不为单机写第二套逻辑**。这是避免“单机好、联机坏”的关键。

**人数与补位（D9）**：固定 4 人局；房主/服务器在开局前用 Bot 补齐空位（单机 = 1 真人 + 3 Bot）。对局中不允许新玩家入座，只允许原座位重连；观战另计（上限 8，D7）。

**房主退出的语义（v1 明确）**：局域网形态下权威 Session 在房主进程内，房主关闭客户端 = 房间结束（客户端收到 `SessionEnded{HostQuit}`）。公网形态下服务器独立，单个玩家退出只是座位离线，对局照常（托管或等待）。这两个行为必须在 UI 上给出明确提示，不能靠玩家猜测。

**空房销毁（D13）**：公网服务器上，若 **4 个真人全部离线**（无论此刻是否由 Bot 托管），立即结束 Session：断开全部观战者、释放房间码；已开局则把已录到的全景流落盘（标注“未完成”），未开局则直接丢弃。

> 注意与 D6 的边界：D6 是“**至少还有一个真人在线**时，离线座位由 Bot 打到本场结束”；D13 是“**一个真人都不剩**时立刻销毁”。

### 11.2 局域网发现

- 协议：UDP 广播探测 + 单播应答；
- 端口：发现固定 47777；游戏端口动态（应答里告知）；
- 广播地址：遍历本机网卡，对每个网段的定向广播地址发送，同时发 255.255.255.255（部分设备只认一种）；
- 应答内容：房间名、房主昵称、人数、版本三元组、是否有密码、tick 率；
- 兜底：手动输入 IP、最近连接列表；
- 注意：Windows 首次运行会弹防火墙授权；同机多开需错开发现端口或由单实例代理；广播不跨 VLAN/部分 Wi-Fi 隔离。

### 11.3 公网

- 房间码：6 位字母数字，由服务器生成并维护映射；
- 客户端直连服务器（无 NAT 问题）；
- 服务器按房间隔离，单进程多房间（SessionHost 轮询）；
- 传输 v1 用 TCP（回合制对延迟不敏感）；后续可换 LiteNetLib；
- **HTTP 面**（与游戏端口分开）：`POST /rooms`（建房/加入返回房间码）、`GET /replays/{id}`（鉴权下载，支持 Range）、`GET /healthz`；
- 中继/打洞**不在 v1 范围**：需要“朋友自己开房且无公网 IP”时，再按 EOS Relay / Steam DGS / 自建 UDP 转发 的顺序评估。

### 11.4 部署与运维

- Mahjong.Server：普通 `dotnet publish -c Release`（可选自包含）/ docker 镜像；配置（端口、房间数、日志级别、回放目录、保留局数）走 appsettings；
- 日志：结构化（JSON Lines）；每房间一个 sessionId；
- 回放：`replays/` + `manifest.json`，保留最近 30 局（D8），启动时清理孤儿文件；
- 监控：房间数、tick 耗时、断线率、重连成功率、回放下载次数；
- AOT：只有客户端遵守 §3.4；服务器 JIT 发布（但共用 Core 仍保持 AOT 安全）。

---

## 12. 内容与资产

### 12.1 内容目录（两端共享，必须完全一致）

```
content/
  manifest.json          # 文件清单 + sha256
  tiles.json             # 34 种牌定义、精灵索引、语音索引
  suits.json             # 花色规则（是否顺子、每种张数）
  skills.json            # 4 个技能的元数据（id、动画资源、语音）
  scoring.json           # 番种、分值（v1 可先硬编码，后续配置化）
  tables/                # 旧项目原样迁移的 6 个查表（v1 唯一事实来源）
    dictionary_normal.bytes      # 胡牌表（万/筒/条）
    dictionary_wind.bytes        # 胡牌表（风）
    dictionary_dragon.bytes      # 胡牌表（箭）
    ai_dictionary_normal.bytes   # 牌效表（万/筒/条）
    ai_dictionary_wind.bytes     # 牌效表（风）
    ai_dictionary_dragon.bytes   # 牌效表（箭）
```

> 表的二进制格式、key 编码、与上游算法仓库的差异、以及「生成器丢失」后的处理方案，见 [TABLES.md](TABLES.md)。

- contentHash = 上述文件的 sha256 清单哈希，握手与回放都校验；
- 旧项目的 ScriptableObject 数据导出为 JSON；查表二进制**先沿用旧格式**（D10），加载后转为更紧凑的结构（排序数组 + 二分）以降低内存；
- 服务端与客户端加载同一份 content 目录（构建时复制）；
- **AOT 约束**：JSON 读取用 source-generated `JsonSerializerContext`；`*.bin` 查表若继续用 MessagePack，必须静态注册 MPC 生成的 Formatter（禁用动态 resolver），否则导出后裁剪会留下空实现（§3.4）。

### 12.2 素材迁移

| 素材 | 处理 |
| --- | --- |
| 牌面/头像/UI 贴图 PNG | 直接复用（注意图集与九宫格重排） |
| 音效/中文语音 MP3 | 直接复用；**仅 Mandarin 一种语言，男/女两套，83 个文件**（D11） |
| BMFont .fnt + 图集 | 直接复用（Godot 原生支持） |
| Unity Prefab/Scene/Animator | 重做 |
| DOTween 序列 | 用 Godot Tween 重写 |
| ScriptableObject | 导出 JSON（见上） |

---

## 13. 项目结构

```
mahjong/
  src/
    Mahjong.Core/            # 引擎无关内核（net10，无 Godot 引用）
      Rules/  Flow/  Protocol/  Content/
    Mahjong.Session/         # 权威会话（net10，仅 Server 引用）
    Mahjong.Client/          # Godot 项目（project.godot 在此，net10 + PublishAot）
    Mahjong.Server/          # 无头宿主（net10，普通 JIT 发布）
    Mahjong.Tests/           # xUnit（net10）
  content/
    tables/                  # 6 个查表 .bytes（旧项目迁移，v1 不动）
  replays/                   # 运行期生成，git 忽略
  docs/
    AGENTS.md                # 文档标准：tier/写作规则/预算/门禁
    architecture.md          # 当前实现地图（M0.1 起随代码更新）
    DESIGN.md                # 目标设计基线（本文件）
    development.md           # 命令/验证/MCP/团队/提交
    TABLES.md                # 查表格式/来源/生成计划
    PROTOCOL.md              # 包字段级定义（M2 产出）
    REPLAY_FORMAT.md         # 回放二进制布局（M1 产出）
    milestones/  postmortem/  engineering/pitfalls/  reference/  legacy/
  .agents/notes/             # 决策记录（implemented/rejected/...）
  tools/
    doc-gate/                # 文档门禁
    table-inspect/           # 查表解码
    aot-smoke/               # 导出+AOT 冒烟（每个里程碑跑）
```

> 注：Session 不放进 Core，避免客户端程序集包含权威逻辑；Core 内不再拆子程序集（AOT root 越少越省事）。若未来 Core 超过约 2 万行，再按 Rules/Flow/Protocol 拆分并同步补 `TrimmerRootAssembly`。

命名约定：

- 规则层无 `Godot` 命名空间引用；有 CI 检查（编译期即可保证）；
- 协议类型统一 `Mj*` 前缀（如 `MjWelcome`），避免与规则类型混淆；
- 事件/请求 id 用 `long`，单调递增，会话内唯一。

---

## 14. 测试策略

| 层级 | 内容 | 工具 |
| --- | --- | --- |
| 单元 | 胡牌判定（含七对/鬼牌）、番种计分、副露合法性、牌区操作 | xUnit |
| 流程 | 脚本化 4 个 Bot 打完 8 局；超时；非法命令；取消/中断 | xUnit |
| 确定性 | 同一输入序列跑两遍 StateHash 一致；Bot 决策可复现 | xUnit |
| 金标回放 | 固定一场对局的全景流，回放后终态哈希与预期一致 | xUnit |
| 断线注入 | 随机断线/重连/重复命令；断言状态一致、无重复动作 | 集成测试 |
| 多房间隔离 | 同时跑 2 个不同配置房间，交错 tick，断言互不影响 | 集成测试 |
| 回放导航 | 选轮/快进/快退后状态与目标 tick 的 StateHash 一致 | xUnit |
| 服务端保留 | 第 31 局写入后最旧一局被淘汰；manifest 自愈 | xUnit |
| 下载 | HTTP Range 续传、token 过期/越权拒绝/二次下载拒绝（D14） | 集成测试 |
| 空房销毁 | 4 真人全离线后 Session 立即销毁、回放落盘/丢弃正确、观战被断开（D13） | 集成测试 |
| **客户端 AOT 冒烟** | 导出产物无 coreclr/hostfxr/GodotSharp.dll；独立 exe 打一局 + 播回放；stderr 为空 | 脚本（每个里程碑） |
| 性能 | 50 房间 × 10Hz 的 tick 耗时；单房间重放耗时 | 基准测试 |

> “多房间隔离”测试专门针对 SGS 的 `Game.CurrentGame` 教训：测试里故意交错两个房间的 tick，任何共享静态状态都会立刻暴露。

---

## 15. 里程碑与验收

| 里程碑 | 内容 | 验收标准 |
| --- | --- | --- |
| M0.1 | 工程底座与内核骨架：5 工程、CI/doc-gate、内容/查表、牌/区域、tick 引擎、AOT 空客户端（细则见 [M0.1](milestones/M0.1-工程底座与内核骨架.md)） | M0.1 §7 全部满足：构建/测试/门禁、查表与模型、tick 确定性、AOT 空运行、文档 |
| M0.2 | 单机可玩闭环：规则子集、Bot、本地会话、Godot 最小闭环、AOT 完整对局（细则见 [M0.2](milestones/M0.2-单机可玩闭环.md)） | M0.2 §7 全部满足：8 局单机闭环、确定性、AOT、性能基线、文档 |
| M1 | 回放与流正式化：SessionStream、全景录制/播放/选轮/倍速、金标回放（前置 M0.2） | 单机对局产出并播放全景回放；选轮/快进后哈希一致；同局重放 < 200ms |
| M2 | 局域网：发现、房主内嵌服务器、30s 托管、重连、观战 | 断网 30s 后 Bot 接管、60s 回来立即交还且状态一致；第三人可中途观战；两房间同时在线不串数据 |
| M3 | 公网：无头服务器、房间码、版本握手、回放下载与保留 30 局 | 3 个跨网络客户端打完一场；赛后客户端下载并播放；版本不匹配被正确拒绝 |
| M4 | 打磨：托管 UX、回放浏览器、Bot 表优化、性能 | 50 房间压测达标；选轮/拖动 < 300ms；**先测出 Bot 表基线（内存/冷加载/单步决策），再据此定优化目标并复测** |
| M5 | 移动端：Android 优先（触摸/DPI/内存/启动时间）；Godot + net10 + AOT 链路验证 | 真机跑完一局 + 播放回放；无桌面专用交互；帧率/内存达标 |

> 本表只保留一行摘要；M0.1/M0.2 的范围、任务、验收与完成定义以各自计划为准。

---

## 16. 风险与待决问题

### 16.1 风险

| 风险 | 影响 | 缓解 |
| --- | --- | --- |
| AOT 裁剪导致运行期缺类型 | “编译过、跑到某功能才炸” | 工程第一天就跑 aot-smoke；按名字/反射加载的程序集一律 root；新依赖必重跑 |
| Godot AOT 导出链路（跨平台） | 导出产物不可运行 | 先 Windows 打通（Ursa 已实测）；Linux/macOS 导出单独验证 |
| 10Hz 计时精度 | 倒计时显示跳动 100ms | 表现层插值显示；逻辑只认 tick |
| 镜像与权威流程漂移 | 静默状态不一致 | StateHash 对账 + 金标回放 + 单测 |
| UI 重做工作量被低估 | 进度风险 | M0.2 先做“能玩”的最小 UI，美术后置 |
| Bot 查表 15MB 的内存/加载 | 移动端/低配机 | D17：实现后先测基线；先沿用旧表 + 排序数组/异步加载，M4 再定优化目标 |
| 旧 C# 表生成器丢失 | 无法重新生成/扩展查表 | 以 content/tables 下的 .bytes 为唯一事实来源（已迁移并解码确认）；M4 才评估重写生成器，验收=与旧表逐条语义一致；**不得直接用上游 data 替换**（语义不同，见 TABLES.md） |
| 重构后规则语义偏离 DESIGN | 与预期不符、测试失效 | 以 DESIGN + 规则单测 + 金标回放为准；旧版不作为对照基线 |
| 版本耦合（镜像需同 rulesVersion） | 老录像/老客户端不可用 | 明确“不跨版本”；回放文件带版本，拒绝而非迁移 |
| 移动端 AOT/性能未验证 | M5 才暴露 | v1 就遵守 §7.5 约束；M5 先 Android，iOS 更晚 |
| 空房/断线策略边界 | 幽灵房间、房间泄漏 | D13 写死销毁条件 + 集成测试覆盖 |

### 16.2 待决问题（需要你拍板）

D1–D18 已全部拍板。剩下的是“实现后才有数据”或“以后再定”的项，不阻塞 M0.1/M0.2：

1. **Bot 基线口径（D17）**：M0.2 结束时测一套基线（常驻内存、冷加载耗时、单步决策耗时、单局耗时），M4 据此定优化目标并复测；现在不预设数字。
2. **M5 移动端**：平台顺序与最低配置（建议 Android 先，最低版本/机型在 M5 启动时定）；Godot 4.7.2 + net10 + AOT 的 Android 链路需实测，iOS 更晚。
3. **回放自动下载**：默认“赛后弹窗询问”，是否提供“自动下载最近一局”开关（建议 M3 再定）。
4. **局域网房主退出后**：v1 直接结束房间回大厅，不实现“继任房主/重开”；若朋友局高频遇到，再评估。
5. **语音**：确认不内置语音聊天（D15），文档/UI 提示玩家使用外部语音工具。

---

## 附录 A：与 SGS 的对应关系

| SGS | 本设计 |
| --- | --- |
| CardItem(place, index, cardId=-1) | TileRef(zone, slot, tileId) |
| CardSync | Reveal |
| AskForCardUsageResponse（广播答案） | Answer（Batch 内） |
| StatusSync / SyncInteger | SyncValue |
| SeedSync（仅回放用） | 回放文件头 seed（审计，不参与重建） |
| UIStatusHint(IsDetached) | UIState(detached/attached) |
| RecordTakingOutputStream + AddStream | SessionStream + Resume 全量重放 |
| 观战末尾槽位 | Stream(spectator)，无座位 |
| HandCardVisibility | VisibilityPolicy |
| Game.CurrentGame（线程静表态） | SessionContext 显式传递 |
| TriggerResultException | AbortParents / AbortSiblings |
| GameOverException | ActionState.Complete(result) |
| ClientNetworkProxy 每座一个 | Mirror 内每座一个 Gateway 视图 |
| protobuf-net / 反射序列化 | 手写二进制 codec（AOT 约束，§3.4） |
| MessagePack（仅数据文件） | 仅 Bot 查表；静态注册 source-generated resolver |

## 附录 B：旧项目素材/规则复用参考（非验收基线）

> 本表只回答「哪些东西可以复用、怎么复用」，**不构成行为等价要求**。
> 不要求与旧版行为一致；差异不视为缺陷；只有与 DESIGN 冲突时才按 DESIGN 修。「移植」= 复用数据/规则意图，不是逐行复刻。

| 旧项目 | 新设计 | 处理 |
| --- | --- | --- |
| MahjongGame（379 行上帝对象） | Flow + Session 拆分 | 重写结构，保留语义 |
| Card / CardType / SuitType | Tile / TileType | 移植 |
| CompositeCard / CompositeCardMessage | Meld / MeldRef | 移植（去掉 Netcode 序列化） |
| Skill / SkillResource / SkillRegistry | Skill + content/skills.json | 移植逻辑，配置外置 |
| Trigger / GameEvent / GameEventArgs | Flow 的 Trigger/Action | 移植并接入 tick 引擎 |
| ScoreManager / ScoreActionType | 规则层保留 | 移植；番种配置化留 M4 |
| WinningCardDetector / BotDecisionMaker | Rules / BotPolicy | 移植；表格式先沿用 |
| GameDataSource | ContentCatalog | 重写（去 ScriptableObject） |
| NetworkGameState / ServerPlayer / RPC | SessionStream + Command | 重写 |
| GameView / PlayerView / CardContainerView | Godot 视图 | 重写 |
| UI（uGUI） | Godot Control | 重写 |
| DOTween | Godot Tween | 重写 |
| MessagePack（仅牌表） | 保留用于 Bot 查表 | 保留（静态 resolver；M4 可换手写二进制） |
| protobuf-net / Json.NET | 手写 codec / 源生成 JSON | 不引入 |
| VContainer / UniTask / Netcode | 全部移除 | 不使用 |

## 附录 C：协议事件类型（v1 初版）

| 事件 | 可见性 | 说明 |
| --- | --- | --- |
| SessionStarted | 公开 | 座位表、设置、庄家/鬼牌 |
| RoundStarted | 公开 | 局号、庄家、牌墙数量 |
| TurnStarted | 公开 | 当前座位、deadline tick |
| Draw | 本人/公开(数量) | 摸牌；本人含 tileId，他人仅数量 |
| Discard | 公开 | 座位、slot、tileId |
| RequestOpen | 目标座位 | 请求类型、deadline、允许的 verb |
| RequestClosed | 目标座位 | 结果（答案/超时/取消） |
| GroupOpened / GroupResolved | 公开/相关 | 多人竞速 |
| Meld | 公开 | 碰/杠类型、牌、来源座位 |
| Win | 公开 | 赢家、胡牌张、番种、分数变化 |
| RoundDrawn | 公开 | 流局、亮手 |
| RoundEnded | 公开 | 分数变化、下一局庄家 |
| SessionEnded | 公开 | 最终分数、回放标识 |
| SeatStatus | 公开 | 在线/离线/托管 |
| SyncValue | 按需 | 庄家、鬼牌、其他权威值 |
| UIState | 单连接 | 追帧开关 |

---

## 附：设计原则速查（写代码前贴在墙上）

1. 权威端只产出“流”，其他一切都是消费者。
2. 规则层不认识网络、不认识 Godot、不认识真实时钟。
3. 不排序流程牌区；排序只发生在 ViewModel。
4. 身份靠 Reveal，不靠 seed；随机靠 SyncValue，不靠客户端 RNG。
5. 流程中断靠返回值，不靠异常。
6. 每个座位一份流；观战是“没有手牌的玩家”；全景流只落盘。
7. 没有静态状态；房间之间零共享。
8. 表现不阻塞逻辑；逻辑不等待动画。
9. AOT 纪律：不反射序列化、不动态生成、按名字加载的程序集必须 root；每个里程碑跑 aot-smoke。
10. 权威端是唯一的大脑子；客户端只负责“看起来对”和“把输入送上去”。
