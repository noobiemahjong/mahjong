# 游戏协议（TCP，v1）

状态：M2 实现；网络协议契约，改字段需 Note + `protocolVersion` 递增。
传输：TCP（v1），客户端/服务端都用 .NET `System.Net.Sockets` + `System.IO.Pipelines`；**不用 Godot 传输 API**。帧格式固定，不依赖 Godot 类型。

## 0. 版本

- `protocolVersion = 1`、`rulesVersion = 1`（常量在 `Mahjong.Core.Protocol.ProtocolVersion`）。
- 改帧/字段/事件表 → protocolVersion+1；改规则语义 → rulesVersion+1；改内容/表 → contentHash 变。

## 1. 帧

`[u32 length][u8 type][payload]`；**length = type + payload 字节数**（不含 length）；最大 64KB；小端。

| type | 方向 | 名称 |
|---|---|---|
| 0x01 | C→S | Hello |
| 0x02 | S→C | Welcome |
| 0x03 | S→C | Reject |
| 0x10 | C→S | Command |
| 0x20 | S→C | Batch |
| 0x21 | S→C | Sync |
| 0x22 | C→S | Ack |
| 0x30 | C→S | Resume |
| 0x40 | C→S | Ping |
| 0x41 | S→C | Pong |

## 2. 握手/加入

- C→S `Hello`：`protocolVersion u16, rulesVersion u16, contentHash 32B, playerToken 32B（首次为全 0）, role u8（0 玩家/1 观战）, roomCodeLen u8 + roomCode UTF8, passwordLen u8 + password UTF8, nameLen u16 + name UTF8, avatarId u16`。
- S→C `Welcome`：`selfSeat u8（观战=255）, playerToken 32B（首次加入由服务端生成；Resume 时回显）, roomCode 6B, tickRate u8, settingsCount u16 + (keyLen u16+UTF8 key + valLen u16+UTF8 val)[]`。
- S→C `Reject`：`reason u8`：0 protocol、1 rules、2 content、3 room full、4 password、5 token invalid、6 room not found。
- 握手阶段 5s 超时；失败断开。`roomCode/password` 由 HTTP 建房/加入流程获得（M3）；LAN 房主可固定房间码。

## 3. 命令/事件

- C→S `Command`：`requestId i64, clientSeq i64, verb u8, slot u16, cardId u16`。
  - verb：1 Discard、2 Pong、3 Kong、4 Win、5 Pass。
  - 服务端按 `(seat, requestId)` 幂等；重复返回上次结果。
- S→C `Batch`：`tick i64, seqBase i64, count u16, (eventType u8, payload)[]`；`seqBase` 为本批第一帧 seq，客户端可算后续 seq。
- `eventType` 与 [M1 StreamEventKind](../milestones/M1-实施规格.md) 数值一致：0x10 RoundStarted、0x11 TurnStarted、0x12 Drew、0x13 Discarded、0x14 Meld、0x15 RequestOpened、0x16 RequestClosed、0x17 RoundEnded、0x18 GameEnded、0x19 SeatStateChanged。payload 字段以 [M0.2 T-5 §1.1](../milestones/M0.2/T-5-本地会话.md) 为唯一权威。
- S→C `Sync`：`kind u8, payload`；kind 0 庄家、1 鬼牌、2 座位在线状态、3 tick 校准。
- C→S `Ack`：`lastAppliedSeq i64`；每 32 帧或 200ms 一次。
- 心跳：C→S `Ping` 每 5s；S→C `Pong`；任一侧 15s 无有效帧 → 断开。tick 换算：10Hz → 1 tick=100ms。

## 4. 重连

- C→S `Resume`：`playerToken 32B, lastAckSeq i64`。
- S→C：`Welcome`（回显 token、seat），随后从 **seq=1** 全量重放该座位流（v1 无 checkpoint），末尾补 `RequestOpened`（若有）。
- 座位保留 30s（300 tick）；到期 Bot 接管；玩家回来立即交还，deadline 重置为完整时长。

## 5. 错误

- 未知 type/长度越界/超大 payload/握手前发 Command → 断开并记日志。
- 不加密（v1 局域网/朋友局）；公网 TLS 在 M3 评估。
