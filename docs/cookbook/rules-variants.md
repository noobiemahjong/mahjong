# 规则变体开关（content/rules.json）

> 用途：从 `variants` 六键查到行为与判据。键表、锁定块与校验规则的唯一家是 [T-1 §3](../milestones/M0.3/T-1-规则表与数值定稿.md#3-contentrulesjson-schema)，加载链与快照见 [core.md §1](../subsystems/core.md#1-内容加载)，位值与掩码见 [scoring.md §2](../subsystems/scoring.md#2-番种与杠位位集)，金额与叠加见 [T-1 §2](../milestones/M0.3/T-1-规则表与数值定稿.md#2-支付与叠加口径)。命令都在仓库根执行。

## 1. 读出当前开关

```pwsh
pwsh -NoProfile -Command "(Get-Content content/rules.json -Raw | ConvertFrom-Json).variants | ConvertTo-Json -Compress"
```

输出（六键即全部开关，顺序按文件）：

```text
{"dealerStreakBonus":[0,1,2,3],"followingBanker":1,"horseBonus":1,"robbingKong":true,"kongFlowerContract":true,"twelveCardsContract":true}
```

## 2. 确认缺省与拒载口径

```pwsh
dotnet test Mahjong.sln --filter FullyQualifiedName~ContentCatalogValidationTests
```

输出末行 `已通过! - 失败: 0，通过: 54`，退出码 0。该组用临时内容目录分别构造「缺 `variants` 段」「缺单键」「未知字段」「数值越界」「显式空数组」：缺省与缺键读作关闭（数值 0、布尔 false、连庄表为空表），显式空数组与越界值拒载并报字段路径。

## 3. 开关到行为

| 开关 | 打开时行为 | 关闭时行为 | 判据 |
|---|---|---|---|
| `dealerStreakBonus` | 庄家胡按 `b = 表[min(streak, len) - 1]` 收三家，每家具 `b` | 缺键或该项为 0：不付、不置 `DealerStreak` | `R1_DealerStreak_PaysThreeSharesAndSkipsZero`、`S1` |
| `followingBanker` | 非庄胡赢家 `+amount`、庄家 `-amount`，每局一次 | 0：不付、不置 `FollowingTheBanker`，庄家胡同样不付 | `R2_FollowingTheBanker_PaysOneOnlyForNonBankerWins`、`S2` |
| `horseBonus` | 结算第 4 步从墙头翻马，命中赢家时三家各付 | 0：不翻马、无 `HorseRevealed`、不置 `HorseWinner`；墙空整步跳过 | `M09_HorseBonusZero_PaysNothingAndSetsNoBit`、`M04_NonBankerSelfDrawn_WallEmpty_NoHorsePaymentNorBit` |
| `robbingKong` | 补杠第 4 张前开抢杠窗口，候选按座位升序应答 | false：不开窗，补杠直接成立（杠分、补牌、`Meld` 照常） | `R4_RobbingKong_ChargesTheRobbedSeatThreeShares`、符号 `GameSession` 的 `_robbingKong` 门 |
| `kongFlowerContract` | 明杠补牌自摸由放杠者包三家 | false：按常规自摸三家付，不置 `KongUpFlowerPayForEveryone` | `R5_KongUpFlowerContract_ChargesOnlyTheExposedKongPayer`、符号 `GameSession` 的 `_kongFlowerContract` 门 |
| `twelveCardsContract` | 四明副露自摸由第 4 副露供牌者包三家 | false：按常规自摸三家付，不置 `TwelveCardsConceded` | `R6_TwelveCardsContract_ChargesTheFourthMeldSupplier`、符号 `GameSession` 的 `_twelveCardsContract` 门 |

关闭侧的金额 0 不记账、不置位是账本口径，见 [scoring.md §2](../subsystems/scoring.md#2-番种与杠位位集)。

## 4. 单变体与组合矩阵

```pwsh
dotnet test Mahjong.sln --filter FullyQualifiedName~RuleVariantMatrixTests
```

输出末行 `已通过! - 失败: 0，通过: 29`，退出码 0。29 例 = R1–R7 七个单变体 + 21 对组合 + 流局行；组合结论的唯一家是 [T-1 §5](../milestones/M0.3/T-1-规则表与数值定稿.md#5-规则变体交互矩阵)。逐对看点：`1×2` 与 `4×5` 互斥，其余 19 对相加，`4×6`/`5×6` 由优先级收敛到唯一承包者。

## 5. 手算分例 S-1…S-8

```pwsh
dotnet test Mahjong.sln --filter FullyQualifiedName~ContractHandCalcTests
```

输出末行 `已通过! - 失败: 0，通过: 10`，退出码 0。10 例 = 七变体前提 1 例 + S-1…S-8 + 无鬼抢杠基分 1 例；每例比较四座精确 delta、完整位集与四座和 0，用例表见 [T-5 §1](../milestones/M0.3/T-5-回归与收口.md#1-单测矩阵)。

## 6. 中马分例 M-01…M-10

```pwsh
dotnet test Mahjong.sln --filter FullyQualifiedName~HorseScoringHandCalcTests
```

输出末行 `已通过! - 失败: 0，通过: 10`，退出码 0。覆盖马中/不中/墙空/关闭四个分支与 `HorseWinner=512` 的位分布；映射与金额见 [T-4 §2](../milestones/M0.3/T-4-中马.md#2-映射与计分)。

## 7. 会话侧：抢杠窗口与马事件

```pwsh
dotnet test Mahjong.sln --filter FullyQualifiedName~RobbingKongFlowTests
```

输出末行 `已通过! - 失败: 0，通过: 17`，退出码 0。覆盖窗口参数、候选升序、`Cancelled`/`TimedOut`、补杠取消后无 `Meld`/杠分/`Drew`、事件序 `RequestClosed < HorseRevealed < RoundEnded`；窗口口径见 [T-3 §1](../milestones/M0.3/T-3-抢杠与承包.md#1-抢杠窗口)，事件与载荷见 [T-4 §3](../milestones/M0.3/T-4-中马.md#3-事件与载荷)。

## 8. 改开关后的收口

```pwsh
pwsh -File tools/doc-gate/doc-gate.ps1 -Fail
```

输出末行 `doc-gate: OK`，退出码 0。改 `variants` 会改 `contentHash`：`content/manifest.json` 与三处落点的口径见 [T-1 §3](../milestones/M0.3/T-1-规则表与数值定稿.md#3-contentrulesjson-schema)，字段与校验见 [TABLES §7](../TABLES.md)。
