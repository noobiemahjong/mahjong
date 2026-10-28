# Agent Note: Client 引用 Session（单机/局域网权威会话在宿主进程内）

Status: implemented

## Problem

设计稿写「Client → Core；Server → Session → Core」，但单机模式需要权威 `GameSession` 在客户端进程内运行，局域网房主也需要在客户端进程内起会话。M0.2 T-5/T-6、M1 录制、M2 房主、M3 复用都依赖它；按原依赖表无法实现，新 agent 只能自行改架构。

## Decision

1. **依赖方向改为**：Client → Session → Core；Server → Session → Core；Core 不反向依赖。
2. `Mahjong.Session` 承载：`GameSession`、座位/流产出、本地回环、网络宿主（`TcpSessionHost`）、录制。
3. `Mahjong.Client` 引用 Session；AOT `TrimmerRootAssembly` 增加 `Mahjong.Session`。
4. 单机 = Client 进程内 Session + 本地回环；局域网房主 = Client 进程内 Session + TCP 宿主；公网 = 独立 Server 进程。
5. `GameState/规则/Flow` 仍在 Core；Session 不反向引用 Client/Godot。

## Alternatives considered

- **把 GameSession 下沉到 Core**：会让 Core 依赖网络/宿主概念，破坏「引擎无关内核」；且 Server/Client 都要宿主逻辑。
- **Client 只引用 Core，单机另写一套逻辑**：形成双路径，测试面翻倍（R-009 已否决）。
- **单机用 Server 进程 + 回环 TCP**：多进程调试成本高，且 AOT/启动复杂。
- **Client 引用 Server 工程**：Server 是 `Exe` 且含宿主入口，引用不干净。

## Consequences

- 需同步改：`DESIGN` §3.2/§13、`architecture.md`、`src/AGENTS.md`、M0.1/T-1 的工程引用与 AOT root、M2/M3 的宿主说明。
- AOT 裁剪面增加 Session 程序集；T-6 冒烟要覆盖。
- 依赖方向仍无环：Client → Session → Core。
