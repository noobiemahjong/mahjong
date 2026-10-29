# mahjong

开源的四人麻将游戏，使用 Godot 4.7.2 与 C#（.NET 10）实现。玩法设计为简化地方规则：无立直、无花牌，红中作鬼牌，一场 8 局，只有自摸（含七对），无点炮；目标支持单机（1 真人 + 3 Bot）、局域网 IP 直连、公网房间码对战，并提供赛后全景回放与观战。

项目处于早期开发阶段，尚无可玩版本；进度与任务状态以 [里程碑](docs/milestones/README.md) 任务表为准。

## 文档

- [开发手册](docs/handbook.md)：环境、构建、运行与工作方式
- [设计文档](docs/DESIGN.md)：目标架构、游戏循环、网络协议、回放、部署
- [架构说明](docs/architecture.md)：当前代码怎么运转
- [里程碑](docs/milestones/README.md)：阶段计划与任务状态
- [查表数据](docs/TABLES.md)：`content/tables/` 的格式与来源
- [决策记录](.agents/notes/README.md)：为什么这样设计、放弃了哪些方案

## 构建与运行

前提：

- Windows 10/11
- [Godot 4.7.2](https://godotengine.org/)（mono / .NET 版）
- [.NET SDK 10](https://dotnet.microsoft.com/download)（客户端 net10.0 + NativeAOT，服务器 JIT 发布）
- Windows AOT 导出另需 MSVC C++ 工作负载 + Windows 10 SDK

构建、测试与文档门禁：

```sh
dotnet build Mahjong.sln
dotnet test  Mahjong.sln
pwsh -File tools/doc-gate/doc-gate.ps1 -Fail
```

运行客户端：用 Godot 打开 `src/Mahjong.Client`，运行 `Startup.tscn`；完整步骤见 [开发手册](docs/handbook.md)。

## 参与开发

开发约定见 [AGENTS.md](AGENTS.md)，构建与提交流程见 [开发工作流](docs/development.md)，文档写法见 [docs/AGENTS.md](docs/AGENTS.md)；提交前请通过构建、单测与文档门禁。

## 许可证

[Apache License 2.0](LICENSE)。
