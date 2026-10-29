# Agent Note: 门禁可机械判定，CI 首绿由用户回填

Status: implemented — doc-gate 规则与 M0.1 收口判据按此定稿。

## Problem

原规格有三处会在交付时露馅：① M0.1 完成定义要求「CI 绿」，但红线是「不 push」，agent 永远无法自证收口；② G8 棘轮允许同提交改 json+baseline，且 4096 字节容忍大于 pitfall 的 4KB 预算，等于没有棘轮；③ 违规夹具本身是 `*.md`，会被同一遍扫描命中，「修复后绿」不可达；④ G10/G11 会把自己的模板与 README 判红，G12 没有元数据头豁免，现状就会满屏警告。

## Decision

1. M0.1 的 CI 判据改为：CI workflow 就绪 + 本地等价命令（`dotnet build` / `dotnet test` / doc-gate）全绿即满足；CI 首绿由用户 push 后回填，回填前 M0.1 结果记「CI 待远程确认」。
2. G8 去掉容忍值，判定为 `json.limitBytes ≤ baseline`；提额=同提交改 baseline + Agent Note，门禁只校验一致性，合理性归评审。
3. doc-gate 增加 `-Root`；夹具放 `tools/doc-gate/tests/fixtures/<case>/`，用 `-Root` 跑红；仓库根扫描排除该目录。
4. G10 豁免 `README.md`/`AGENTS.md`/模板；G11 的 `000-template.md` 不计入编号，首篇 001；G12 只警告，豁免元数据头块、引用块、缩进续行。
5. 单篇 task 预算 8KB → 10KB（T-3 已到 7462B，写实施记录必超）。

## Alternatives considered

- **强制 CI 真绿才收口**：判据最硬，但把收口卡在用户手上，M0.1 会长期挂着「未收口」。
- **授权 agent push**：破坏红线，且会让「本地没跑过也能绿」变成可能。
- **G8 读 `git show HEAD:` 的 json 做比较**：能防同提交提额，但实现复杂、在 CI 之外无意义；收益不抵成本。
- **不设 G12**：现状 81 处命中，删掉规则等于放弃「一段一行」，而它正是文档可 diff 的基础。

## Consequences

- 「CI 待远程确认」成为 M0.1 结果里的合法状态；用户 push 后补一行证据即可转绿。
- 提额必须同时出现在 json、baseline、Note、提交信息里，缺一项评审打回。
- 新增文档必须同提交登记预算，否则 G7 只警告、不会拦。
- M0.1 的 T-2 交付时，doc-gate 的扫描范围与夹具路径是本 Note 定的。
