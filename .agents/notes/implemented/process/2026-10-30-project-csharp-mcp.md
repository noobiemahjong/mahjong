# Agent Note: 本项目 C# MCP 走 Mahjong.sln 的 VS 托管服务

Status: implemented — 本项目 C# 符号通道固定为 Visual Studio 打开 `Mahjong.sln` 后托管的 `MSVC MCP SDK Server`，DSH 注册名 `mahjong`。

## Problem

C# 符号/调用链/编译诊断要优先走 MCP，不能默认退回 `grep`/`read`。此前 DSH profile 只注册了其他项目（Ursa.slnx、Ursa-Client.sln）在 3001/3003 的 Roslyn MCP，mahjong 会话按「当前环境配置」探活时只能看到它们，容易把别的解决方案的符号当成本项目依据；本项目自己的 VS MCP（3011）既没有注册，也没有写进文档。

## Decision

1. DSH profile patch `~/.dsh/profiles/desktop/cordis.patch.yml` 的 insert 列表增加 `mcp-mahjong`：`serverName: mahjong`、`transport: streamable-http`、`url: http://localhost:3011/SDK/`。
2. 本项目 C# 通道只认 `Mahjong.sln` 承载的 server；其他项目的 Roslyn MCP 不用于本项目。
3. `docs/development.md` 写端点、DSH 注册名、新会话生效与探活命令；`docs/handbook.md` 环境表同步入口。
4. `.mcp/`（VS 扩展的本机端口/路径设置）与 `.vs/` 加入 `.gitignore`，不入库。

## Alternatives considered

- **继续用 Ursa 的 Roslyn MCP**：那是另一套解决方案，符号、工程结构与编译诊断都不对应本仓。
- **再包一层 stdio**：VS 扩展已经提供 streamable-http，多一层桥接只增加故障点。
- **把 `.mcp/mcpserver.settings.json` 入库**：端口与路径格式是本机状态，扩展会改写，不同机器会冲突。
- **从 DSH profile 删除 Ursa 条目**：会破坏其他项目的会话，不属于本项目范围。

## Consequences

- 新 DSH 会话出现 `mcp__mahjong__*` 工具：Roslyn 符号/定义/引用/调用链、工程与引用、编译诊断/构建/测试、调试器、编辑器与 VS 输出窗口。
- 依赖本机 Visual Studio 打开 `Mahjong.sln` 且扩展运行；否则按 [development](../../../../docs/development.md) 降级为 `grep`/`read` 并逐处说明理由。
- profile 改动重启 DSH 或开新会话才生效；换机器要在本地重新注册，仓库不承诺开箱即有 C# MCP。
