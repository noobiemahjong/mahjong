# aot-smoke (client AOT export and smoke test)

Windows release export plus NativeAOT standalone exe smoke test for the Godot client; covers the M0.1 T-6 smoke path and the M0.2 T-7 autoplay/startup baseline. ASCII only.

## Prerequisites

| Item | Check |
|---|---|
| .NET SDK | `dotnet --list-sdks` lists `10.0.401` (see [global.json](../../global.json)) |
| Windows SDK | `HKLM\SOFTWARE\Microsoft\Windows Kits\Installed Roots` has `KitsRoot10` with `Include\<ver>` |
| MSVC | `vswhere -latest -requires Microsoft.VisualStudio.Component.VC.Tools.x86.x64` prints a path |
| Godot | 4.7.2 mono console exe and `%APPDATA%\Godot\export_templates\4.7.2.stable.mono` |
| Project | `project.godot` without MCP `autoload` or `[editor_plugins]`, with `dotnet/project/solution_directory` |

A missing item prints `MISSING:` and exits 1; nothing is exported and no AOT pass is claimed.

## Usage

```powershell
pwsh -File tools/aot-smoke/aot-smoke.ps1
pwsh -File tools/aot-smoke/aot-smoke.ps1 -RepoRoot E:\learn\mahjong -GodotExe "<console exe>" -TimeoutSeconds 60
```

`-RepoRoot` defaults to two levels above the script, `-GodotExe` to this machine's mono console path, `-TimeoutSeconds` (per run) to 60. The script cleans `build/windows` itself.

## Artifacts

- `build/windows/Mahjong.exe`: exported exe, pck embedded
- `build/windows/data_<assembly>_windows_x86_64/`: NativeAOT libraries and pdb
- `build/windows/content/`: `rules.json`, `manifest.json`, `tables/`
- `build/aot-smoke.log`: full script log with every marker
- `build/aot-smoke-export.log`: Godot export output
- `build/aot-smoke-run.log`, `build/aot-smoke-autoplay.log`, `build/aot-smoke-gui.log`: engine logs

## Assertions

1. Export exits 0, its log has no `ERROR:` line, exactly one `data_*_windows_x86_64` exists.
2. The data directory has no `coreclr.dll`, `hostfxr.dll`, `GodotSharp.dll`.
3. No non-content exported file contains `_mcp_game_helper` (pck embedded in the exe).
4. `--smoke` x3: exit 0; stdout has `CONTENT OK <64 lowercase hex>` equal to the manifest `contentHash`, `TABLES OK ...` and `M0.1 SMOKE OK`; stderr empty or exactly the two-line noise below (case-sensitive whole text); prints `AOT STARTUP MS run=<n> value=<ms>` plus its median.
5. `--autoplay` x3, 60 s each: exit 0; same stderr rule; exactly one `AUTOPLAY READY` line and exactly one whole line `GAME OK rounds=8`; no `LOCAL READY` (autoplay skips the UI); launch to `AUTOPLAY READY` < 3000 ms and launch to `GAME OK` < 5000 ms per run; prints `AOT AUTOPLAY READY MS` / `AOT AUTOPLAY TOTAL MS run=<n> value=<ms>` and both medians.
6. GUI x3, wait 60 s: waits for `LOCAL READY`, records launch-to-marker, then kills the process (its exit code is not asserted); captured stdout has exactly one `LOCAL READY` line and no timeout; median < 3000 ms; prints `AOT GUI STARTUP MS run=<n> value=<ms>` plus its median.
7. `AOT AUTOPLAY OK` is printed right after the autoplay step; the last line is `AOT SMOKE OK`; exit 0 means every assertion passed.

## Engine noise

A Godot 4.7.2 export on this host prints exactly this two-line certificate-store error to stderr at startup; it is unrelated to content, AOT and the markers. Any other stderr byte fails the run:

```text
ERROR: Failed to read the root certificate store.
   at: get_system_ca_certificates (platform/windows/os_windows.cpp:2582)
```

## Exit codes

| Code | Meaning |
|---|---|
| 0 | preflight, export, artifacts, smoke x3, autoplay x3 and GUI x3 all passed |
| 1 | any assertion failed; the reason is in `build/aot-smoke.log` |

## Failure handling

- Missing prerequisite: fix the `MISSING:` item; do not skip the SDK or templates.
- Export failure: check `build/aot-smoke-export.log` and the msbuild issue csv under `%APPDATA%\Godot\mono\build_logs` (usual causes: `TreatWarningsAsErrors`, missing template, unresolved solution).
- Artifact or run failure: editor write-back of MCP entries into `project.godot` / `Startup.tscn`, a content copy or `contentHash` problem, or an engine/marker change; the log holds the last stdout and stderr.
