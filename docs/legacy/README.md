# Legacy（旧 Unity 项目资料）

本目录是重写前对旧 Unity 项目的架构总结与模块精读：

- [ARCHITECTURE-unity-legacy.md](ARCHITECTURE-unity-legacy.md)：规则/网络/表现层总览
- [rule-engine.md](rule-engine.md)：规则引擎与算法
- [networking.md](networking.md)：连接状态机、Lobby/Relay、基础设施
- [presentation.md](presentation.md)：表现层、UI、音频、序列化

**定位：历史资料/素材参考，不是设计依据，也不是验收基线。**

- 设计以 [DESIGN](../DESIGN.md) 与 [.agents/notes](../../.agents/notes/README.md) 为准；
- 不要求与旧版行为等价，不做逐项对照，不登记旧版差异；
- 需要复用规则语义、数值、素材时查这里；「旧版为什么这样」不作为理由。
- 本目录保留迁移原文（含旧文件行号），不受「禁行号」规则约束；它不是本仓代码的引用。

若发现事实错误：不改写原文，在 [reference](../reference/README.md) 增加带复现命令的订正，并在此留一行指针。
