# 新成员手册

> 目标：**不依赖任何人的口头记忆，拿到仓库就能继续开发。** 本文件是入口；细节按链接走。

## 1. 这个项目是什么

四人麻将（单机 / 局域网 / 公网房间），Godot 4.7.2 + C#（net10）重写；进度与任务状态以 [里程碑索引](milestones/README.md) 与各阶段任务表为准，当前还没有可玩的牌局。

## 2. 环境

| 项 | 要求 |
|---|---|
| 系统 | Windows 10/11（M0–M4 只支持 Windows；移动端在 M5） |
| .NET SDK | **10.0.401**；`global.json` 锁定版本 |
| Godot | **4.7.2 mono/.NET**，本机路径 `D:\Program Files\Godot_v4.7.2-stable_mono_win64\` |
| AOT 工具链 | MSVC C++ 工作负载 + Windows 10 SDK（`dotnet publish` AOT 前置）；用 `vswhere`/`ls "C:\Program Files (x86)\Windows Kits\10\Include"` 自检 |
| Git | 能执行 `git config core.hooksPath tools/git-hooks`（安装提交钩子） |
| MCP | C# 符号 MCP：Visual Studio 打开 `Mahjong.sln` 后托管，端点 `http://localhost:3011/SDK/`（DSH 注册名 `mahjong`）；Godot MCP 另按需启用；开工先探活，见 [development](development.md) |
| 内容 | `content/` 已在仓库；查表 6 个 `.bytes` 在 `content/tables/` |
| Android（M5） | JDK 17、Android SDK 35、NDK 27、adb、Godot Android 导出模板；M5 T-0 探针确认 |

## 3. 第一次跑起来

````sh
git clone <repo>
cd mahjong
git config core.hooksPath tools/git-hooks   # 每克隆一次
dotnet build Mahjong.sln -warnaserror
dotnet test  Mahjong.sln
pwsh -File tools/doc-gate/doc-gate.ps1 -Fail
pwsh -File tools/git-hooks/tests/run.ps1     # 钩子自检
pwsh -File tools/aot-smoke/aot-smoke.ps1     # AOT 导出 + 独立 exe 冒烟
````

命令的期望输出、退出码与失败处置见 [cookbook/build](cookbook/build.md)；运行客户端见 [cookbook/run](cookbook/run.md)，AOT 导出与冒烟见 [cookbook/aot-export](cookbook/aot-export.md)。

## 4. 读文档顺序

1. 根 [AGENTS.md](../AGENTS.md)：常驻规矩。
2. [docs/AGENTS.md](AGENTS.md)：文档放哪、怎么写。
3. [architecture.md](architecture.md)：当前实现地图；目标设计看 [DESIGN.md](DESIGN.md)。
4. [DESIGN.md](DESIGN.md)：目标设计与决策索引。
5. [.agents/notes/](../.agents/notes/README.md)：为什么这么定、放弃过什么。
6. 当前里程碑计划 + 实施规格；再用 [milestones/README](milestones/README.md) 看全局。

## 5. 工作怎么组织

- **里程碑**：M0.1 → M0.2 → M1 → M2 → M3 → M4 → M5；每个有 master 计划、实施规格、验收与完成定义。
- **任务**：master 任务表是状态唯一权威；开工时建 `M<N>/T-<n>-*.md` 记录判据与实施记录。
- **决策**：改设计基线写 [Agent Note](../.agents/notes/README.md)（Problem/Decision/Alternatives/Consequences），同批更新 DESIGN。
- **坑/事故**：坑进 [engineering/pitfalls](engineering/pitfalls/README.md)，事故进 [postmortem](postmortem/README.md)。
- **验证**：L1 构建+相关单测；L2 doc-gate+全量单测；L3 AOT/金标；见 [development](development.md)。

## 6. 接一个任务的标准流程

1. 读 master 任务表 → 对应实施规格 → 相关 Note/architecture。
2. `git status` 干净；确认写作用域（目录级）。
3. 先写/改测试，再实现；改行为必须改测试。
4. 跑 L1；改 `docs/` 跑 L2；里程碑收口跑 L3。
5. 一个任务一个 Conventional Commit（标题 ≤50 字，body ≤10 行），**不 push**。
6. 停下汇报：做了什么、怎么验的、剩余风险；等确认再下一个。

## 7. 找东西

| 想找 | 去哪 |
|---|---|
| 当前代码怎么跑 | [architecture.md](architecture.md) |
| 构建/运行/导出的具体命令 | [cookbook](cookbook/build.md) |
| 目标设计/约束 | [DESIGN.md](DESIGN.md) |
| 为什么做这个选择 | [.agents/notes/](../.agents/notes/README.md) |
| 当前里程碑/任务状态 | [milestones/README.md](milestones/README.md) |
| 未来阶段做什么 | 各 `milestones/M*-*.md` 与 `M*-实施规格.md` |
| 协议/回放/发现格式 | `docs/specs/`（M1/M2 产出） |
| 查表格式 | [TABLES.md](TABLES.md) |
| 旧项目资料 | [legacy/](legacy/README.md)（仅素材） |
| 已知缺口 | 当前里程碑「结果」+ 各计划「已知缺口」 |
| 工程坑 | [engineering/pitfalls](engineering/pitfalls/README.md) |

## 8. 约定

- **不 push**、一任务一提交、收尾汇报。
- 文档中文；代码/注释英文或中文均可，但前后一致。
- 不记录代码出处/旧版行号；引用本仓代码用符号/路径。
- 不手改 `content/tables`；不伪造绿灯。
- 有疑问先查 Note/rejected-alternatives，再问人；不要靠猜。

## 9. 如果卡住

1. Godot MCP 与本项目 C# MCP 是否可用？不可用按 [development](development.md) 的降级规则处理。
2. 看当前里程碑「风险」与「已知缺口」。
3. 决策冲突先写 Note，不要私改基线。
4. 仍然卡住：把「现象 + 已查文档 + 已跑命令」写清楚再问。
