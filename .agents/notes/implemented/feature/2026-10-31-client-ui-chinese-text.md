# Agent Note: 客户端 UI 中文文案与字体回退

Status: implemented — 用户直接指令中文 UI；中文标签配字体回退，M4 负责终版 Theme/字体。

## Problem

M0.2 T-6 客户端占位 UI 采用 ASCII 文案（`YOU/NEXT/ACROSS/PREV`、`1M..9M`、`PONG`、英文状态行），依据是「Godot 默认字体缺 CJK 字形」的假设与「不新增字体/美术资源」的范围约束。用户实测后明确要求中文 UI：ASCII 牌面与状态行难以辨认、无法用于验收测试，且该中文化本就出自用户要求。同时默认字体是否含 CJK 未经运行验证，只改字符串可能出现豆腐块；契约禁止新增字体/美术资源，M4 才负责 Theme 与终版字体。

## Decision

1. UI 文案改中文：相对座位用「你/下家/对家/上家」，牌用「1万..9万、1筒..9筒、1条..9条、东风/南风/西风/北风、红中/发财/白板」，副露用「碰/明杠/暗杠/补杠」，状态行与结算层中文，按钮用「出牌/碰/杠/胡/过」与「继续/重开/返回」；Info 与 StatusLine 同样中文，具体文案以 `View/Table.cs`、`View/TableViewProjection.cs`、`LocalGame.cs` 为准。
2. 字体：默认字体实测可渲染中文但 `ThemeDB.fallback_font.has_char('你')=false`（依赖引擎系统回退、跨机器不保证）；`LocalGame._Ready` 首行 `ApplyUiFont()` 建 `SystemFont`（FontNames 微软雅黑 UI/微软雅黑/黑体/Noto Sans CJK SC，`AllowSystemFallback = true`），设 `Theme.DefaultFont`/`DefaultFontSize = 16` 级联全部控件；不新增 `.ttf`/`.tres` 字体资源。
3. 中文 UI 属 M0.2 T-6 交付；M4 只承接 Theme、美术与终版字体。
4. 用户直接指令优先于冻结契约文字：D-T6-7「UI 必须 ASCII」作废，D-T6-9 记录本裁定。
5. 证据必须证明中文可读：`.reviews/m02t6/evidence/impl-zh/` 的中文截图与 `font-fallback.md` 的 `has_char` 断言，不能只改字符串。

## Alternatives considered

- **保持 ASCII（D-T6-7）**：默认字体无缺字风险、符合原范围；但用户明确反馈看不清、无法测试，占位 UI 失去验收价值。放弃。
- **内置 CJK 字体资源（.ttf/.otf）**：可读性最稳；违反契约 §2「不新增字体/美术资源」，增加导入、包体与 AOT 负担，且 M4 还要换终版字体。放弃。
- **只换字符串、不做字体回退**：实现最省；默认字体缺字时全屏豆腐块，问题未解决。放弃。
- **硬编码某个平台字体名**：简单；跨平台缺失时静默退化，不可复现、不可移植。放弃，改用 `SystemFont`/`Theme` 回退并留运行验证。
- **把 Theme/美术一并做进 T-6**：更接近成品；超出 T-6 范围且 M4 有专门排期。放弃，本轮只改文案与字体回退。

## Consequences

- 中文文案表与 [T-6 规格](../../../../docs/milestones/M0.2/T-6-Godot最小闭环.md)、[client.md](../../../../docs/subsystems/client.md)、[cookbook/run](../../../../docs/cookbook/run.md) 同步；ASCII 相关判据（D-T6-7）由中文可读性与字体回退判据取代。
- 变异台账新增中文/字体条目：删字体回退导致豆腐块、中文文案表错位或牌 Label 错配。
- 验收证据须含中文界面截图与字体回退运行检查；M4 承接 Theme/美术/终版字体。
- 旧决策 [godot-minimal-loop](2026-10-31-godot-minimal-loop.md) 的 D9 已改为中文占位 + 字体回退，并交叉链接本 Note。
