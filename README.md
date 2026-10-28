# mahjong

四人麻将（单机 / 局域网 / 公网房间）—— Godot 4.7.2 + C#（net10）重写项目。

当前处于 **设计阶段**，尚未开始写实现代码。

## 文档

- [新成员手册](docs/handbook.md)：环境、跑起来、工作方式、找东西
- [目标设计 v1.0](docs/DESIGN.md)：架构、tick 引擎、会话流协议、全景回放、断线重连、部署与里程碑
- [当前实现地图](docs/architecture.md)：M0.1 起随代码更新
- [文档标准](docs/AGENTS.md)：tier、写作规则、预算、门禁
- [Agent Notes](.agents/notes/README.md)：决策的 why、放弃的方案
- [里程碑](docs/milestones/README.md) / [事故复盘](docs/postmortem/README.md)
- [查表数据](docs/TABLES.md)
- [旧项目资料](docs/legacy/README.md)：仅素材参考，不作为设计依据

## 治理

- 共享规矩：[AGENTS.md](AGENTS.md)
- 开发工作流：[docs/development.md](docs/development.md)
- 文档标准：[docs/AGENTS.md](docs/AGENTS.md)
- 决策记录：[.agents/notes/README.md](.agents/notes/README.md)

> 治理与开发方式参考 Ursa-Server / Ursa-Client 与 DeepSeek Harness 的文档体系；**不与旧 Unity 版做行为等价**。

## 状态

- 决策 D1–D18 已拍板（见设计稿 §0）
- 待办：[M0.1 工程底座](docs/milestones/M0.1-工程底座与内核骨架.md) → [M0.2 单机闭环](docs/milestones/M0.2-单机可玩闭环.md)

## 构建前提

- Godot 4.7.2（mono / .NET 版）
- .NET SDK 10（客户端 net10.0 + NativeAOT；服务器 JIT 发布）
- Windows AOT：MSVC C++ 工作负载 + Windows 10 SDK（M0.1 T-6 冒烟前置）
