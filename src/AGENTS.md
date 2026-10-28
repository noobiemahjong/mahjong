# src/AGENTS.md（代码子树规则）

适用 `src/**`。全仓常驻规矩见根 [AGENTS.md](../AGENTS.md)；代码怎么放、依赖怎么连、注释写什么，在这里。

## 工程边界

| 工程 | 职责 | 目标 |
|---|---|---|
| `Mahjong.Core` | 引擎无关内核：规则/Flow/协议/内容/查表接口 | `net10.0`，**不引用 Godot** |
| `Mahjong.Session` | 权威会话：座位、流产出、请求/超时/托管、回放录制 | `net10.0` |
| `Mahjong.Client` | Godot 宿主：视图/输入/音频/Mirror/回放播放器；单机/房主内嵌 Session | `net10.0` + `PublishAot` |
| `Mahjong.Server` | 无头宿主：房间、传输、发现、HTTP | `net10.0`，普通 JIT 发布 |
| `Mahjong.Tests` | xUnit：规则/流程/回放/断线/多房间隔离 | `net10.0` |

- 依赖方向：Client → Session → Core；Server → Session → Core；Core 不反向依赖任何宿主。
- 规则/流程/协议不得引用 `Godot`、`UnityEngine`、真实时钟、网络 API、静态全局状态。
- 新增工程/程序集必须同步「AOT 与序列化」的 root 清单，并在 [docs/architecture.md](../docs/architecture.md)（实现后）登记。

## 确定性与并发

- 单房间单逻辑线程；房间之间零共享可变状态；禁止 thread-keyed 静态表。
- 时间只用 tick；规则层不读墙钟、不 sleep、不 await 真实时间。
- 随机只在服务端；结果通过事件流下发，客户端不持有 RNG。
- 每个 tick 是一个事务边界；异常只用于违反不变式，流程中断用返回值。

## AOT 与序列化

- 客户端 NativeAOT：禁止 `Reflection.Emit`、`Assembly.Load`、动态类型生成、反射式序列化。
- 网络包用**手写 codec**（显式 switch + 长度前缀）；不引入 protobuf-net。
- 内容 JSON 用 source-generated `JsonSerializerContext`；查表若继续 MessagePack，必须静态注册生成 Formatter，禁用动态 resolver。
- 被按名字/反射加载的程序集必须加入 `TrimmerRootAssembly`；新增依赖后重跑 AOT 冒烟。

## 注释

- 只写：作用/参数语义/返回值与失败行为/前置条件/副作用/读代码看不出的「为什么」。
- 不写：旧版对应、代码出处、IDA 地址、本仓行号、执行历史、日期、测试走查。
- 契约写在声明处；理由链接到 `.agents/notes/`，不在注释里复述。

## 测试

- 规则/流程纯 C# 单测；同输入序列两遍状态哈希一致。
- 协议/回放有金标用例；断线/重连/观战用注入测试。
- 改行为必须同时补/改测试；没测试的验收不算完成。
