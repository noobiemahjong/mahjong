# 移植与对照

本目录负责「旧 Unity 项目 → 新 Godot 项目」的映射、差异与坑。

## 规则

1. **旧版行为是默认基线**：没有在 [registry](../registry/旧版行为偏离登记.md) 登记的行为差异，一律按缺陷处理。
2. **查表不可改**：`content/tables/*.bytes` 是唯一事实来源（[TABLES](../TABLES.md)）；任何替换/重生成需 ADR + 逐条对比。
3. **UI/素材是重写不是移植**：贴图/音频/BMFont 复用；Prefab/场景/Animator/uGUI 布局/DOTween 序列重写。
4. **旧项目文档只作参考**：[ARCHITECTURE-unity-legacy](../ARCHITECTURE-unity-legacy.md) 与 [notes](../notes/) 是历史分析，不是新项目权威。
5. **坑写进** [pitfalls/](pitfalls/README.md)；**否决方案写进** [rejected-alternatives.md](rejected-alternatives.md)。

## 对照表（随实现补充）

| 旧项目 | 新项目 | 状态 | 说明 |
|---|---|---|---|
| `GameRule/Games/MahjongGame` | `Mahjong.Core` 的 Flow | 待移植 | 拆成 tick 引擎 + 触发器 |
| `GameRule/Cards` / `CompositeCard` | Tile / Meld | 待移植 | 保留 id 语义，去掉 Netcode 序列化 |
| `GameRule/Skills` | Skill + content/skills.json | 待移植 | 逻辑移植，配置外置 |
| `Algorithm/WinningCardsInfo/Table` | 查表读取 | 待移植 | 数据沿用 [TABLES](../TABLES.md) |
| `GameplayObjects/Algorithm/BotDecisionMaker` | BotPolicy | 待移植 | 先沿用旧表，M4 再优化 |
| `GameDataSource`（SO） | ContentCatalog | 重写 | 去 ScriptableObject |
| `NetworkGameState/ServerPlayer/RPC` | SessionStream + Command | 重写 | 见 DESIGN §6 |
| `GameView/PlayerView/Card*` / uGUI | Godot 场景 + Control | 重写 | 交互与布局重做 |
| DOTween | Godot Tween | 重写 | — |
| VContainer / UniTask / Netcode | — | 移除 | 新设计不使用 |
