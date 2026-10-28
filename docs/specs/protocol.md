# 游戏协议（TCP，v1）

状态：M2 实现；网络协议契约，改字段需 Note + protocolVersion 递增。
传输：TCP（v1），客户端/服务端都用 .NET `System.Net.Sockets` + `System.IO.Pipelines`；**不用 Godot 传输 API**。LAN 与公网同一协议。帧格式固定，不依赖 Godot 类型。

## 1. 帧

`[u32 length][u8 type][payload]`；length 含 type，最大 64KB；多字节小端。

| type | 方向 | 名称 |
|---|---|---|
| 0x01 | C→S | Hello |
| 0x02 | S→C | Welcome |
| 0x03 | S→C | Reject |
| 0x10 | C→S | Command |
| 0x20 | S→C | Batch（一 tick 事件） |
| 0x21 | S→C | Sync（权威值） |
| 0x22 | S→C | Ack |
| 0x30 | C→S | Resume |
| 0x40 | S→C | Ping |
| 0x41 | C→S | Pong |

## 2. 握手

- C→S `Hello`：`protocolVersion u16, rulesVersion u16, contentHash 32B, playerToken 32B, displayName UTF8, avatarId u16`。
- S→C `Welcome`：`selfSeat u8, tickRate u8, settings`；或 `Reject`：`reason u8`（版本/内容/房间满/密码错/token 无效）。
- 握手阶段 5s 超时；失败断开。
- `playerToken` 由服务端在加入时生成并返回；重连凭 token。

## 3. 命令/事件

- C→S `Command`：`requestId i64, verb u8, payload`；每连接单调 `clientSeq`；服务端幂等去重。
- S→C `Batch`：`tick i64, count u16, (eventType u8, payload)[]`；一个 tick 一个 Batch；按 seq。
- 事件类型与 [SessionStream](replay-format.md) 对齐；私密字段按座位过滤。
- S→C `Sync`：`kind u8, payload`（庄家/鬼牌/在线状态等）。
- `Ack`：`lastAppliedSeq i64`；客户端每 32 帧或 200ms 发一次。
- `Ping/Pong`：30s 心跳；超时 3s 判定断线。

## 4. 重连

- C→S `Resume`：`playerToken 32B, lastAckSeq i64`。
- S→C：`Welcome` + 从 seq=1 全量重放（v1 不做 checkpoint）；结束后补 `RequestOpened`（若有）。
- 座位保留 30s；到期 Bot 接管；玩家回来立即交还。

## 5. 版本与错误

- 三元组：`protocolVersion / rulesVersion / contentHash`；任一不匹配 `Reject`，客户端显示原因。
- 未知 type/长度越界/超大 payload → 断开并记日志。
- 不加密（v1 局域网/朋友局）；公网如需 TLS 在 M3 评估。
