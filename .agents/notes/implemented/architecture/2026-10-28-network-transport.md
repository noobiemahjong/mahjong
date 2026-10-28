# Agent Note: v1 网络传输用 TCP（.NET Sockets + Pipelines），不用 Godot 传输

Status: implemented

## Problem

设计稿只写了「自研协议 + TCP/WebSocket」，没有锁定**用什么实现 TCP**。换一个 agent 可能选：
Godot `StreamPeerTCP`、Godot 高层 MultiplayerAPI、WebSocket、LiteNetLib、或裸 `Socket` 各写一套。
传输实现一旦分散到客户端/服务端两个不同的 API，协议码就无法共享，重连/测试/AOT 也会各踩各的坑。

## Decision

1. **v1 传输 = TCP**，客户端与服务端都直接用 .NET `System.Net.Sockets`（`TcpClient`/`TcpListener` 或 `Socket`），
   加 `System.IO.Pipelines` 做收发缓冲；**不用 Godot 的传输 API**。
2. 协议编解码放 `Mahjong.Core.Protocol`（纯 C#），客户端/服务端共享；帧格式见 [protocol](../../../../docs/specs/protocol.md)。
3. 线程模型：每连接一个读任务 + 一个写队列；命令/事件在 tick 边界交给会话；会话状态单线程，无锁。
4. **LAN 发现用 UDP**（`UdpClient` 广播/单播），只做发现，不承载对局数据。
5. HTTP 只在 M3 用（ASP.NET Core/Kestrel，服务端 JIT）；v1 不做 TLS。
6. 传输抽象为 `ITransport`/`IConnection` 接缝；未来要 WebSocket 或 LiteNetLib，只换实现，不改协议与上层。
7. 不引入第三方网络库（LiteNetLib/ENet/Photon 等）；不把 Ursa/SGS 的传输实现搬进来。

## Alternatives considered

- **Godot `StreamPeerTCP` / MultiplayerAPI**：绑定引擎 API，服务端无法复用；高层 API 的会话/重连/版本语义不满足需求。R-005 已否决。
- **WebSocket v1**：多一层 HTTP 握手与帧封装，对原生客户端无收益；需要网页版时在 `ITransport` 后加实现即可。
- **LiteNetLib / ENet（可靠 UDP）**：回合制对延迟不敏感，TCP 的有序可靠已够；多一个依赖与 AOT 验证面。若以后要做实时观战/语音再评估。
- **自研可靠 UDP**：重复造轮子，且丢包/拥塞/重连语义风险高。
- **每个平台各用一种传输**：协议码分叉，重连/回放/测试无法共享。

## Consequences

- 客户端/服务端共享同一套帧编解码与测试；AOT 只需验证 .NET Sockets/Pipelines（均为 AOT 安全）。
- TCP 队头阻塞存在，但回合制可接受；不做「实时位置同步」。
- 协议文档必须写清帧边界、最大长度、超时；M2 的 T-2 按此实现。
- 未来换 WebSocket/LiteNetLib 时，改 `ITransport` 实现并重跑 M2 的重连/观战测试。
