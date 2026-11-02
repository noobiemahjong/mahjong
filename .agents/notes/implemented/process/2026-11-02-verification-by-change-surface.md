# Agent Note: 验证按改动面选择，文档批不跑全量

Status: implemented — L2 收窄为文档门禁；构建+全量单测只在跨面改动、收口与 CI 跑

## Problem

`docs/development.md` 的 L2 原写作「doc-gate + 构建 + 全量单测｜改 `docs/` 或提交前」，与同节的「验收匹配改动面」及根规矩 11「验证匹配改动」冲突。只改文档也跑 build 与全量单测，验证的是未变代码，对本次改动没有新增信息，却容易把「环境绿」当成改动证据。

## Decision

L2 收窄为文档面门禁：`doc-gate`（改 `tools/doc-gate` 脚本时加其自测）；L1 仍是代码面（构建 + 相关单测）；构建 + 全量单测 + `doc-gate` 只在跨面改动、里程碑收口与 CI 跑。代码零改动的文档批不跑 build / 全量单测。

## Alternatives considered

- 保留 L2 原定义、靠「按改动面」自觉裁剪：规则文本自相矛盾，机械照抄会重复本次浪费，已实际发生。
- 删掉「提交前全量」：CI 与里程碑收口仍需全量兜底，删掉会让跨面改动失去检查。
- 增加 L2+ 全量层：多一层编号，引用面更大；现有三层按单面定义即可。

## Consequences

- `docs/development.md` §验证分层、`docs/handbook.md` §工作怎么组织、`tools/team-taskbook/templates/lead-brief.md` §3.2-13 同步；L2 的引用处不再含构建与全量单测。
- 跨面改动、里程碑收口与 CI 仍跑全量；L3 仍是里程碑收口的 AOT/金标。
- 文档批的验收证据只有 `doc-gate` 输出与退出码。

