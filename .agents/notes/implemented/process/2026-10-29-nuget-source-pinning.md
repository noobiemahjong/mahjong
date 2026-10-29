# Agent Note: 仓库锁定 NuGet 源，解开 CPM 与多源冲突

Status: implemented — T-1 的 restore 源按此固定为 nuget.org。

## Problem

`Directory.Packages.props` 打开 CPM 后，用户级 NuGet 配置里的多个包源（nuget.org、私有源、VS 离线源）触发 NU1507；`TreatWarningsAsErrors` 把它升级为 error，5 个工程全部无法构建。源配置不在仓库里，换机器就复现，T-1 的验收命令不可复现。

## Decision

1. 仓库根加 `NuGet.config`：`<packageSources>` 先 `<clear/>`，只保留 nuget.org（`protocolVersion="3"`）。
2. 不在命令行加 `-p:RestoreSources=` 之类的覆盖，不改用户级/机器级配置（影响本仓之外的仓库）。
3. 本仓需要私有源时改这个文件并同批写新 Note；本 Note 与那篇互不并流。

## Alternatives considered

- **验收命令统一带 `-p:RestoreSources=`**：不用加文件，但每条构建/测试/CI 命令都要带参数，漏一次就红，且掩盖「仓库自己不可复现」。
- **`<packageSourceMapping>` 只映射本仓用到的包**：不动全局源，但每加一个包都要维护模式；当前只有 3 个测试包与 Godot.NET.Sdk，维护成本大于收益。
- **改用户级 NuGet.Config**：改动在仓库外，其他机器与其他仓库不受控。

## Consequences

- 本仓 restore 只从 nuget.org 取包；全局缓存已有包时可离线还原。
- CI 无需额外 restore 参数；新增私有依赖必须改 `NuGet.config` 并写 Note。
