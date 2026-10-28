# 表现层（视图 / UI / 音频 / 配置 / 序列化 / Editor）技术笔记

> 阅读范围：`Assets/Scripts/Gameplay/GameplayObjects/**`、`Gameplay/UI/**`、`Gameplay/GameState/**`、`Gameplay/Configuration/**`、`Gameplay/Serialization/**`、`Audio/**`、`Editor/**`（共约 100 个 .cs，约 8000 行）。
> 骨架源自 Unity 官方 Boss Room 示例，本项目的改动集中在「麻将牌桌视图」「技能面板」「胡牌提示」。
> 文中路径均相对仓库根目录，`文件:行号` 为当前版本行号。

---

## 1. 场景状态：GameStateBehaviour 与四个状态的 Client/Server 配对

`GameStateBehaviour`（`Assets/Scripts/Gameplay/GameState/GameStateBehaviour.cs:36`）继承 vContainer 的 `LifetimeScope`，
它同时承担两个职责：**每个场景一个 DI 作用域** + **“同一时刻只有一个状态对象存活”的全局约束**。

- 枚举 `GameState { MainMenu, LobbyGameStarter, IpGameStarter, GameRoom }`（`GameStateBehaviour.cs:6-12`）。
- 抽象成员 `ActiveState`（`:46`）与 `Persists`（`:41`）。`ClientMainMenuState` 返回 `Persists => true`（`ClientMainMenuState.cs:40`），因此它 `DontDestroyOnLoad` 常驻，其余状态都是场景级。
- 状态互斥逻辑在 `Start()`（`GameStateBehaviour.cs:63-94`）：静态字段 `s_ActiveStateGo` 保存“当前状态对象”；新状态到来时，若旧状态 `Persists && ActiveState == 自己`，则**销毁自己**（`:77-82`），否则**销毁旧状态**（`:86`）。主机模式下 client/server 两份状态对象并存，这段逻辑对两者都宽容（只比较类型与持久标记，见 `:73-74` 注释）。
- DI 注入时机：`Awake` 中调用 `Parent.Container.Inject(this)`（`:53-61`），即子作用域先建容器、再由父容器（`ApplicationController`，`Assets/Scripts/ApplicationLifecycle/ApplicationController.cs:19`）注入外部依赖。

**状态切换由 Netcode 场景加载隐式驱动**（`GameStateBehaviour.cs:22-25` 的注释明确说明）：
服务端 `NetworkManager.SceneManager.LoadScene` → 客户端加载场景 → 场景里的 `Client*/Server*` 状态对象 `Start()` 抢占“唯一状态”位置。
因此不存在独立的“状态机管理器”，**场景即状态**。

四个状态的具体实现：

| 状态 | Client 实现 | Server 实现 | 备注 |
| --- | --- | --- | --- |
| MainMenu | `ClientMainMenuState.cs:25` | 无（无网络连接） | 登录 Unity Services、生成昵称、跳转两个 GameStarter 场景（`:78-89`） |
| LobbyGameStarter | `ClientLobbyGameStarterState.cs:8` | 无 | 只 `RegisterComponent(m_LobbyGameStarterUIMediator)` |
| IpGameStarter | `ClientIpGameStarterState.cs:8` | 无 | 同上，注册 `IpGameStarterUIMediator` |
| GameRoom | `ClientGameRoomState.cs:15` | `ServerGameRoomState.cs:21` | 唯一成对实现；两者 `ActiveState` 都返回 `GameRoom` |

`ClientGameRoomState`（`ClientGameRoomState.cs:15`）是**客户端表现层的中枢**：
- `OnNetworkSpawn` 里创建 `ClientPlayer[4]` 并向 `PlayerRegistry` 注册工厂/存在性判断（`:47-57`）。
- `AddClientPlayer`（`:68-100`）是“座位对齐”的核心：找到 `IsOwner && !IsBot` 的本地玩家作为主视角，用 `MahjongUtils.GetRelativeSeatIndex`（`Assets/Scripts/Utils/MahjongUtils.cs:9-12`，公式 `(b - a + 4) % 4`）把绝对座位换算成**相对屏幕的 0/1/2/3**，再赋给 `PlayerView.PlayerViews[uiSeatIndex].HostPlayer`；四人齐全后才初始化庄家指示与 `GameView.InitIndicatorView()`（`:88-98`）。
- `RemoveClientPlayer`（`:102-115`）区分“开局前后”：开局后置灰离线头像，开局前直接清空 `HostPlayer`。

`ServerGameRoomState`（`ServerGameRoomState.cs:21`）是服务端权威入口：
- `OnNetworkSpawn`（`:90-108`）创建规则层实例 `new MahjongGame(m_Seats.Length, new NotificationProxy(m_NetworkGameState))`，并订阅 `NetworkManager.SceneManager.OnLoadEventCompleted / OnSynchronizeComplete`，用于“首批玩家”和“中途加入/重连”两种出生路径（`:135-150`）。
- `SpawnPlayer`（`:188-236`）：从 `PersistentPlayer` 复制昵称/头像/Gender；非迟到加入者分配空位；迟到者从 `SessionManager<SessionPlayerData>` 恢复 `SeatIndex` 并把 `ServerPlayer` 重新接到 `m_Game.Proxies[...].Connection`（`:221-231`）——**重连的关键一行**。
- `SpawnBotPlayer`（`:152-186`）随机头像、座位、`IsBot = true`、名字“电脑N”（`BotCounter`），随后 `bot.Spawn(true)`。
- 所有玩家 `State == Ready` 时自动 `StartGame()`（`:238-243`、`:264-272`、`:338-342`）。

---

## 2. 视图层分层

视图层是**纯 MonoBehaviour**（除少量入口 NetworkBehaviour），层级为：

```
GameRoom 场景
├─ GameRoomState(GameObject) : ServerGameRoomState + ClientGameRoomState + NetcodeHooks + NetworkGameState
├─ GameplayView : GameView                 (房间级总协调)
│   ├─ GameStateView 预制体 : JokerCardView / RoundView / DeckCardView
│   ├─ GameIndicator 预制体 : IndicatorView
│   ├─ MainPlayerArea : MainPlayerView     (PlayerViews[0])
│   │   ├─ HandCardArea / DrawCardArea : HandCardContainerView
│   │   ├─ PongKongArea : CompositeCardContainerView
│   │   ├─ DiscardDeck / ExposedHandCardArea / WinningCardArea : CardContainerView
│   │   └─ OnlinePlayerAvatarView
│   ├─ TopPlayerArea / LeftPlayerArea / RightPlayerArea : PlayerView
│   └─ RoomUI : RoomControllerUI / SkillPanel / WinningCardTips / 结算 UI
```

（结构由 `Assets/Scenes/GameRoom.unity` 与 `Assets/Prefabs/Character/*PlayerArea.prefab`、`Assets/Prefabs/GameStateView.prefab` 反推；`GameView.cs:19-26` 的序列化字段与 `PlayerViews` 数组顺序一致，`GameView.cs:39` 约定 `PlayerViews[0] == MainPlayerView`。）

职责划分：

- **GameView**（`GameplayObjects/GameView.cs:17`）：房间级“视图总控”。持有 `PlayerView[]`、`IndicatorView`、两张结算 UI 预制体、`WinningCardTipsView`。监听 `NetworkGameState.GameResponseState` 决定指示器呼吸（`:100-115`）；转发技能动画/开牌/发牌音效（`:156-174`）；结算弹窗与“继续”按钮→`SetReadyStateServerRpc` 的闭环（`:176-203`），以及 `GameEndResult` 队列化（`:205-216`，防止轮结束弹窗与整局结算弹窗叠在一起）。
- **PlayerView**（`GameplayObjects/Player/PlayerView.cs:16`）：一个座位的全套表现，实现 `IPlayerView`（`IPlayerView.cs:5-18`）。核心是把 `ClientPlayer`（网络）同步成一份 `GameRule.Players.Player SyncedPlayer`（`PlayerView.cs:55`、`:70`、`:74-105`）。
- **MainPlayerView**（`Player/MainPlayerView.cs:14`）：在主视角上追加“可交互/可决策”能力——手牌提交、响应状态冻结/解冻、技能面板、胡牌提示、房间准备 UI。
- **OnlinePlayerAvatarView**（`Player/OnlinePlayerAvatarView.cs:12`）：头像、昵称、分数、准备标记、Bot 标记、庄家标记、离线遮罩。纯 DOTween 表现层，不持有网络引用。
- **CardView**（`Card/CardView.cs:9`）：一张牌的通用视图，聚合 `CardSignAnimation`（弃牌箭头）、`HighlightableCard`（高亮）、`SelectableAndDropableCard`（选择/甩牌）；通过 `CardViewType.HandCard/OpenCard` 选择不同 Sprite 组（`:164-170`）。
- **CardContainerView**（`Card/CardContainerView.cs:9`）：牌容器基类，负责实例化 `CardPrefab`、维护 `Cards/CardViews` 双列表、按 `CardLayerOrdering` 排 `sortingOrder`（`:114-122`）；`AddCard` 时自动与 Joker 比对并显示金边（`:48-51`）。
- **HandCardContainerView**（`:9`）：手牌特化。额外挂一个 `m_DrawCardContainer` 表示“刚摸到的牌”，`DrawCard(card, index, holdInDrawCardView)` 决定进牌堆还是单独显示（`:42-57`），下次摸牌/出牌时 `MoveDrawCardToHandCard` 合并（`:93-101`）；选择互斥（单选）在 `:59-76`。
- **CompositeCardContainerView / CompositeCardView / PongCardView / KongCardView**（`Card/CompositeCard*.cs`）：副露（碰/杠）视图。容器按 `card.Skill is PongSkill / KongSkill` 分派预制体（`CompositeCardContainerView.cs:30-63`）；基类用相对座位旋转“指向出牌者”的箭头（`CompositeCardView.cs:60-65`）；`KongCardView` 对暗杠把前两张牌换成背面（`KongCardView.cs:24-28`）。
- **DeckCardView / JokerCardView / RoundView**（根目录）：纯文本/图片小控件，由 `GameStateView` 统一驱动。
- **IndicatorView**（`GameplayObjects/IndicatorView.cs:11`）：中央指示器。庄家背景按相对座位旋转 90°（`:56-65`）；倒计时用协程 `StartTimerCoroutine`（`:104-114`）；“该谁响应”用 DOTween 无限 Yoyo 呼吸（`:73-82`）。

**注入方式（VContainer）**：
1. 每个 `GameStateBehaviour` 子类在 `Configure` 中 `builder.RegisterComponent(...)` 注册本场景需要的组件，例如 `ClientMainMenuState.cs:58-63`（`NameGenerationData`、`MainMenuUI`）、`ServerGameRoomState.cs:56-60`（`NetworkGameState`）、`ClientLobbyGameStarterState.cs:14-18`（Mediator）。
2. 场景里还存在**不注册只注入**的组件（`UIQuitPanel`、`ConnectionStatusMessageUIManager`、`MainMenuUI`、`LocalAvatar` 等），通过 `LifetimeScope.autoInjectGameObjects`（VContainer 1.13）把 GameObject 挂到对应状态作用域上，例如 `GameRoom.unity:201-202`、`Startup.unity:987-988`、`IpGameStarter.unity:1276-1278`。
3. 网络预制体（`PersistentPlayer`、`ServerPlayer`/`ClientPlayer` 所在的玩家 prefab）**不能走 DI**（由 Netcode 运行时 Spawn 实例化），因此改用两条桥：`PlayerRegistry`（座位号→规则层 `Player` 模型，见 §4）和 `PersistentPlayerRuntimeCollection`（ScriptableObject 集合，见 §6）。

---

## 3. 交互：选中、甩牌、高亮、签名动画

- **SelectableAndDropableCard**（`Card/SelectableAndDropableCard.cs:9`）实现 uGUI 指针接口：
  - 选中＝在原位上移 0.5（`:47-66`），`IsActive=false` 时强制取消选中（`:27-38`），这就是“冻结/解冻”的实现基础；
  - 单击：未选中→选中+音效；已选中→提交（`:74-90`）；
  - **甩牌**：按下后按鼠标移动速度判定，超过 `m_DropSpeed = 3000`（`:11`）即 `Submit`（`:99-117`）。
- **CardView.Freeze()/Thaw()**（`Card/CardView.cs:25-41`）只是把 `m_Input.IsActive` 置否，同时 `m_IsFreeze` 抑制 `CardSelectChanged/CardSubmit` 事件（`:111-137`），保证“不能打的牌”点了没反应。
- **HighlightableCard**（`Card/HighlightableCard.cs:5`）：换 `SpriteRenderer.color` 到固定蓝灰（`0.524,0.609,0.858`）。`MainPlayerView.HandCardsOnCardSelectChanged`（`Player/MainPlayerView.cs:225-247`）在选中一张牌时，**遍历四家的弃牌与副露**，把同 `CardType` 的牌一起高亮——这是“我选这张牌，桌面上还剩几张”的即时信息设计。
- **CardSignAnimation**（`CardSignAnimation.cs:6`）：最近一张弃牌上的向下浮动标记，`DOLocalMoveY(...).SetLoops(-1, Yoyo)`（`:26`）；`GameView.PlayerViewOnPlayerDiscard`（`GameView.cs:53-60`）保证全局只有最新弃牌带签名。
- 动画技术栈：**DOTween**（头像分差飘字 `OnlinePlayerAvatarView.cs:72-78`、庄家标记抛物线/弹跳 `:164-198`、面板滑入 `UIFullScreenPanel.cs:30-45`、结算逐条淡入 `GameOverResultViewUI.cs:32-43`）；**协程**（指示器倒计时 `IndicatorView.cs:104-114`）；**UniTask**（`SkillPanelView.Show` 用 `DOFade(...)` 的 await 版 `SkillPanelView.cs:15-21`、`PlayerView.NotifyDiscardAsync` 的 `UniTask.Delay` `PlayerView.cs:404-409`、`AutoDestroyAnimation` 按动画时长自毁 `UI/Animation/AutoDestroyAnimation.cs:17-22`）。

---

## 4. 数据流：网络数据 → GameDataSource/PlayerRegistry → 视图（重点）

本项目有**三份并存的玩家/牌数据**，理解它们的转换就是理解表现层：

```
[服务端规则层] MahjongGame / Player(纯 C# 模型, INotifyPropertyChanged)
        │ PropertyChanged
        ▼
ServerGameRoomState.GameOnPropertyChanged   ServerGameRoomState.cs:290-321
        │ 逐字段搬运
        ▼
[网络层] NetworkGameState.NetworkVariable        GameState/NetworkGameState.cs:16-26
          ServerPlayer.NetworkVariable/NetworkList  Player/ServerPlayer.cs:26-50
          + NotificationProxy 的 ClientRpc（无状态事件）  GameRule/Games/NotificationProxy.cs:19-67
        │ 序列化只传 id / seatIndex（见 §10）
        ▼
[客户端] NetworkVariable.OnValueChanged / NetworkList.OnListChanged
        │
        ├─ GameStateView  ← 局/轮/牌堆/宝牌       GameplayObjects/GameStateView.cs:34-104
        ├─ GameView       ← 响应阶段/计时/结算     GameplayObjects/GameView.cs:72-142
        └─ PlayerView     ← 该座位的一切           Player/PlayerView.cs:153-175
                │ 同时镜像出一份 SyncedPlayer（GameRule.Player）
                ▼
        [Unity 视图] CardContainerView / OnlinePlayerAvatarView / IndicatorView …
```

1. **服务端→NetworkVariable（下沉）**：`ServerGameRoomState.GameOnPropertyChanged`（`ServerGameRoomState.cs:290-321`）把 `m_Game.State / DeckCardCount / JokerCardType / CurrentRound / TotalRounds / ResponseState` 逐个写入 `m_NetworkGameState` 对应 `NetworkVariable`；`MahjongGame.State` 变化还顺带 `SessionManager.StartSession/EndSession`（`:294-304`）。
2. **无状态事件走 Rpc**：计时器、技能动画、开牌、发牌、亮手牌、轮结算、整局结算这 7 类“一次性事件”不进 NetworkVariable，而是经 `NotificationProxy`（`GameRule/Games/NotificationProxy.cs`）调用 `NetworkGameState` 上的 `ClientRpc`（`NetworkGameState.cs:42-82`），客户端 Rpc 直接转调 `GameView` 的同名方法。**规则层只认识 Proxy 接口，不认识 Netcode**，这是规则/网络解耦的关键。
3. **“值”走 NetworkVariable，“事”走 Rpc**——这条分界线非常清晰，值得写进架构文档。
4. **客户端解析 id**：NetworkVariable 里存的是 `ushort/uint` id，客户端用 `GameDataSource.Instance.GetCard(cardId)`（`RuntimeDataContainers/GameDataSource.cs:102-110`）、`GetCardType`（`:112-120`）、`GetSkill`（`:140-148`）还原成对象；`ServerPlayer.HandCards` 就是 `NetworkList<ushort>`。
5. **PlayerRegistry 解决“模型无 NetworkObject”问题**：`Player` 是 `INotifyPropertyChanged` 的纯模型（`GameRule/Players/Player.cs:13`），持有 `SeatIndex`。因为不能用 Netcode 序列化，`CustomSerializationRegister`（`Serialization/CustomSerializationRegister.cs:46-59`）把 `Player` 序列化成 `seatIndex`，读回时通过 `PlayerRegistry.Instance.GetPlayer(seatIndex)` 反查；服务端注册工厂 `ServerGameRoomState.cs:104-105`（`m_Game.Players[i]`），客户端注册 `ClientGameRoomState.cs:55-56`（从 `PlayerViews[].SyncedPlayer` 里找）。这条“按座位号解引用”的注册表模式是本项目最值得注意的桥接设计。
6. **SyncedPlayer 影子模型**：`PlayerView` 在 `SetHostPlayer` 时创建 `new GameRule.Players.Player((ushort)seatIndex)`（`PlayerView.cs:55`），之后每个 `OnValueChanged/OnListChanged` 都同步写入这份影子（`:74-105`、`:177-316`）。好处是：**UI 层可以复用规则层的查询/校验能力而完全不依赖网络对象**——例如 `WinningCardTipsView` 用 `SyncedPlayer.HandCards` 调算法、`RoundOverListItemUI` 用 `SyncedPlayer.CompositeCards/WinningCard` 画结算牌面。
7. **GameDataSource 是“静态数据源”而非“网络数据源”**：它是 Startup 场景里的常驻单例（`Startup.unity` 中 `GameDataSource` 节点；`GameDataSource.cs:41-56` `DontDestroyOnLoad` + 唯一性检查），在 `Awake` 里由 `CardTypeDefinition[]` 构建 `CardTypeSet`（`:83-91`）、由每种牌 4 张构建 `CardSet`（`:67-81`）、由 `SuitTypeDefinition[]` 构建花色规则表（`:58-65`）、并 `m_SkillRegistry.InitMap()`（`:54`）。它还负责**牌面 Sprite 取图**（`:122-138`，按 `playerSeatIndex` 取对应朝向的图）和**剩余张数**（`GetAmountOfEachRankOfSuit` `+` `GameView.m_ExposedCardNumber` 计数，见 `GameView.cs:218-244`）。
8. **上行（视图→服务端）**：`MainPlayerView.HandCardsOnCardSubmit` → `OnCardOrSkillSubmit` 先用**当前响应状态自带的 Verifier** 在本地校验（`Player/MainPlayerView.cs:47-70`，构造 `VerifierParams`，见 `GameRule/Triggers/CardAndSkillUsageVerifier.cs:29-44`），通过后才发 `ServerPlayer.ResponseSkillAndCardUsageServerRpc`（`ServerPlayer.cs:106-110`）——本地预校验 + 服务端权威校验的双保险。
9. **本地“可行动提示”**：`ResponseState` 是 `NetworkVariable<PlayerResponseState>` 且 **read permission = Owner**（`ServerPlayer.cs:69`），只有本人知道“现在轮到你响应”；`MainPlayerView.UpdatePlayerViewResponseState`（`:124-160`）据此逐张 `Thaw/Freeze` 手牌并 `FilterSkillCommand` 出技能按钮。`HandCards` 同样是 Owner 权限（`:61`），其他玩家只能拿到 `HandCardCount` 画背面（`PlayerView.cs:291-316`）——**视野隔离靠 Netcode 的读权限完成，而不是 UI 层过滤**，这是很干净的一点。

---

## 5. 胡牌提示 UI

- **WinningCardTipsView**（`GameplayObjects/Algorithm/WinningCardTipsView.cs:11`）：
  - 空状态（轮到自己出牌前）在桌面侧边显示一个“提示”按钮，点击展开全部听牌（`:41-51`、`:130-161`）；`Show` 中若“所有牌种都听”则改用 `WinningAnyCardView` 并显示“任意牌”（`:139-143`），否则每张听牌牌面显示**剩余张数**，底部文案 `共听N种M张牌`（`:147-159`）。
  - 响应状态变化时：若 Verifier 判 `CanPlayCard`（`MainPlayerView.cs:122`），逐张手牌“试删再判胡”，命中则在该牌上 `ShowSign()`（`:193-228`），鼠标悬停/选中就弹出“打这张能听什么”（`:62-77`）。
  - 判定入口 `GetWinningCardTypes`（`:236-245`）**先试七对、再试普通型**，直接复用规则层 `WinningCardDetector`（`Algorithm/WinningCardDetector.cs`）。
  - 副作用：`MainPlayerViewOnPlayerDiscard`（`:106-109`）每次弃牌都重新计算，属于 O(n·m) 的枚举，牌局规模小可以接受。
- **WinningCardView**（`Algorithm/WinningCardView.cs:7`）：单张听牌条目，剩余 0 张时置灰（`:17-20`），宝牌加标记（`:24`）。

---

## 6. 房间与本地玩家

- **RoomController**（`GameplayObjects/RoomController.cs:9`）：开局前的准备 UI。`MainPlayer` 属性的 setter 里统一维护“加 Bot（仅房主可见）/准备/取消准备”三个按钮（`:19-41`），并订阅 `ServerPlayer.State`（`:80-88`）；房主权限用 `ClientPlayer.IsHostPlayer`（`ClientPlayer.cs:44`，`OwnerClientId == 0`）判定。加 Bot 的链路：`RoomController.OnAddBotButtonClick` → `ClientGameRoomState.AddBot`（`ClientGameRoomState.cs:132-135`）→ `NetworkGameState.AddBotServerRpc`（`NetworkGameState.cs:30-40`，**仅 clientId==0 才允许**）→ 服务端 `AddBot` 事件 → `SpawnBotPlayer`。
- **ClientPlayer / ServerPlayer**：同一玩家 prefab 上的两层壳。`ServerPlayer` 持有全部网络状态（`ServerPlayer.cs:26-50`），`ClientPlayer` 只在客户端启用（`ClientPlayer.cs:19-36`，`Awake` 先 `enabled=false`，`OnNetworkSpawn` 再打开并注册进 `ClientGameRoomState`）。`ClientPlayer.SeatIndex` 直接透传 `ServerPlayer.SeatIndex.Value`。
- **ServerPlayerDataRelay**（`Player/ServerPlayerDataRelay.cs:9`）：一个很“窄”的 NetworkBehaviour，唯一职责是 despawn 时把 `SeatIndex` 写回 `SessionManager<SessionPlayerData>`（`:21-32`），供重连恢复座位。
- **PersistentPlayer**（`GameplayObjects/PersistentPlayer.cs:11`）：跨场景的玩家身份（昵称/头像/Gender），spawn 时从 `SessionPlayerData` 载入（`:28-44`），despawn 时写回（`:57-71`）；同时自己注册到 `PersistentPlayerRuntimeCollection`（`:32`）。
- **PersistentPlayerRuntimeCollection**（`RuntimeDataContainers/PersistentPlayerRuntimeCollection.cs:7`）：`RuntimeCollection<PersistentPlayer>`（ScriptableObject，`Infrastructure/ScriptableObjectArchitecture/RuntimeCollection.cs:7`）的资产实例，带 `TryGetPlayer(clientId)`；被 `ClientMahjongLoadingScreen` 用来给加载进度条填玩家名（`UI/ClientMahjongLoadingScreen.cs:22-32`）——**ScriptableObject 充当运行时集合/查询表**是 Boss Room 遗留风格。
- **PlayerRegistry**（`Player/PlayerRegistry.cs:5`）：静态单例 + 两个 `Func` 委托（工厂、存在性判断），生命周期由 `ClientGameRoomState/ServerGameRoomState` 的 spawn/despawn 注册与 `UnregisterAll` 管理（`ClientGameRoomState.cs:55-62`、`ServerGameRoomState.cs:104-105,130`）。
- **NetworkAvatarGuidState**（`Player/NetworkAvatarGuidState.cs:11`）：`NetworkVariable<NetworkGuid>` + `AvatarRegistry` 查表，`RegisteredAvatar` 惰性解析（`:22-33`），Bot 用 `SetRandomAvatar()`（`:35-38`）。

---

## 7. UI 框架

- **PopupManager**（`UI/PopupManager.cs:7`）：静态 `ShowPopupPanel(title, mainText, closeableByUser)`（`:32-41`），内部维护 `PopupPanel` 池并**错位叠放**（`:54-84`，每多一个偏移 30px、上限 200px）。Canvas 被 `DontDestroyOnLoad`（`:24`），所以任何场景都能弹窗。`PopupPanel`（`:6`）用 `CanvasGroup.alpha/blocksRaycasts` 显隐，`closeableByUser=false` 时显示 loading spinner（`PopupPanel.cs:39-46`）——用于“重连中”这类强制提示。
- **UIFullScreenPanel**（`UI/UIFullScreenPanel.cs:10`）：全屏面板基类，点击空白即 `Hide()`（`:68-71`）；`Show/Hide` 用 `m_InAnimation` 防重入、用 `GetCancellationTokenOnDestroy()` 防对象销毁后 await 复活（`:25-28`、`:47-66`）。
- **UISettingMenuPanel**（`UI/UISettingMenuPanel.cs:11`）：设置/退出两个按钮的滑出菜单，同样用 UniTask + DOTween 的 `AwaitForComplete` 处理取消。`UISettingsPanel`（`:7`）只有两个 `SwitchButton`（音效/音乐开关）与版本号，最终落到 `ClientPrefs` + `AudioMixerConfigurator.Instance.Configure()`（`:42-52`）。
- **SwitchButton**（`UI/SwitchButton.cs:8`）：继承 `Selectable` 并实现 `ICanvasElement` 的自绘 Toggle 替代品，`IsOn` setter 走 `Set()`（`:85-99`），带 `SetIsOnWithoutNotify`；滑块位移/背景色由 `PlayEffect` 统一（`:35-75`），编辑器下 `OnValidate` 立即生效（`:132-140`）。
- **UITooltipDetector / UITooltipPopup**（`UI/UITooltipDetector.cs:6`、`UI/UITooltipPopup.cs:7`）：悬停延迟 0.5s 或点击即显示（`:51-65`）；Popup 用 `RectTransformUtility.ScreenPointToLocalPointInRectangle` 把屏幕坐标映射到 Canvas（`:46-55`）。`ClientMainMenuState` 用 `m_UGSSetupTooltipDetector.enabled` 开关“Unity Services 未配置”的提示（`ClientMainMenuState.cs:47,70,115`）。
- **GameStarter 三件套（Mediator 模式）**：
  - 基类 `GameStarterUIMediator`（`UI/GameStarter/GameStarterUIMediator.cs:10`）注入 `ISubscriber<ConnectStatus>` + `ConnectionManager` + `LocalLobbyUser`（`:19-26`），提供 `BlockUIWhileLoadingIsInProgress/Unblock`（`:41-56`）与输入净化 `Sanitize`（正则只留字母数字点，`:58-61`）。
  - `LobbyGameStarterUIMediator`（`:9`）：创建/按码加入/快速加入三条路径，成功后 `ConnectionManager.StartHostLobby/StartClientLobby`（`:28-110`）。
  - `IpGameStarterUIMediator`（`:6`）：**局域网模式的实现**，端口固定 9988、Host 绑 0.0.0.0（`:8-9`），`JoinWithIp` 先 `IPAddress.TryParse` 再连接（`:24-32`）；它重写 `OnConnectStatus` 为“任何状态都解锁 UI”（`:11-14`）。
  - 视图侧 `IpJoiningUI/LobbyJoiningUI` 只负责读控件值并转交 Mediator（`IpJoiningUI.cs:11-19`、`LobbyJoiningUI.cs:27-45`）。
- **结算 UI**：
  - 轮结束 `RoundOverResultViewUI`（`UI/TurnOver/RoundOverResultViewUI.cs:12`）：按 `winner == null / winner == 本地 / 其他` 切换“流局/自摸胡/他家和”标题图（`:54-65`），逐家 `RoundOverListItemUI` 展示副露、亮出的手牌、胡牌张、番种文本（`RoundOverListItemUI.cs:42-95`，番种用 `ScoreActionType.ExpandFlags()` 展开 + `GetDescription()`）。
  - 整局结算 `GameOverResultViewUI`（`UI/GameOver/GameOverResultViewUI.cs:9`）：正分/负分两种列表项原型（`:11-12`），按分数高低标“大赢家”（`:18-25`），`Start` 里 DOTween 逐条淡入（`:32-43`）。
  - 两者都由 `GameView` 在收到 ClientRpc 时 `Instantiate` 到 `m_RoomUIRoot`（`GameView.cs:176-216`），并 `m_PopupResultShowing` + `Queue<GameEndResult>` 保证“轮结算→整局结算”的先后顺序（`:182-202`、`:205-216`）。
- **网络异常提示**：`ConnectionStatusMessageUIManager`（`UI/ConnectionStatusMessageUIManager.cs:8`）把 `ConnectStatus` 全枚举映射成中文弹窗（`:32-68`），并把 `ReconnectMessage` 变成不可关闭的“正在重连 N/M”（`:70-93`）；`UnityServicesUIHandler`（`:10`）把 `UnityServiceErrorMessage` 按 Lobby/Auth 分类，对 `LobbyExceptionReason` 逐项给中文文案（`:51-84`）。这两个都是 **全局常驻（DontDestroyOnLoad）+ 消息总线订阅**，与游戏内 UI 完全解耦。
- **加载屏**：`ClientMahjongLoadingScreen`（`UI/ClientMahjongLoadingScreen.cs:6`）继承基础设施层的 `ClientLoadingScreen`，仅重写“进度条上写玩家名”（`:10-32`），数据来自 `PersistentPlayerRuntimeCollection`。

---

## 8. 音频

- **AudioMixerConfigurator**（`Audio/AudioMixerConfigurator.cs:5`）：常驻单例，`Start` 与设置变更时 `Configure()`（`:41-46`）把 0.0001~1 的线性音量转成 dB（`20*log10(v)`，`:48-55`），写入三个 Mixer 参数（Overall/Music/SFX）；`MinimumVolume = 0.0001`（`:21`）就是 -80dB 的静音。
- **ClientGameSoundEffectPlayer**（`Audio/ClientGameSoundEffectPlayer.cs:7`）：常驻单例 + 单 `AudioSource` 的 `PlayOneShot`。固定音效走序列化字段；**技能语音与牌语音走 Resources 动态路径**：`Sounds/{语言}/{性别}/Skills/{技能名}`、`Sounds/{语言}/{性别}/Mahjong/{cardTypeId}`（`:18-19`、`:45-55`），`Language` 目前只有 `Mandarin`（`:83-86`）。`PlayerView` 在弃牌时先播 `m_DiscardEffect`、延迟 `GameDelay.DiscardSoundEffectDelay = 0.15s` 再播牌语音（`PlayerView.cs:404-409`），技能则立即播语音并实例化 `SkillResource.AnimatorPrefab` 动画（`:397-402`）。
- **ClientMusicPlayer**（`Audio/ClientMusicPlayer.cs:6`）：常驻单例，`PlayTrack` 做了“同曲不重启”的判断（`:28-40`）。场景入口用两个极小脚本触发：`MainMenuMusicStarter`（`Audio/MainMenuMusicStarter.cs:7-10`）、`RoomMusicStarter`（`Audio/RoomMusicStarter.cs:7-10`，挂在 GameRoom 的 Main Camera 上）。
- **ButtonEffect**（`Audio/ButtonEffect.cs:6`）：按钮点击音效的自助组件，`IPointerDownHandler` + 可选自定义 clip（`:10-20`），是 UI 里最省事的音效接入方式。

---

## 9. 配置资源（ScriptableObject）

- **Avatar / AvatarRegistry**（`Gameplay/Configuration/Avatar.cs:9`、`AvatarRegistry.cs:7`）：`Avatar` 继承 `GuidScriptableObject`（`Infrastructure/ScriptableObjectArchitecture/GuidScriptableObject.cs:7`，`OnValidate` 自动生成 16 字节 Guid），只多一个 `Portrait`；Registry 提供 `TryGetAvatar(Guid)` / `GetRandomAvatar()`。网络上传的是 `NetworkGuid`，因此**资产的 Guid 必须稳定**——这也是为什么 Guid 写在资源本身而不是 registry 索引。
- **CardTypeDefinition**（`:7`）：`Id / SuitType / Rank / OpenedCardSprites[] / HandCardSprites[]`，两个 Sprite 数组按“四家朝向”排列，正好对上 `GameDataSource.GetHandCardSprite(card, index)` 的 `index`（即 `CardView.m_CardSpriteIndex`，`CardView.cs:15,164-170`）。
- **SuitTypeDefinition**（`:7`）：`RankCount / AmountOfEachRank / AllowedSequence`——把“顺子是否合法”“每种牌几张”数据化，`DictionaryHandlerBase` 与 `WinningCardDetector` 都从这里取常量（`DictionaryHandlerBase.cs:13-17`）。
- **NameGenerationData**（`:9`）：随机昵称词表，`MainMenuUI.RegenerateName()` 消费（`UI/MainMenuUI.cs:37-41`）。
- **SkillResource 家族**（`Configuration/SkillResource.cs:9` + `Skills/*.cs`）：抽象基类继承 `IdAutoGenerationScriptableObject`（`Infrastructure/.../IdAutoGenerationScriptableObject.cs:8`，编辑器下用 `GlobalObjectId` 的哈希自动产生 `uint Id`），提供 `Skill` 抽象属性与 `AnimatorPrefab`；四个子类 `KongSkillResource/PongSkillResource/SelfDrawnSkillResource/SelfDrawnWith7PairsSkillResource` 各自 `m_Skill ??= new XxxSkill(this)` **懒加载工厂**（如 `KongSkillResource.cs:11`），把 SO 资源当成技能对象的“配置 + 唯一标识”两者合一。`SkillRegistry.InitMap()` 把它们灌进 `Dictionary<uint, SkillResource>`（`GameRule/Skills/SkillRegistry.cs:17-24`），供 `GameDataSource.GetSkill/GetSkillResource` 反查。
- **GameDataSource 的编辑器校验**（`GameDataSource.cs:200-211`）：`OnValidate` 里**强制按数组下标重写每个 CardTypeDefinition.Id**，并断言 (Suit,Rank) 不重复——把“id 必须等于下标”从约定变成自动保证，这直接支撑了 `GetCardType(id)` 的 O(1) 数组索引。

---

## 10. 序列化（MessagePack + Netcode 自定义序列化）

两条**互不相干**的序列化链，容易混淆，必须分开写：

1. **MessagePack（跨机器传规则对象的快照）**
   - `MessagePackRegister`（`Serialization/MessagePackRegister.cs:11-26`）在 `[RuntimeInitializeOnLoadMethod(BeforeSceneLoad)]` 注册 `StaticCompositeResolver(GeneratedResolver, StandardResolver)` 并覆盖 `MessagePackSerializer.DefaultOptions`；编辑器下额外用 `[InitializeOnLoadMethod]`（`:31-35`），避免编辑器里首次序列化走偏。
   - `GeneratedResolver`（`Serialization/GeneratedResolver/MessagePack_Resolvers_GeneratedResolver.cs:16`）是 MPC 生成的查表：仅 8 个类型（`Dictionary<ulong,List<HandCardsValueInfo>>`、`List<...>`、`HandCardsValueInfo`、`HandCardsValueTable`、`WinningCardsInfo`、`WinningCardsTable`，见 `:50-60`），**只服务于算牌缓存表**，不是玩家数据。
   - 生成的 Formatter 是典型 MPC 手写风格：`Serialize` 写 `WriteArrayHeader(2)` + 两个字段，`Deserialize` 按 index `switch` + `Skip()` 容错（`...HandCardsValueInfoFormatter.cs:22-64`）。**注意**：这些文件由 MPC 生成（文件头 `// THIS (.cs) FILE IS GENERATED BY MPC`），改字段必须重新生成。
2. **Netcode 自定义序列化（运行时按 id 传值）**
   - `CustomSerializationRegister`（`Serialization/CustomSerializationRegister.cs:9`）是一个挂在 Startup 场景的 `MonoBehaviour`，`Awake` 里给 `UserNetworkVariableSerialization<Card>/<CardType>/<Skill>/<Player>` 装读写委托（`:11-59`）——这是 Netcode 1.x 对泛型 `NetworkVariable<T>`/`NetworkList<T>` 的官方扩展点。
   - `SerializationExtensions`（`Serialization/SerializationExtensions.cs:10`）是同一套逻辑的 `FastBufferWriter/Reader` 扩展方法版（供 Rpc 参数用）：`Card→ushort`、`CardType→ushort`、`Skill→uint`、`Player→seatIndex(ushort.MaxValue 表示 null)`、`CardAndSkillUsageVerifier→ShortName(string)`（`:60-69`）。**两处逻辑必须保持一致**，否则 `NetworkVariable` 与 Rpc 会解析出不同结果——这是本项目一个可改进点（应当抽公共方法）。
   - 关键语义：**网络上只传 id，接收端通过 `GameDataSource.Instance` / `PlayerRegistry.Instance` 反解**（如 `:15`、`:21`、`:32`、`:43`、`:50-52`）。这要求收发双方的数据表完全一致（同一套 CardTypeDefinition/SkillRegistry），否则会静默取到错对象或抛 `IndexOutOfRangeException`（`GameDataSource.cs:104-107`）。

---

## 11. Editor 工具

- **SceneBootstrapper**（`Editor/SceneBootstrapper.cs:27`）：`[InitializeOnLoad]` 监听 `playModeStateChanged`，保证从任意场景按 Play 都先进入 `EditorBuildSettings.scenes[0]`（即 Startup/Bootstrap）。
  - 细节很讲究：先 `SaveCurrentModifiedScenesIfUserWantsTo`（`:122`），若当前不是 Bootstrap 场景，则 **`EditorApplication.isPlaying = false` → `OpenScene(Bootstrap)` → `isPlaying = true`**（`:135-144`）强制重进播放模式。注释（`:12-26`）解释了原因：NetworkObject 的 `GlobalObjectIdHash` 依赖 `OnValidate`，需要 AssetDatabase 先建立缓存，否则 NetworkManager 的网络预制体列表会失效——这是 Netcode 项目的经典坑。
  - 另外缓存 `PreviousScene`（`:47`）以便退出播放后回到原场景（`:152-158`），并用 `LoveMeMahjong/Load Bootstrap Scene On Play` 菜单开关（`:34-36`、`:69-91`）与测试场景（`InitTestScene`，`:38`、`:161-164`）兼容。
- **BitmapFontExporter**（`Editor/BitmapFontExporter.cs:8`）：`ScriptableWizard`，读 BMFont 的 `.fnt(xml)` + 图集，逐字符构造 `CharacterInfo`（注意 UV 的 V 轴翻转 `:92`、顶点 Y 取负 `:105-106`），生成 `Material(Unlit/Transparent)` + `Font` 两个资产（`:72-82`）。用于中文牌面/标题的艺术字体，属于“把美术给的点阵字体转成 Unity 可用的旧式 Font”。

---

## 12. 设计与可改进点（供架构文档引用）

**设计亮点**
1. **“场景即状态”+ LifetimeScope 状态对象**：状态互斥、DI 作用域、场景加载三者合为一体，没有额外状态机；`Persists` 用一个布尔解决“主菜单常驻”这个特例（`GameStateBehaviour.cs:41,63-94`）。
2. **值/事分离的同步策略**：持续存在的状态（局、轮、牌堆、响应阶段、每家手牌）用 `NetworkVariable/NetworkList`，一次性事件（计时、动画、结算）用 ClientRpc，且规则层只依赖 `NotificationProxy` 接口（`NotificationProxy.cs:10`），Netcode 完全下沉。
3. **三模型分层（Player / ServerPlayer / SyncedPlayer）**：规则模型不继承 MonoBehaviour、可直接序列化/单测；网络模型只管同步；视图模型给 UI 提供本地查询。`PlayerRegistry` + 自定义序列化完成了“座位号 ↔ 模型”的解引用。这是全项目最值得写进架构文档的模式。
4. **数据驱动 + 编辑器自动校验**：CardTypeDefinition/SuitTypeDefinition/SkillResource 全部 SO 化，`GameDataSource.OnValidate` 强制 id==下标并查重（`GameDataSource.cs:200-211`），`IdAutoGenerationScriptableObject` 自动生成稳定 id。
5. **权限即视野**：手牌与响应状态用 `NetworkVariableReadPermission.Owner`（`ServerPlayer.cs:61,69`），客户端天然看不到别人手牌，UI 不需要任何过滤逻辑。
6. **纯表现层组件高度可复用**：`CardContainerView` 家族用模板方法（`AddCard/RemoveCard/SortCardViews`）支撑手牌/弃牌/副露/结算四个场景，新增牌区几乎零成本。

**可改进点**
1. `CustomSerializationRegister` 与 `SerializationExtensions` 是两份重复实现（`CustomSerializationRegister.cs:13-59` vs `SerializationExtensions.cs:12-69`），应抽成单一 `CardNetworkSerializer` 静态类；否则新增可序列化类型时极易漏改。
2. `GameDataSource`/`PlayerRegistry`/`ClientGameSoundEffectPlayer`/`ClientMusicPlayer`/`AudioMixerConfigurator`/`PopupManager` 共 6 个静态单例入口，与 vContainer 体系并存，测试时难以替换；建议至少给 `GameDataSource` 定义接口并从容器解析。
3. `PlayerView` 411 行，把“同步 + 视图 + 音效 + 动画”揉在一起；`DiscardCardsOnOnListChanged` 等 6 个 List 回调可下沉为一个 `NetworkListBinder<T>`。
4. `ClientGameRoomState` 用 `m_GameView.PlayerViews[uiSeatIndex].HostPlayer` 做座位对齐，视图与状态对象直接双向依赖（`ClientGameRoomState.cs:83`、`:107`、`:119`），若将来支持观战/换座，建议引入 `SeatAlignment` 服务。
5. `WinningCardTipsView` 的听牌预计算在每次弃牌与每次选择时全量重算（`WinningCardTipsView.cs:106-109,193-228`），可在 `ResponseState` 变化时批量缓存；且 `m_CacheWinningCardTypes` 只在 `Empty` 状态清空（`:119`），存在跨轮残留风险。
6. `SelectableAndDropableCard` 直接读 `Input.mousePosition`（`:95,105`）而 `PointerEventData` 参数未被使用，Input System 迁移时会需要改。
7. `RoundView.SetRound` 用 `totalRound - current + 1` 反算“第几局”（`RoundView.cs:20-23`），语义反直觉，建议直接同步“当前是第几局”。
8. `PlayerState`/`Gender`/`MahjongGameState` 等枚举以 `byte` 传输，新增值时 `ConnectionStatusMessageUIManager`/`GameOverResultViewUI` 这类 `switch` 缺少 default 分支保护（前者有 `:64-66` 兜底，后者 `GameOverResultViewUI.cs:16-30` 没有）。
