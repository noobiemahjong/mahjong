# mahjong

四人麻将（局域网 / 单机 / 公网房间）—— Godot 4.7.2 + C#（net10）重写项目。

本仓库当前处于 **设计阶段**，尚未开始写实现代码。

## 文档

- [设计稿 v1.0](docs/DESIGN.md)：架构、tick 引擎、会话流协议、全景回放、断线重连、部署与里程碑
- [旧项目（Unity）架构总结](docs/ARCHITECTURE-unity-legacy.md)：作为规则语义与素材迁移的对照基线
- 模块精读笔记：
  - [规则引擎与算法](docs/notes/rule-engine.md)
  - [联机与连接生命周期](docs/notes/networking.md)
  - [表现层 / UI / 序列化](docs/notes/presentation.md)

## 状态

- 决策 D1–D18 已拍板（见设计稿 §0）
- 待办：M0（工程骨架 + tick 引擎 + 单机 + AOT 冒烟）

## 构建前提

- Godot 4.7.2（mono / .NET 版）
- .NET SDK 10（客户端 net10.0；服务器 JIT 发布）


## 治理

- 共享指令：[AGENTS.md](AGENTS.md)
- 文档规则：[docs/AGENTS.md](docs/AGENTS.md)
- 设计基线：[docs/DESIGN.md](docs/DESIGN.md)
- 决策索引：[docs/adr/README.md](docs/adr/README.md)
- 里程碑：[docs/milestones/README.md](docs/milestones/README.md)
- 旧版行为偏离登记：[docs/registry/旧版行为偏离登记.md](docs/registry/旧版行为偏离登记.md)
- 查表数据：[docs/TABLES.md](docs/TABLES.md)

> 治理体系裁剪自 `E:/learn/Ursa-Server` 与 `E:/learn/Ursa-Client`，见 [ADR-002](docs/adr/002-文档治理裁取Ursa两仓.md)。
