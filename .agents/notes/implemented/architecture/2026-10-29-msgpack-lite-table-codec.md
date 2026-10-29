# Agent Note: 查表用 MessagePack 子集手写解码

Status: implemented — 6 个 .bytes 只由手写、严格拒绝超集类型码的读取器解码，不引第三方序列化库。

## Problem

6 张查表合计 15.2MB，是胡牌与牌效判定的唯一事实来源；客户端目标 NativeAOT。引入 MessagePack-CSharp 会带来动态 resolver/反射 Formatter 与裁剪风险；通用解码器还会静默接受被换成别的编码的数据，把错误推迟到查询期。

## Decision

- Mahjong.Core.Content.Tables.MsgPackLite 只接受 positive fixint、uint8/16/32/64、fixarray/array16/32、fixmap/map16/32、bool、float64；其余类型码（str/bin/nil/负 fixint/int8/16/32/64/float32/ext）一律 InvalidDataException，消息带文件路径与偏移。
- 按头部声明长度推进，不预分配最坏长度；头声明数超过剩余字节立即失败；文档必须恰好消费完，尾随字节报错。
- TableCatalog.Load 一次读入 6 个固定文件名，校验结构（顶层 array(1)->map、行字段数、winningRanks 长度按花色 9/4/3、hasWin 与空数组一致、重复 key、空行列表、补张数不超过 4、AI 概率有限），插入 Dictionary<ulong, WinRow[]>；查询只做字典查找，零分配。
- TryGetWinRows/TryGetAiRows 对「花色无表或 key 无行」统一返回 false 且 out 为 Array.Empty，调用方只关心有无候选。
- 表 key 当作不透明 ulong：前导 0 让 rank 位数不可从数值反推，合法性由 TableKey.Encode 与内容侧保证，加载不对 key 位数做校验。

## Alternatives considered

- MessagePack-CSharp 加静态注册 Formatter：依赖大、AOT/裁剪要额外配置，与本仓「手写 codec」的协议取向不一致；只为 6 个固定文件引第三方不划算。
- 自定紧凑二进制格式重生成表：6 个 .bytes 是 v1 唯一事实来源且禁止手改，重生成是 M4 才评估的风险项（见 TABLES §4）。
- 宽松解析再在查询期校验：错误延迟、可能被当成「无候选」静默吞掉，不可接受。

## Consequences

- 新增表字段或编码必须同时改 MsgPackLite、TableCatalog 与表格式文档；不存在「宽松解码」兜底。
- 冷加载约 0.7s 量级（15.2MB，Windows 本机单次实测）；测试用共享 fixture，进程内只付一次。
- 比规格更严的结构校验（空行列表、补张数超过 4、非有限概率）对真实 6 表全部成立；若将来出现合法但越界的数据，先改数据格式，不放宽解码。
- WinRow.WinningRanks 暴露的是 catalog 内部缓存数组（零分配要求），调用方不得修改。
