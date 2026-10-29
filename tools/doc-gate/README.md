# doc-gate（文档门禁）

扫描 `-Root`（默认仓库根）下所有 `*.md`，按 G1–G12 报告问题。规则原文见 [docs/AGENTS.md](../../docs/AGENTS.md) 的门禁表，本文件只写用法与约定。

## 用法

```powershell
pwsh -File tools/doc-gate/doc-gate.ps1 [-Root <dir>] [-Fail]
```

`-Root` 为扫描根，默认取脚本上两级（仓库根）；`-Fail` 只让 ERROR 影响退出码，WARN 永不阻断。

## 输出与退出码

每条发现一行：`<ERROR|WARN> <Gid> <relpath>:<line> <message>`，行号为 0 表示文件级或配置级问题，`relpath` 用 `/`。末尾两行统计：

```text
doc-gate: N files, E errors, W warnings
doc-gate: OK|FAIL
```

有 ERROR 且给了 `-Fail` 时退出 1，否则退出 0。注册表缺失、JSON 非法、schema 违规计为 ERROR；扫描根不存在等配置级问题以 `G0` 报告。输出统一为 UTF-8，路径一律用 `/` 分隔。

## 规则清单

细节以 [docs/AGENTS.md](../../docs/AGENTS.md) 的门禁表为准。

| 编号 | 判定 |
|---|---|
| G1 | 仓内相对链接可解析，仓外只警告 |
| G2 | 无 UTF-8 BOM |
| G3 | 仅 LF |
| G4 | 表单元格数等于表头，无未转义双竖线 |
| G5 | 单行 ≤5000 字符 |
| G6 | `docs/milestones/README.md` 表行 ≤300 字符 |
| G7 | 每篇不超过登记预算，未登记只警告 |
| G8 | 预算 json ≤ baseline |
| G9 | 会被执行的脚本纯 ASCII |
| G10 | Note 路径、Status、四段结构合规 |
| G11 | pitfalls 编号连续 |
| G12 | 一段一行（只警告） |

## 预算注册表与 baseline

`doc-budgets.json` 按文档精确路径登记 `path` 与 `limitBytes`：路径相对仓库根、`/` 分隔、大小写敏感、不写通配与 `..`。登记覆盖每篇被扫描到的 `.md`；上限取 [docs/AGENTS.md](../../docs/AGENTS.md) 预算表（各级 README、模板、指针 4KB，legacy 64KB 等）。`doc-budgets-baseline.txt` 每行 `path<TAB>limitBytes`，是棘轮基准：G8 要求 json 中每个路径都在 baseline 中且 json 值不大于 baseline 值，baseline 多出的行只警告。新增文档同批登记两处；上调预算另需 Agent Note；登记了但文件不存在只警告（提示注册表漂移）。

## 扫描排除

跳过路径任一段为 `.git`、`content`、`lib`、`bin`、`obj`、`build`、`addons` 的目录，并跳过相对前缀 `tools/doc-gate/tests/fixtures/`。`addons` 是 Godot 插件的 vendor 内容，不属本仓文档标准；夹具目录里的文档必须能保持「红」，不能参与仓库自检。

## 夹具

夹具位于 `tests/fixtures/<case>/{bad,good}`，每个扫描根自带最小合法注册表 `tools/doc-gate/doc-budgets.json`（`{"version":1,"limits":[]}`）与空 `doc-budgets-baseline.txt`，否则 G7/G8 会因注册表缺失报错。`tests/run.ps1` 逐例跑 bad 断言退出 1 且命中目标 G 编号，再跑 good 断言退出 0。新增夹具：建 bad/good 两个根、按需登记超限文档、在 `run.ps1` 的用例表加一行。BOM 与 CRLF 夹具以真实字节保存，`.gitattributes` 的 `tools/doc-gate/tests/fixtures/** -text` 防止换行被改写。

## 实现口径

- G9 只查会被 shell 或 PowerShell 执行的脚本：`tools/**/*.ps1` 与 `tools/git-hooks/` 下无扩展名文件（如 `commit-msg`）；`*.md` 与测试目录不查。
- 本工具与夹具脚本源码纯 ASCII；读文档一律 `ReadAllBytes` 加显式 UTF-8 解码，不用默认编码。
