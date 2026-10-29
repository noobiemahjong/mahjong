# Reference（可复现证据）

本目录只放**技术证据与外部资料勘察**：数据格式、AOT、性能、外部实现结构。不放决策与过程纪要。

规则：数字/枚举/路径必须给复现命令或实读落点；**否定结论**必须建立在跨编码扫描或字节实读之上；外部资料只是证据，不是权威。

## 索引

| 主题 | 文档/路径 | 说明 |
|---|---|---|
| 查表格式与来源 | [../TABLES.md](../TABLES.md) | 6 个 .bytes 的 MessagePack 结构、key 编码、与上游差异、解码命令 |
| 查表二进制 | `content/tables/` | v1 唯一事实来源；sha256 待写入 manifest |
| GameEventEngine | [game-event-engine.md](game-event-engine.md) | tick/Action 树原型：可借鉴与不要照搬的清单 |
| Godot AOT 取证 | [godot-aot.md](godot-aot.md) | 4.7.2 NativeAOT 配方、导出命令、裁剪坑 |
| SGS 参考实现 | `E:/learn/sgs/` | TCP + protobuf + 录播/观战/重连的历史实现；只作结构参考 |
| 上游算法仓库 | `https://github.com/gaodayihao/majiang_algorithm` | MIT；当前 data 与我们的表语义不同（见 TABLES §4） |
| 旧项目资料 | [../legacy/](../legacy/README.md) | 历史资料/素材参考，非权威 |

## 写作要求

- 每条证据给：**来源路径/URL + 复现命令 + 结论**；命令必须能在本机直接跑。
- 引用外部仓用绝对路径（反引号包住）或 URL；引用本仓代码用符号锚点（不写行号）。
- 决策写 Note，坑写 pitfalls，事故写 postmortem；证据文档不承担决策职责。
- 不记录代码出处/逆向地址（IDA、旧版行号）；这类信息不进本仓。
