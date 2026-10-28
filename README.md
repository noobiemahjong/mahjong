# mahjong

四人麻将（单机 / 局域网 / 公网房间）—— Godot 4.7.2 + C#（net10）重写项目。

当前处于 **设计阶段**，尚未开始写实现代码。

## 文档

- [设计稿 v1.0](docs/DESIGN.md)：架构、tick 引擎、会话流协议、全景回放、断线重连、部署与里程碑
- [查表数据](docs/TABLES.md)：6 个 .bytes 的格式、来源、解码方法
- [旧项目架构总结](docs/ARCHITECTURE-unity-legacy.md)：仅素材参考，不作为设计依据
- [文档治理](docs/AGENTS.md) / [ADR 索引](docs/adr/README.md) / [里程碑](docs/milestones/README.md)

## 治理

- 共享指令：[AGENTS.md](AGENTS.md)
- 文档规则：[docs/AGENTS.md](docs/AGENTS.md)
- 设计基线：[docs/DESIGN.md](docs/DESIGN.md)
- 决策索引：[docs/adr/README.md](docs/adr/README.md)
- 工程坑与否决方案：[docs/engineering/](docs/engineering/README.md)
- 里程碑：[docs/milestones/README.md](docs/milestones/README.md)

> 治理与开发方式裁剪自 `E:/learn/Ursa-Server` 与 `E:/learn/Ursa-Client`；**不与旧 Unity 版做行为等价**，见 [ADR-002](docs/adr/002-文档治理裁取Ursa两仓.md)。

## 状态

- 决策 D1–D18 已拍板（见设计稿 §0）
- 待办：M0（工程骨架 + tick 引擎 + 单机 + AOT 冒烟 + 文档门禁）

## 构建前提

- Godot 4.7.2（mono / .NET 版）
- .NET SDK 10（客户端 net10.0 + NativeAOT；服务器 JIT 发布）
