# Godot 4.7.2 NativeAOT 配方（Windows 实测）

来源：2026-09-21 的 **Windows AOT 真实跑过**记录（原始取证不在本仓；配方与坑已内联，可独立复现）。本仓尚未在本机复现；T-6 步骤 0 自检 MSVC/Windows SDK，缺则停。

Android：Godot 4.7.2 的 Android AOT 分支只在源码层面确认存在，没有本机 Android 实测（当时无 Android SDK/NDK）；本机现在也没装 NDK。因此 M5 T-0 是**本机 Android 工具链实测**，不是重复验证 Windows 结论。
用途：M0.1 T-6 的 AOT 冒烟。客户端 AOT、服务器 JIT。

## 配置

`Mahjong.Client.csproj`：

````xml
<PropertyGroup>
  <PublishAot>true</PublishAot>
</PropertyGroup>
<ItemGroup>
  <TrimmerRootAssembly Include="Mahjong.Client" />
  <TrimmerRootAssembly Include="Mahjong.Session" />
  <TrimmerRootAssembly Include="Mahjong.Core" />
  <TrimmerRootAssembly Include="GodotSharp" />
</ItemGroup>
````

- Godot 4.7.2 导出预设**没有** AOT 开关，只能写 csproj；编辑器运行仍是 Debug/JIT。
- 按名字/反射加载的程序集必须 root；漏 root GodotSharp 时 `ScriptPathAttribute` 构造参数会被裁空，运行期报 `native_name == StringName()`。
- 客户端禁反射序列化；内容 JSON 用 source-gen；查表用手写解析（见 T-3）。

## 导出与断言

````powershell
Remove-Item build/windows -Recurse -Force -ErrorAction Ignore
& "D:\Program Files\Godot_v4.7.2-stable_mono_win64\Godot_v4.7.2-stable_mono_win64_console.exe" --headless --path src/Mahjong.Client --export-release "Windows Desktop" build/windows/Mahjong.exe
# 断言：导出数据目录无 coreclr.dll / hostfxr.dll / GodotSharp.dll
& build/windows/Mahjong.exe --smoke
````

期望：stdout 含 `CONTENT OK <contentHash>` 与 `M0.1 SMOKE OK`，退出 0，stderr 空；导出数据目录名为 `data_Mahjong_windows_x86_64/`。Windows 路径有实测先例；本机仍以 T-6 步骤 0 自检为准。

## 已知坑

- 默认配置下失败常是 trimming 把特性构造参数裁掉，不是 AOT 不工作；补 root 即可。
- 编辑器 Debug 构建不验证裁剪；必须跑导出产物。
- 新增依赖/程序集后重跑 AOT 冒烟。
- 本机只验证 Windows；Linux/macOS 导出另验。
