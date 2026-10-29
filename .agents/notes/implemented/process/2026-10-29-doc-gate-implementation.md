# Agent Note: doc-gate 落地：登记覆盖、夹具布局与脚本口径

Status: implemented — T-2 交付的三处口径按此定稿。

## Problem

T-2 交付 doc-gate 时有三件事必须在规则层面定死，否则门禁一开就是噪音：① 预算注册表要覆盖到什么粒度——只登记预算表已分类的文档、其余一律「未登记」警告，G7 就退化成永久噪音，而棘轮的既有判定是无容忍值的；② 违规夹具本身是 `*.md`，既要「违规必红」又不能污染仓库自检，还得保住 BOM/CRLF 的真实字节，否则 G2/G3 夹具一旦被 git 规范化就永远测不到东西；③ 门禁脚本自身受 G9 纯 ASCII 约束，而它必须处理中文路径与中文提示，并且 G9 的适用范围照字面 glob 会连 `tools/**/README.md` 一起判红。

## Decision

1. 登记覆盖：扫描范围内每篇 `.md` 按精确路径全量登记 `doc-budgets.json`（72 条），同批写 `doc-budgets-baseline.txt`；上限取 [docs/AGENTS.md](../../../../docs/AGENTS.md) 预算表，表内含「各级 `README.md` / 模板 / `CLAUDE.md` 4 KB」兜底与 `docs/legacy/**` 64 KB 一行，legacy 与各级 README 因此有明确上限而不落进未登记。G7 的「未登记」与「登记了但文件不存在」都只记 WARN（注册表漂移信号）；G8 无容忍值，判定为 `json ≤ baseline`，只校验两者一致。
2. 夹具布局：`tools/doc-gate/tests/fixtures/<case>/{bad,good}` 双根，每个扫描根自带最小合法注册表 `{"version":1,"limits":[]}` 与空 baseline，用 `-Root <fixture>` 跑门禁；`.gitattributes` 对 `tools/doc-gate/tests/fixtures/**` 声明 `-text`，BOM 与 CRLF 以真实字节入库。仓库根扫描排除该前缀，夹具不参与自检。
3. G9 范围：只查会被 shell 或 PowerShell 执行的脚本——`tools/**/*.ps1` 与 `tools/git-hooks/` 下无扩展名钩子；`*.md` 与测试数据不查。脚本源码纯 ASCII，读文档走 `ReadAllBytes` 加显式 UTF-8 解码，输出统一 UTF-8、路径一律 `/`。
4. 其余口径：G2 的非法 UTF-8 记 WARN，BOM、CR 等字节级问题记 ERROR；G11 只机械校验编号连续，坑清单与 README 的「编号区间」文字归评审；G1 只查行内链接与图片，仓外目标 WARN 不拦；commit-msg 标题按字符数判定（中文 `20 字符/60 字节` 通过），实现用 `od -An -v -tu1` 做字节分类计数。

本 Note 补充 [2026-10-29-doc-gate-and-ci-acceptance.md](2026-10-29-doc-gate-and-ci-acceptance.md) 而不替代它：那篇归属「CI 首绿由用户回填、G8 无容忍值、夹具靠 `-Root` 隔离」的判定，这篇归属交付时的登记粒度、夹具形态与脚本口径。

## Alternatives considered

- **只登记预算表已分类的文档，其余留 WARN**：实现最省事，但每篇新增文档都会变成永久噪音，正是棘轮要消灭的对象；重开条件是出现能自动给新文档分类的元数据。
- **用 git 跟踪文件列表代替目录扫描**：能天然排除夹具与 vendor，但 `-Root` 夹具根不是 git 仓库，门禁在 CI 之外无从判定；重开条件是门禁只在仓库根运行。
- **夹具用「单根 + `run.ps1` 运行时修复」**：坏/好两个状态散在测试代码里，评审看不出哪里违规、哪里合规；重开条件是夹具数量膨胀到双根维护成本超过收益。
- **把 `docs/legacy/**` 整体排除出扫描**：预算表已给 64 KB 上限，排除等于放弃约束；重开条件是 legacy 规模大到单篇上限失效。
- **让 G9 照字面 glob 覆盖 `tools/**` 全部文件**：会把中文 `README.md` 判红，与工具文档用中文的要求冲突；重开条件是仓库改用英文文档。

## Consequences

- 新增文档必须同批登记 `doc-budgets.json` 与 `doc-budgets-baseline.txt` 两处：漏 json 只出 G7 警告，漏 baseline 则 G8 报错。
- [docs/AGENTS.md](../../../../docs/AGENTS.md) 预算表的「各级 `README.md` / 模板 / `CLAUDE.md` 4 KB」与「`docs/legacy/**` 64 KB」两行是登记上限的唯一出处，调额须同批改 json、baseline 与 Note。
- doc-gate 输出统一 UTF-8、路径 `/` 分隔，Windows 控制台需 65001 才不乱码。
- CI 每次 push/PR 跑 doc-gate 与钩子测试；夹具保真靠 `.gitattributes` 的 `-text`，改动该行会静默削弱 G2/G3 夹具。
- hook 的字符计数依赖 `od -v`；换平台或换 `od` 实现时先复跑 [tools/git-hooks/tests/run.ps1](../../../../tools/git-hooks/tests/run.ps1) 的中文标题用例。
