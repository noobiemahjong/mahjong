# 回放文件格式（.mjr）

状态：M1 实现；本文是格式契约，改格式需 Note + `formatVersion` 递增。
用途：全景回放（赛后下载/播放），不是网络协议。

## 1. 文件

- 扩展名 `.mjr`；文件名 `{yyyyMMdd-HHmmss}-{roomId}-{replayId}.mjr`。
- 单机：roomId = `local`；replayId = 16 位小写 hex（随机/时间派生）。联机：roomId = 6 位房间码。
- 全部小端；字符串 UTF-8（u16 长度前缀）；magic ASCII。

## 2. Header

| 字段 | 类型 | 说明 |
|---|---|---|
| magic | 4B | `MJR1` |
| formatVersion | u16 | 1 |
| protocolVersion | u16 | 当前协议版本 |
| rulesVersion | u16 | 规则版本 |
| contentHash | 32B | 内容哈希原始字节 |
| tickRate | u8 | 10 |
| seed | i64 | 审计用，不参与播放重建 |
| createdAtUnixMs | i64 | 创建时间 |
| playerCount | u8 | 4 |
| seats | 变长 | 每座：seatId u8、tokenHash 32B、nameLen u16 + UTF8、avatarId u16、isBot u8 |
| settings | 变长 | settingsCount u16，随后每项 keyLen u16 + UTF8 key + valLen u16 + UTF8 val |

## 3. Body

`frame[]`：每帧 = `u32 length + u8 type + payload`；**length = type + payload 字节数（不含 length 本身）**。

| type | 名称 | payload |
|---|---|---|
| 0x01 | Batch | `frameCount u16 + (type u8 + payload)[]`（同一 tick 合并） |
| 0x10 | RoundStarted | 同 M0.2 T-5 §1.1 |
| 0x11 | TurnStarted | 同上 |
| 0x12 | Drew | 同上（全景：真实 tileId） |
| 0x13 | Discarded | 同上 |
| 0x14 | Meld | 同上 |
| 0x15 | RequestOpened | 同上 |
| 0x16 | RequestClosed | 同上 |
| 0x17 | RoundEnded | 同上 |
| 0x18 | GameEnded | 同上 |
| 0x19 | SeatStateChanged | 同上 |

payload 字段表以 [M0.2 T-5 §1.1](../milestones/M0.2/T-5-本地会话.md#11-事件-payload二进制字段按序) 为唯一权威；本文件只加“全景”差异（所有 tileId 都真实）。事件编码为**手写二进制**，不使用 JSON/MessagePack。

## 4. Index 与 Trailer

Index 位于 Body 之后、Trailer 之前：

- `roundsCount u8`；每项：`roundNo u8、startTick i64、firstFrameOffset u64、winnerSeat i8(-1=流局)、scoreDelta i32[4]`。
- `checkpointsCount u16`；M1 必须为 0；每项：`tick i64、byteOffset u64`。
- `indexOffset u64`（相对文件头）与 `indexLength u32` 放在 Trailer，reader 从文件尾反读。

**Trailer（固定 81B，文件末尾）**：

| 字段 | 类型 |
|---|---|
| indexOffset | u64 |
| indexLength | u32 |
| frameCount | u32 |
| finalStateHash | 32B（权威 SessionEnd 的 StateHash 原始字节） |
| flags | u8（bit0=completed） |
| sha256Body | 32B（从 Body 起点到 Index 起点的摘要） |

## 5. 播放规则

- 只允许 `rulesVersion + contentHash` 匹配的客户端播放；不匹配拒绝并提示。
- 选局：从头静默重放到该局 startTick；快进/快退/拖动同理；M1 不依赖 checkpoint。
- 全景：播放器强制所有手牌可见，UI 标注“全景回放”。
- 回放只读；不产生输入。
- 未完成回放：`flags bit0=0`；播放器提示“未完成”，只播到最后一个完整局。

## 6. 兼容

- `formatVersion` 不匹配：拒绝，不做迁移。
- 新字段只能追加到 Header 末尾并提升 `formatVersion`。
