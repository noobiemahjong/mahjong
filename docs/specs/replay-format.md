# 回放文件格式（mjr）

状态：M1 实现；本文是格式契约，改格式需 Note + 版本号递增。
用途：全景回放（赛后下载/播放），不是网络协议。

## 1. 文件

- 扩展名 `.mjr`；文件名 `{yyyyMMdd-HHmmss}-{roomId}-{replayId}.mjr`。
- 全部小端；字符串 UTF-8；magic ASCII。

## 2. Header

| 字段 | 类型 | 说明 |
|---|---|---|
| magic | 4B | "MJR1" |
| formatVersion | u16 | 1 |
| protocolVersion | u16 | 当前协议版本 |
| rulesVersion | u16 | 规则版本 |
| contentHash | 32B | 内容哈希原始字节 |
| seed | i64 | 审计用，不参与播放重建 |
| createdAtUnixMs | i64 | 创建时间 |
| playerCount | u8 | 4 |
| seats | 变长 | 每座：seatId u8、playerTokenHash 32B、displayName UTF8（u16 长度）、avatarId u16、isBot u8 |
| settings | 变长 | 规则子集/超时等键值（u16 keyLen/key、u16 valLen/val） |

## 3. Body

`frame[]`：每帧 = `u32 length + u8 type + payload`。
type 与 SessionEvent 对应（RoundStarted/TurnStarted/Drew/Discarded/Meld/RequestOpened/RequestClosed/RoundEnded/GameEnded/SeatStateChanged），payload 是该事件的**全景可见**编码（所有手牌身份可见）。

- 帧按 seq 升序；一个 tick 的事件可合并为一帧 batch（type=0x01，payload 为子帧数组）。
- 不使用压缩（M1 先用原始大小；M4 可加）。

## 4. Index

- `rounds[]`：roundNo u8、startTick i64、firstFrameOffset u64、winnerSeat i8(-1=流局)。
- `checkpoints[]`：M1 为空；M4 若加入，格式为 tick i64 + byteOffset u64。
- 文件尾 32B：bodyStartOffset u64、frameCount u32、sha256(body) 32B。

## 5. 播放规则

- 只允许 `rulesVersion/contentHash` 匹配的客户端播放；不匹配拒绝并提示。
- 可选轮：从头静默重放到该轮 startTick；快进/快退/拖动同理；不依赖 checkpoint。
- 全景：播放器强制所有手牌可见，UI 标注“全景回放”。
- 不写回放中的玩家输入；回放只读。

## 6. 兼容

- formatVersion 不匹配：拒绝，不做迁移。
- 新字段只能追加到 Header 末尾，且提升 formatVersion；旧版本文件不保证可读。
