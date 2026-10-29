# Godot 4.7.2 NativeAOT 配方（Windows 实测）

来源：2026-09-21 的 **Windows AOT 真实跑过**记录（原始取证不在本仓；配方与坑已内联，可独立复现）。本仓已在本机复现：前置自检、导出、产物断言与冒烟判据见 [cookbook/aot-export](../cookbook/aot-export.md)。

Android：Godot 4.7.2 的 Android AOT 分支只在源码层面确认存在，没有本机 Android 实测（无 Android SDK/NDK），本机也未装 NDK；因此 M5 T-0 是**本机 Android 工具链实测**，不是重复验证 Windows 结论。

用途：客户端 AOT 冒烟（空客户端导出 + 独立 exe 运行）；客户端 AOT、服务器 JIT。

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
$repo = (Get-Location).Path   # 在仓库根执行；Godot CLI 相对路径基准不可靠，一律传绝对路径
Remove-Item -LiteralPath "$repo/build/windows" -Recurse -Force -ErrorAction SilentlyContinue
New-Item -ItemType Directory "$repo/build/windows" -Force | Out-Null   # 导出不会自建目标目录
& "D:\Program Files\Godot_v4.7.2-stable_mono_win64\Godot_v4.7.2-stable_mono_win64_console.exe" --headless --path "$repo/src/Mahjong.Client" --export-release "Windows Desktop" "$repo/build/windows/Mahjong.exe"
# 断言：导出数据目录无 coreclr.dll / hostfxr.dll / GodotSharp.dll
# 冒烟前把 content/ 复制到 "$repo/build/windows/content/"（见 cookbook/aot-export 第 2 节）
# --log-file 必须给：编辑器开着时不加会多出 user:// 日志写入错误，stderr 判据随之失效
& "$repo/build/windows/Mahjong.exe" --log-file "$repo/build/aot-smoke-run.log" --smoke
````

期望：stdout 含 `CONTENT OK <contentHash>`、`TABLES OK ...` 与 `M0.1 SMOKE OK`，退出 0；stderr 只允许本机 Godot 导出版固定打印的证书库错误两行，其它 stderr 行判失败（见 cookbook 的「引擎噪声」节）。导出数据目录名是 `data_<assembly_name>_windows_x86_64/`：按 `project.godot` 的 `dotnet/project/assembly_name` 命名，本机为 `data_Mahjong.Client_windows_x86_64/`，不按 exe 名。

## 已知坑

- 默认配置下失败常是 trimming 把特性构造参数裁掉，不是 AOT 不工作；补 root 即可。
- 编辑器 Debug 构建不验证裁剪；必须跑导出产物。
- 新增依赖/程序集后重跑 AOT 冒烟。
- 本机只验证 Windows；Linux/macOS 导出另验。
