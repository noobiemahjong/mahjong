# Agent Note: 审核会话独立性与超期判定

Status: implemented — 审核一次一审 + 复审双轨成文；超期判据与 PS 脚本落地

## Problem

标准已有「独立审核、零信任上游」，但没写死审核会话的生命周期：没有一次一审、版本标识、禁止沿用上轮判断与复审双轨，也没有会话超期判据。长期驻留的审查者会锚定自己的旧结论、把「已 PASS」当本轮前提，上下文裁剪后忘记自己要求过什么，发现率随轮次下降，「修 A 坏 B」最容易被漏；而自报步数无法独立核验。

## Decision

审核按「单元 × 轮次」开一次性会话（`subagent` 即可），任务书四件套 = 产物路径 + 版本标识 + 检查清单 + 上一轮 findings（标注待核、禁止沿用上轮判断）；`teammate` 只做争议裁决；复审双轨 = 全量重审 + findings 逐条闭环；超期判据 = >80 步 / 平均上下文 >200K / 同一产物第 4 轮，命中即换新会话。

工具落地为 `tools/team-taskbook/check-review-sessions.ps1`：读 DSH 会话投影缓存，输出步数、平均上下文、EXPIRED，支持 `-List` / `-Json`，退出码 0/1/2；纯 ASCII。阈值保留上游的 200K：本仓单元预算 ≤150K 是规划值，200K 是审查独立性上限（超过即认为审查者受上下文裁剪影响）。

## Alternatives considered

- 原样引入上游 `check-review-sessions.mjs`：Node 不在本仓工具链与 CI，且脚本与 DSH 缓存格式耦合；改 PS 可与现有 `tools/doc-gate` 一致，并复用同一缓存读取口径。
- 只写规则、不做可核验：超期只能靠审查者自报，纪律无法独立检查。
- 只做脚本、不写规则：没有一次一审与双轨复审，脚本判出的超期也无处置依据。
- 不迁移：维持现状，长驻审查者的锚定与「修 A 坏 B」漏洞仍在。

## Consequences

- `lead-brief.md` §3.2-3 与 §3.4、`cell-brief.md` §2 审核文件头四行；`tools/team-taskbook/README.md` 工具节与文件表、`tools/AGENTS.md` 工具清单同步。
- 本 Note 登记预算 8192；脚本纯 ASCII，受 G9 约束；用法与阈值写在 README。
- 验收：`doc-gate` 0 ERROR；脚本用合成 fixture 测 OK / EXPIRED（步数、上下文）/ NO_DATA / 坏记录 / 缺目录。

