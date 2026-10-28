# Architecture（当前实现地图）

> 本文件描述**当前代码怎么运转**，读代码前先读它。目标设计与取舍见 [DESIGN.md](DESIGN.md) 与 [.agents/notes/](../.agents/notes/README.md)。

## 当前状态

尚未开始实现；M0 起本文件随代码更新。目标结构见 [DESIGN §3–§9](DESIGN.md)。

## 组成

| 工程 | 职责 | 边界 |
|---|---|---|
| `Mahjong.Core` | 规则、Flow、协议、内容 | 不引用 Godot/网络/真实时钟 |
| `Mahjong.Session` | 权威会话、流产出、回放录制 | 只被 Server 引用 |
| `Mahjong.Client` | Godot 宿主、Mirror、表现、回放播放 | 只引用 Core |
| `Mahjong.Server` | 房间、传输、发现、HTTP | 引用 Session/Core |

## 核心循环

- 权威会话按 tick 推进：命令入队 → 推进 Action 树 → 结算请求/超时 → 事件入流。
- 客户端 Mirror 消费同一条流，跑同一套 Flow，得到可视状态。
- 表现层不阻塞逻辑；逻辑不等待动画。

## Seam

| Seam | 作用 | 规则 |
|---|---|---|
| SessionStream | 每座位/观战/全景各一条事件流 | 可见性在产出侧过滤 |
| Command | 客户端唯一上行 | 带 requestId，服务端复核 |
| Flow / Action | 规则编排 | 纯 C#，无引擎类型 |
| Content | 牌表/技能/查表 | 内容哈希校验 |

## 扩展点

- 技能：`Skill` + 内容配置；新增不改协议。
- 内容：`ContentCatalog`；改表需 Note + 金标验证。
- Bot：服务端 `IAnswerPolicy`，与真人同通道。
- 回放/观战：同一流的消费者，不改规则。

## 子系统页

实现落地后，每子系统在 `subsystems/` 建一页（类型/语义/契约）；本文件只保留地图与指针。
