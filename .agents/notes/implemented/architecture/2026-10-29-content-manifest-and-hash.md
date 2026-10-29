# Agent Note: 内容清单与 contentHash

Status: implemented — 清单即真相，contentHash 是内容版本三元组的唯一内容指纹。

## Problem

内容目录有 4 个 JSON 与 6 个二进制查表，启动与联机握手都要判断「两端是否同一份内容」。按目录扫描或按 mtime 判断无法表达「未列出文件不计入」，也没法把 rules.json（鬼牌配置）纳入指纹；规格 §1.1 的清单样例漏列 rules.json，实现若照抄会漏算鬼牌规则。

## Decision

- 清单固定 10 个路径：tiles.json、suits.json、skills.json、rules.json 与 6 个 tables/*.bytes；manifest.json 自身不入清单。
- 每个文件取 SHA-256 小写 hex；contentHash = SHA-256("v1\n" + 按 path ordinal 升序的每行 "path=hash\n"（含末行换行）的 UTF-8 字节) 的 32 字节摘要；manifest 存 64 位小写 hex，ContentCatalog 同时暴露原始 32B（ContentHash）与 hex（ContentHashHex）。
- 清单即真相：manifest.files 必须恰好是上述 10 条；逐条与磁盘字节比对；重算 contentHash 必须与声明一致；未列出文件不计入（content/ 下其它文件不改变指纹，manifest.json 本身即例）。
- rules.json 进入清单：改鬼牌规则必变 contentHash；rulesVersion 独立于 contentHash（v1 常量 1）。
- JSON 一律 source-generated JsonSerializerContext，未知字段与缺失字段直接报错，避免 schema 漂移被静默忽略。
- rules.json 的 joker.mode=FlipIndicator 在 v1 加载即抛 NotSupportedException，不猜翻牌。
- 加载失败抛 ContentLoadException，携带文件绝对路径；基类为 Exception（net10.0 的 System.IO.InvalidDataException 是 sealed，无法派生；规格未规定基类）。

## Alternatives considered

- 扫描目录排序后拼接：会把 manifest.json 等非内容文件卷进指纹，新增文件改变指纹，与「未列出文件不计入」冲突。
- 只哈希 4 个 JSON：6 张表直接决定胡牌/牌效行为，改表不变指纹会让回放与联机误判同内容。
- 直接哈希 manifest.json 字节：manifest 是载体，排版变化不应改变内容身份。
- 换成 MessagePack/文本摘要：无收益，且协议与回放已按 32B 原始摘要留位。

## Consequences

- 任何内容改动都要同批重算 manifest（含 rules.json），否则启动失败；改清单算法必须改版本前缀 v1 并写新 Note。
- 协议/回放传 32B 原始摘要，日志与显示用 hex，不把 hex 当协议值。
- 给内容 schema 加字段要同步 DTO 与源生成上下文；多余字段被视为格式错误，向前兼容靠版本号而非忽略字段。
- rulesVersion 与 contentHash 分离：只有规则语义兼容性变化才动 rulesVersion。
