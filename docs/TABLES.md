# 查表数据（content/tables）

> 状态：已迁移并解码确认（2026-10-28）
> 对应设计稿：§12 内容与资产
> 说明：这 6 个表是旧 Unity 项目的运行时数据，**不是**从当前上游仓库直接生成的；详见 §4。

---

## 1. 文件清单

| 文件 | 大小 | 条目数 | 用途 |
| --- | ---: | ---: | --- |
| `dictionary_normal.bytes` | 7,303,469 | 227,494 | 胡牌判定（万/筒/条 共用牌型） |
| `dictionary_wind.bytes` | 9,619 | 251 | 胡牌判定（风牌） |
| `dictionary_dragon.bytes` | 2,299 | 64 | 胡牌判定（箭牌） |
| `ai_dictionary_normal.bytes` | 8,017,905 | 286,550 | Bot 牌效（万/筒/条） |
| `ai_dictionary_wind.bytes` | 15,749 | 610 | Bot 牌效（风牌） |
| `ai_dictionary_dragon.bytes` | 3,139 | 125 | Bot 牌效（箭牌） |
| **合计** | **15,352,180** | — | — |

对应旧项目路径：`Assets/Resources/Algorithm/`。

---

## 2. 二进制格式

MessagePack（MessagePack-CSharp 生成代码写出），所有文件都是：

```
array(1)  ->  map<ulong, list<Row>>
```

- 顶层是一个长度为 1 的数组，第 0 项是 map；
- 每条记录的 value 是一个**行列表**（同一牌型可以有多种分解方式）。

### 2.1 key 编码

key = 该花色内每个 rank 的张数，按 rank 顺序、**十进制拼接**（与上游一致）：

```
key = (((count[0] * 10 + count[1]) * 10 + count[2]) ... )
```

- normal：9 位（1 万..9 万 / 1 筒..9 筒 / 1 条..9 条 共用）
- wind：4 位（东南西北）
- dragon：3 位（中发白）
- **前导 0 被省略**：`1210220` 表示 `[0,0,1,2,1,0,2,2,0]`。

> 这与旧项目 `DictionaryHandlerBase.RankAmountsToCombinationKey` 的 `cur * 10 + count` 完全一致。

### 2.2 胡牌表行（dictionary_*）

```
[ needGui:int, jiang:bool, hasWin:bool, hupai:int[] ]
```

| 字段 | 含义 |
| --- | --- |
| `needGui` | 该分解需要多少张鬼牌 |
| `jiang` | 该分解是否已含将（对子） |
| `hasWin` | 该分解在 `needGui` 张鬼牌下**直接成胡**；此时 `hupai` 为空 |
| `hupai` | 未成胡时，各 rank 的“补张”张数（数组长度 normal=9 / wind=4 / dragon=3）；全 1 表示任意一张该花色都可 |

例：key `1210220` → `[[2,true,false,[0,1,1,1,1,1,1,1,1]],[3,false,false,[0,1,1,1,1,1,1,1,1]],[3,true,true,[]],[4,false,true,[]]]`。这是 4 种分解：前两行是“未成胡、需补某张万子”，后两行是“直接成胡”。（2026-10-28 对 `.bytes` 实读；旧文档曾误写成单行 `[4,...]`。）

> 本表按全 rank 空间生成：当「中」被配置为鬼牌时，`dictionary_dragon` 的 rank1（中）计数恒为 0，64 个 key 中 48 个含中≠0 的 key 在该配置下不可达；鬼牌配置为 None 或其他牌时这些 key 可达。表数据不随鬼牌配置改变，配置见 `content/rules.json`。

### 2.3 牌效表行（ai_dictionary_*）

```
[ hasPair:bool, probability:double ]
```

- 同一牌型多种分解，每行一个 `[是否含将, 该分解的胡牌概率]`；
- 旧 `BotDecisionMaker.Calculate` 取“各分解概率之和的最大值”，并强制“有且只有一对将”。

### 2.4 金标 fixture（7 条）

M0.1 T-3 的断言基线（`src/Mahjong.Tests/fixtures/tables.json` 与之逐字节一致）：

| 表 | key | 期望行 |
|---|---|---|
| `dictionary_normal` | 1210220 | `[(2,true,false,[0,1,1,1,1,1,1,1,1]),(3,false,false,[0,1,1,1,1,1,1,1,1]),(3,true,true,[]),(4,false,true,[])]` |
| `dictionary_normal` | 101210221 | `[(3,true,false,[1,1,1,1,1,1,1,1,1]),(4,true,true,[])]` |
| `dictionary_wind` | 2 | `[(0,true,true,[]),(0,false,false,[0,0,0,1]),(1,false,true,[]),(2,true,false,[1,1,1,1]),(3,true,true,[]),(3,false,false,[1,1,1,1]),(4,false,true,[])]` |
| `dictionary_dragon` | 1 | `[(0,true,false,[0,0,1]),(1,true,true,[]),(1,false,false,[0,0,1]),(2,false,true,[]),(3,true,false,[1,1,1]),(4,true,true,[]),(4,false,false,[1,1,1])]` |
| `ai_dictionary_normal` | 0 | `[(true,0.006089478044739022),(false,1.0016042780748664)]` |
| `ai_dictionary_wind` | 0 | `[(true,0.014027149321266967),(false,1.0058823529411764)]` |
| `ai_dictionary_dragon` | 0 | `[(true,0.0196078431372549),(false,1.0088235294117647)]` |

（2026-10-28 对 `.bytes` 实读；改表或改 fixture 必须逐条对照，见 §4.2。）

---

## 3. 解码方法（可复现）

```bash
python -m pip install msgpack
python - <<'PY'
import msgpack
p='content/tables/dictionary_normal.bytes'
with open(p,'rb') as f:
    u=msgpack.Unpacker(f, raw=False, strict_map_key=False)
    root=u.unpack()
d=root[0]
print(len(d))
for k in list(d)[:3]:
    print(k, d[k][:2])
PY
```

旧项目里对应的 C# 类型（`Assets/Scripts/Gameplay/Algorithm/`）：

- `WinningCardsTable { Dictionary<ulong, List<WinningCardsInfo>> }`
- `WinningCardsInfo { int JokerNeededCount; bool HasPair; bool HasWin; int[] WinningCardRankAmounts; }`
- `HandCardsValueTable { Dictionary<ulong, List<HandCardsValueInfo>> }`
- `HandCardsValueInfo { bool HasPair; double Probability; }`

生成代码（MPC）在 `Assets/Scripts/Gameplay/Serialization/GeneratedResolver/`。

---

## 4. 与上游算法仓库的关系（重要）

上游仓库：https://github.com/gaodayihao/majiang_algorithm （README 指向 `esrrhs/majiang_algorithm`，MIT）。
仓库当前提供 `data/majiang_clien_*.txt`、`majiang_server_*.txt`、`majiang_ai_*.txt`，以及 Java/Go/C++ 三份实现。

### 4.1 文本格式与 key 编码是兼容的

```
key gui jiang hu [description...]
```

- `key` 编码与 §2.1 相同（十进制拼接）；
- `gui` = needGui，`jiang` = hasPair；
- `hu = -1` 表示“成胡”（对应 `hasWin = true`，`hupai = []`）；
- `hu` 为 9/4/3 位十进制数时，逐位即 `hupai`；
- `server` 变体额外带人类可读描述，`clien` 变体不带。

按此规则可以在上游数据与本仓库格式之间互转。

### 4.2 但本仓库的表**不是**用当前上游数据生成的

抽样对比 key `1210220`：

| 来源 | 行 |
| --- | --- |
| 本仓库 `dictionary_normal` | `[2,true,false,[0,1×8]]`、`[3,false,false,[0,1×8]]`、`[3,true,true,[]]`、`[4,false,true,[]]`（4 行） |
| 上游 `clien`（当前） | `5 1 111111111`、`2 1 11111111`、`4 0 -1` |
| 上游 `server`（当前） | 同上三行 + 描述 |

进一步核对历史版本：`843cac466f`（2017-12-19）、`728ed52df7`（2018-02-05）以及当前版本，该 key 的行**没有变化**。

结论：**本仓库的 6 个 .bytes 来自你当年那版 C# 实现/更早的算法语义，与当前上游数据不是同一套结果**。因此：

1. v1 **继续以这 6 个 .bytes 为唯一事实来源**（设计稿 D10），不能用上游 `data/*.txt` 直接替换——那会改变胡牌判定与 Bot 行为；
2. 上游仓库只作为**算法结构参考**（Java/Go/C++ 的 HuTable/AITable 实现、key 设计、AI 评分模型）；
3. 将来若要重新生成表，验收标准是**与现有 .bytes 语义完全一致**（全 key、全行逐条对比），而不是“与上游一致”。

---

## 5. 后续工具与计划（M0.1 / M4）

| 工具 | 时机 | 作用 |
| --- | --- | --- |
| `tools/table-inspect` | M0.1 | 把 .bytes 解码为 JSONL（含 key 的 rank 数组），固化 schema，可 diff、可人工检查 |
| `tools/table-diff` | M4 | 对比两套表（全 key/全行），输出差异报告；用于验证“重写的生成器是否复现旧表” |
| `tools/table-gen` | M4（可选） | 重写生成器（C# net10），以现有 .bytes 为金标准；通过 `table-diff` 后才允许替换 |

- 决策：**M0.1–M3 不动表**；M4 才评估“是否值得重写生成器”。
- 若你哪天找回当年的 C# 生成器仓库：先跑 `table-diff` 验证它能复现现有 .bytes，再决定是否采用。
- 任何表数据变更都必须提升 `contentHash` 与 `rulesVersion`（设计稿 §6.5）。

---

## 6. 版本与许可

- 数据来源：旧项目自研（C# 实现生成）；上游仓库为 MIT。
- 建议在 `content/manifest.json` 中记录这 6 个文件的 sha256；构建时校验。
- 若将来引入上游数据，必须在文档中标注具体 upstream commit（当前数据自 2018 起未变，仓库重构提交为 `3fd96a9ef3`，2026-10-02）。
