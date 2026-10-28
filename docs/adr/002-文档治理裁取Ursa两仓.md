# ADR-002：文档治理裁取 Ursa-Server / Ursa-Client

状态：已确认
日期：2026-10-28
相关：根 AGENTS.md、docs/AGENTS.md、docs/adr/001

## 背景

`E:/learn/Ursa-Server` 与 `E:/learn/Ursa-Client` 是两套已跑通的 vibe coding 仓库：前者 49 篇 ADR + 纪律路由表 + 门禁测试，后者 21 篇 ADR + doc-gate + git hooks + CI + MCP 硬门禁。

麻将项目规模小得多（单人主导、Godot + C#、规则/回放/联网三条主线），**不能整套照搬**，否则治理成本会超过开发成本（Ursa-Server 自己也是被「过度文档化」教训后才收窄的）。本 ADR 记录裁取结果。

## 决策

采用下列机制，并按麻将项目实际情况调整：

| 机制 | 来源 | 处理 | 调整说明 |
|---|---|---|---|
| 根 `AGENTS.md` + 子树 `AGENTS.md` + 读序/冲突裁决 | 两仓 | **保留** | 目前只有根 + `docs/` 两个；`src/`、`tools/` 落地时再建 |
| 三载体分工（注释/commit/docs） | 两仓 | **保留** | 锚点从「Java 类.方法」换成「旧 Unity 类.方法 / 文件路径」；禁本仓行号 |
| `docs/` 是项目状态唯一权威 | 两仓 | **保留** | DESIGN/TABLES 为常设文档，notes/ARCHITECTURE 降为历史参考 |
| ADR（一条一决策 + 索引 + 冻结 + 收窄指针） | 两仓 | **保留** | 只保留 `000-template` + 两条种子 ADR；不追求 49 篇规模 |
| rejected-alternatives | 两仓 | **保留** | 麻将版聚焦：Unity 续命、Netcode/UGS、seed 重放、protobuf-net、Godot MultiplayerAPI、checkpoint、中继打洞等 |
| 里程碑 master + 任务文档 + 状态唯一权威 | Ursa-Client ADR-014 | **保留** | 状态只在任务表；入口 README 不写进度 |
| pitfalls 编号篇（现象/根因/正确做法/验证） | 两仓 | **保留** | 新建前先 `Glob` 避让；只写仍有效结论 |
| registry（有意偏离基线） | Ursa-Client | **保留并换基线** | 从「偏离官方客户端」改为「偏离旧 Unity 版本行为」 |
| bugs 与决策/坑分家 | 两仓 | **保留** | 事故诊断单独放 |
| reference 证据链（复现命令/路径） | Ursa-Client | **保留** | 查表解码、AOT 取证、SGS 对照都归这里 |
| 文档预算 + 棘轮 | Ursa-Client ADR-010 | **保留但粗粒度** | 先按类别给上限，不做逐文件 census；M0 起冻结 |
| doc-gate（G1–G11）+ commit-msg hook + CI | Ursa-Client ADR-011/017 | **保留为 M0 交付** | 当前只有规范（根 AGENTS §6），**未实现前不声称已跑** |
| 验证分层 L1/L2/L3 | Ursa ADR-011/027 | **保留** | L3 改为「AOT 冒烟 + 金标回放」 |
| Conventional Commit + 一任务一提交 + 不 push + 收尾汇报 | 两仓 | **保留** | 标题 ≤50 字、body ≤10 个非空行 |
| MCP 优先（Godot 硬门禁 / Roslyn 可降级） | Ursa-Client ADR-012/020、Server ADR-020 | **保留** | 麻将只有 Godot + C# 两条通道；数据表用仓库内工具/实读 |
| 团队单元化（任务书/写作用域/Lead 复核） | Ursa-Server ADR-048 | **保留精简版** | 一单元一产物、≤80 步/150K、批次 ≤8、同时 ≤5 |
| GitHub Milestone/Issue 镜像 | Ursa-Client ADR-013 | **暂不采用** | 单人开发阶段收益低；需要外部协作再评估 |
| 客户端逆向目录 / red-test 变异台账 / doc-slim 全量普查 | Ursa-Client | **暂不采用** | 麻将没有客户端逆向面；变异测试与全量普查成本 > 收益，M2 后再评估 |
| Java 基线 / submodule / 脚本转换工具链规则 | Ursa-Server | **不适用** | 旧 Unity 项目是只读参考，不做 submodule；无脚本转换 |
| 49 篇 ADR 的体量 | Ursa-Server | **不采用** | 按需写；ADR 只在「改变基线或不可逆选择」时新增 |

## 理由

1. **只保留有判别力的机制**：三载体、ADR、registry、pitfalls、milestones 都能直接防止「漂移/失忆/重复」；而 census/变异台账在单人小仓里维护成本高于收益。
2. **门禁必须后置**：M0 没有代码与文档规模，先实现 doc-gate 会阻塞开工；因此规范先立、工具在 M0 收尾前补齐，且**未实现就写「待实现」**。
3. **基线换位**：Ursa 的基线是官方客户端/Java 服务端，麻将的基线是旧 Unity 项目 + 设计稿；因此把「registry 偏离官方」改为「偏离旧版」，把「Java 锚点」改为「旧 Unity 符号锚点」。

## 放弃了什么

| 方案 | 否决理由 | 重开条件 |
|---|---|---|
| 完整复制 Ursa-Server 治理（49 ADR + 19 纪律 + 全量门禁） | 治理成本压过开发；多数规则针对大仓/多人 | 团队 >3 人且里程碑并行 >4 时再评估 |
| 不做治理，只写 DESIGN | 决策失忆、重复踩坑、回放/协议契约漂移 | 不重开 |
| 把所有规则塞进根 AGENTS.md | 单文件膨胀、子树无法按需加载 | 不重开；按子树拆分 |

## 后果

- M0 必须交付：doc-gate（G1–G11）、commit-msg hook、CI（构建 + 单测 + 文档门禁，无 paths 过滤）。
- 预算与门禁规则已写在根 AGENTS §6；实现前不得作为验收证据。
- 若治理成本开始压过开发（例如每周维护文档 >2 小时），按本 ADR 的「暂不采用」清单重新收窄，而不是继续加规则。
