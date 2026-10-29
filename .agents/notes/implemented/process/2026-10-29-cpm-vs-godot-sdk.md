# Agent Note: CPM 只管 4 个 .NET 工程，Client 关闭集中包管理

Status: implemented — T-1 的包版本策略按此落地。

## Problem

T-1 要求 `Directory.Packages.props`（CPM）集中锁版本，但 `Godot.NET.Sdk` 会注入带 `Version=` 的隐式 `PackageReference`（GodotSharp、Godot.SourceGenerators、Debug 下的 GodotSharpEditor）。CPM 打开时这些带版本的引用会触发 NU1008，Client 工程无法还原。文档没写这条，实现者会在第一次 restore 时卡住。

## Decision

1. `Directory.Packages.props` 打开 CPM，只覆盖 `Mahjong.Core` / `Mahjong.Session` / `Mahjong.Server` / `Mahjong.Tests`。
2. `Mahjong.Client` 设 `<ManagePackageVersionsCentrally>false</ManagePackageVersionsCentrally>`，GodotSharp 等版本由 `Godot.NET.Sdk` 自带。
3. 测试包版本锁定为 `Microsoft.NET.Test.Sdk 17.14.1` / `xunit 2.9.3` / `xunit.runner.visualstudio 3.1.4`（与本机 NuGet 缓存一致，可离线还原）。
4. `EnableDynamicLoading` 不作为初始配置，只在导出实测需要时再加。

## Alternatives considered

- **全 CPM + `DisableImplicitGodotSharpReferences=true`**：版本最集中，但要在 Client 手写 GodotSharp / Godot.SourceGenerators / Debug 的 GodotSharpEditor 三条引用并自己跟 SDK 版本，升级时容易漏。
- **完全放弃 CPM**：改动最小，但 4 个 .NET 工程的测试包版本会散在各自 csproj，T-1 的「集中锁定」验收要改写。
- **把 Godot 工程拆出解决方案**：CI 与 `dotnet build Mahjong.sln` 的验收就覆盖不到 Client，得不偿失。

## Consequences

- 升级 .NET 侧依赖改 `Directory.Packages.props`，升级 Godot 侧依赖改 `Godot.NET.Sdk/4.7.2` 版本；两处分开升级。
- Client 工程新增普通 NuGet 包时要在 csproj 里写版本（不受 CPM 管）。
- AOT 的 `TrimmerRootAssembly` 清单不受影响。
