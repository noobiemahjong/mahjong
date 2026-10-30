# 开发工作流

本文件是**操作层**：命令、验证、MCP、团队与提交。规矩判据在根 [AGENTS.md](../AGENTS.md)，文档标准在 [docs/AGENTS.md](AGENTS.md)。

## 命令

命令的期望输出、退出码与失败处置见 [cookbook/build](cookbook/build.md)；运行客户端见 [cookbook/run](cookbook/run.md)，AOT 导出与冒烟见 [cookbook/aot-export](cookbook/aot-export.md)。

```sh
dotnet build Mahjong.sln              # L1 构建
dotnet test  Mahjong.sln              # L1 单测
pwsh -File tools/doc-gate/doc-gate.ps1 -Fail   # L2 文档门禁
pwsh -File tools/doc-gate/tests/run.ps1        # L2 门禁夹具自检
pwsh -File tools/git-hooks/tests/run.ps1       # L2 commit-msg 钩子测试
git config core.hooksPath tools/git-hooks      # 每克隆安装一次
pwsh -File tools/aot-smoke/aot-smoke.ps1       # L3 客户端 AOT 冒烟（里程碑收口）
```

改动涉及哪个面，就跑哪个面的检查；**不默认全量**。CI 负责全量矩阵与平台交叉验证。

## 验证分层

| 层 | 内容 | 何时跑 |
|---|---|---|
| L1 | 构建 + 相关单测（规则/流程/协议） | 每次提交前 |
| L2 | doc-gate + 构建 + 全量单测 | 改 `docs/` 或提交前；CI 每次 push/PR |
| L3 | 客户端 AOT 导出 + 独立 exe 冒烟（`tools/aot-smoke`）+ 金标回放；M5 起加 Android 冒烟（`build-android` + adb install/logcat） | 每个里程碑收口 |

规则：

- 验收匹配改动面：改规则跑规则单测，改协议跑协议/回放测试，改文档跑 doc-gate；不要用「跑了全量」代替针对性证据。
- 汇报只写**真正跑过**的命令与结果；没跑就写未跑。
- CI 红 = P1，不带着红提交。

## MCP

**开工第一步 = 探活**；禁止「不探活就直接 grep」作为默认路径。

| 通道 | 范围 | 强度 | 不通时 |
|---|---|---|---|
| Godot MCP（当前环境提供的 Godot 编辑器 MCP） | 场景/节点/UI/资源/运行态 | **硬门禁** | 停下，要求用户启动 Godot 编辑器与对应 MCP server，不降级 |
| C# 符号 MCP：Visual Studio 打开 `Mahjong.sln` 后托管的 `MSVC MCP SDK Server`，端点 `http://localhost:3011/SDK/` | Roslyn 符号/定义/引用/调用链、工程与引用、编译诊断、构建/测试、调试器、VS 输出窗口 | 必须优先 | 先确认 VS 打开 `Mahjong.sln` 且扩展运行；仍不可用可回退 `grep`/`read`，汇报里逐处说明理由 |
| 仓库内数据 | 查表、回放、内容 | — | 用 `tools/table-inspect` 或 python/msgpack 实读 |

- **本项目 C# 符号通道只认 `Mahjong.sln` 承载的 server**：其他项目的 Roslyn MCP（如 Ursa 的 3001/3003）属于别的解决方案，不作为本项目依据；开工先探活本项目通道，不可用时按上表降级并说明。
- DSH 注册项为 `mcp-mahjong`（`~/.dsh/profiles/desktop/cordis.patch.yml`，`transport: streamable-http`，`url: http://localhost:3011/SDK/`）；改 profile 后需重启 DSH 或开新会话生效，工具前缀为 `mcp__mahjong__`。
- 端口由 VS 扩展的本机设置 `.mcp/mcpserver.settings.json` 决定（默认 3011）；`.mcp/` 与 `.vs/` 是本机状态，不入库。
- 探活（VS 已打开 `Mahjong.sln`）：

```sh
curl -s -X POST http://localhost:3011/SDK/ -H "Content-Type: application/json" \
  -d '{"jsonrpc":"2.0","id":1,"method":"initialize","params":{"protocolVersion":"2024-11-05","capabilities":{},"clientInfo":{"name":"probe","version":"0"}}}'
# 期望 result.serverInfo.name = MSVC MCP SDK Server
```

- 外部仓库（旧 Unity、`E:/learn` 下的参考仓）与 `docs/**` 不在 C# 索引内，用文件读取/搜索是正解，不算回退。
- MCP 快照是「取用那一刻」的视图；跨轮复用旧读数前重新取。
- MCP 写操作（改场景、改资产、导入）必须属于已确认任务；顺手改 = 违规。

## 团队与委派

- 开工前在共享任务板建任务，写清**写作用域（目录级）**；并行任务作用域不重叠。
- 子任务书必须含：MCP 条款、载体（改哪些目录）、交付物、验收命令、停止条件。
- **一单元一产物**：默认单单元 ≤80 步 / ≤150K token；批次 ≤8；同时活跃 ≤5。
- Lead 复核最终 diff、跑 L1/L2、确认无越界；**不 push**。
- 做完一个单元停下汇报，等确认再开下一个。

## 提交与收尾

- 一个任务一个 Conventional Commit（`feat` / `fix` / `docs` / `refactor` / `chore` / `test`）；标题 ≤50 字。
- body ≤10 个非空行：改了什么 / 为什么 / 验收口径（只写跑过的命令）。
- **不 push**；提交留在本地。
- 本地 `build` / `test` / `doc-gate` 全绿即满足里程碑收口判据；CI 首绿由用户 push 后回填，回填前记「CI 待远程确认」。
- 收尾汇报：做了什么、怎么验的、剩余风险；等确认再做下一件。

## 需要先征得同意

- 停服/重启服务、结束用户进程。
- 修改场景/资产/外部仓库。
- 执行 MCP 写操作、批量重命名、删除文件。
- 任何可能不可逆的操作。
