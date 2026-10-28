# docs/AGENTS.md（文档与知识树规则）

适用 `docs/**`。全仓规则见根 [AGENTS.md](../AGENTS.md)；本文件只声明**文档放哪、怎么写、怎么精简**，不复述根文件。

## 开工先读顺序

`README.md` → 根 [AGENTS.md](../AGENTS.md) → 本文件 → [DESIGN.md](DESIGN.md) → [adr/README.md](adr/README.md) → 对应 milestone / registry / pitfalls。

## 文档树（新文档放哪）

| 位置 | 唯一职责 |
|---|---|
| `DESIGN.md` | **设计基线**：是什么/为什么这么设计；D1–D18 与各章节是权威；只经 ADR 变更 |
| `TABLES.md` | 查表数据的格式、来源、差异证据、生成计划 |
| `ARCHITECTURE-unity-legacy.md` | 旧 Unity 项目架构总结（**历史参考**，不是新项目权威） |
| `notes/` | 旧项目模块精读（**历史参考**） |
| `adr/` | **决策**：选了什么 + 为什么 + 放弃了什么；索引先读 |
| `milestones/` | 阶段 master + 任务文档；**状态唯一权威在任务表** |
| `porting/` | 旧→新映射、rejected-alternatives、pitfalls |
| `registry/` | **有意偏离旧版行为**的登记表 |
| `reference/` | 外部素材勘察与可复现证据链（含命令/路径） |
| `bugs/` | 我们自己的缺陷与事故诊断 |

**放不下时怎么选**：① 判「读者是谁」——读代码的人要的进注释；下一个做决定的人要的进 `adr/` 或 `porting/rejected-alternatives.md`；可复现证据进 `reference/`；任务判据与实施记录进 `milestones/`；我们自己的缺陷进 `bugs/`。② 只是「我当时怎么查出来的」不落盘，除非它本身是可复现证据链。

## 非平凡改动判定（必须留痕）

命中任一类 ⇒ 必须留痕（载体按根文件 §1 判）：

| 触发类 | 例 |
|---|---|
| 行为 | 玩家可见效果、判定顺序、超时/托管、分数、提示文案 |
| 架构 | 程序集/目录边界、谁能调谁、跨层依赖 |
| 跨文件契约 | 协议包、事件、内容格式、表 key、版本三元组 |
| 工具链 | 构建、AOT、导出、CI、门禁、git hooks |
| 测试策略 | 什么必须测、什么不测、为什么 |
| 数据/网络/配置格式 | 表结构、回放格式、房间设置、contentHash |

**豁免**：格式化/重排、纯重命名、不改行为的死代码删除、按已登记项做回补、跟随既有决策的局部实现（不推翻不扩展）。

## 写作口径

- **禁历史变迁词**：不写「以前/现在/不再/曾经」，只写当前事实；变迁过程归 git 与 ADR。
- **禁范围字面量**：不写 `001~017` 这类区间；用目录链接或可解析引用（`ADR-002`、`pitfalls/003`、`D12`）。
- **日期取真值**：写进 docs 的日期必须来自 git 提交日或当天真实日期；禁推断/未来日期。
- **指针优先**：上位文档只写定位与链接；同一事实只写一处。
- **表格不变量**：行内不得含未转义 `||`；单元格数必须与表头一致。
- **编码**：UTF-8 无 BOM、LF；行尾交给 git 归一，不用脚本「统一行尾」。
- **体量**：遵守根文件 §6 的预算；上限只降不升，上调须同提交改基线并说明理由。
- **不写进度速览**：进度以 milestones README + 各任务表为准，不在入口文件维护。
- **不手抄生成物**：查表、协议常量、版本号由源文件/工具产生，文档只链接。
- **旧结论不改**：已发布 ADR 不重写；新决策写新 ADR，并在 ADR 索引状态列标注收窄关系。

## 模板与门禁

- ADR：[adr/000-template.md](adr/000-template.md)；索引与状态取值见 [adr/README.md](adr/README.md)。
- 里程碑：[milestones/TEMPLATE.md](milestones/TEMPLATE.md)。
- Pitfall：[porting/pitfalls/000-template.md](porting/pitfalls/000-template.md)。
- Bug：[bugs/000-template.md](bugs/000-template.md)。
- 门禁规则与预算：根 [AGENTS.md](../AGENTS.md) §6；实现（`tools/doc-gate`）是 M0 交付，**未实现前不假装已跑**。
