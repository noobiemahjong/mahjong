# Agent Notes（决策记录）

本目录只放**决策的为什么**：为什么选这套、放弃了什么、以后重提时要先看什么。代码/文档能说清的事实不写这里。

## 路径与命名

````
.agents/notes/{lifecycle}/{class}/yyyy-mm-dd-topic-title.md
````

- **lifecycle**：`proposed/`（提案，未实现）、`implemented/`（已落地）、`rejected/`（已否决）、`archived/`（冻结历史）。
- **class**：`feature` / `bug-fix` / `simplification` / `architecture` / `process` / `testing`。
- 日期是**首次提出**的日期；文件名用短横线连接，不加编号。
- **禁止 `INDEX.md` / 总索引**；用目录浏览或搜索。跨笔记引用用相对链接。

## 什么时候写

只在**非平凡改动**时写（同一提交）：

| 触发 | 例 |
|---|---|
| 行为 | 玩家可见效果、判定顺序、超时/托管、分数 |
| 跨文件契约 | 协议包、事件、回放格式、表 key、版本三元组 |
| 架构 | 程序集/目录边界、seam、依赖方向 |
| 工具链/门禁 | 构建、AOT、导出、CI、doc-gate 规则 |
| 测试策略 | 什么必须测、什么不测、为什么 |
| 数据格式 | 表结构、内容格式、持久化 |

**豁免**：格式化/重排、纯重命名、不改行为的死代码删除、局部 UI 展示、跟随既有决策的局部实现。

规则：

- 同一个决策只更新**拥有它的那篇**，不新建重复篇。
- **不能把一篇 Note 改成另一个决策**：写新 Note，旧 Note 保留并交叉链接；完全被取代时才允许合并删除。
- `implemented/` 的 Note 要跟**已落地事实**更新（路径/名字/默认值），但不改决策本身。
- `implemented/` 里禁止 spec-speak（「应该/计划/验收清单」），只写现在是什么。
- `rejected/` 只在它的理由还能阻止一次重提时保留；否则删除。
- `archived/` 永久冻结，不再作为当前权威；只有 implemented 可归档。

## 文件格式

````markdown
# Agent Note: <标题>

Status: proposed | implemented | rejected — <一句话原因>

## Problem
## Decision
## Alternatives considered
## Consequences
````

- 首行标题 + 空行 + `Status:` + 空行；`Status` 必须与所在 lifecycle 文件夹一致。
- **Alternatives considered 必填**：不写，下个 agent 会把旧方案重新提一遍。
- 中文写作；不要求双语对照（与 DSH 的差异）。
- 不写操作教程、接口清单、测试走查；这些归 docs 或代码。

## 种子

- [设计基线与变更程序](implemented/process/2026-10-28-design-baseline-and-change-procedure.md)
- [文档治理与开发思路](implemented/process/2026-10-28-governance-and-doc-tiers.md)
- [v1 否决的设计方案](rejected/process/2026-10-28-rejected-v1-design-space.md)

模板：[000-template.md](000-template.md)。
