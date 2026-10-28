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
