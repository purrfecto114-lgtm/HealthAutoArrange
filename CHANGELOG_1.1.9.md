# HealthAutoArrange 1.1.9

- 重新评估目标游戏版本：从 Casualties: Unknown Demo **v6.1** 改为 **v7.0.1**（Steam playtest 当前 demo，buildId 24774057，2026-08 更新）。
- 引用程序集改为来自 NuGet 包 `CasualtiesUnknown.GameLibs.Steam 7.0.1-ngd.0`（由 NuGet-GameLib-Dehumidifier 项目公开化后发布的 BepInEx 风格 reference assemblies），替代此前从 Paili-16/Scav-Prototype-System-file-changed- 仓库取得的 v6.1 `Assembly-CSharp.dll`。
- 反编译 v7.0.1 `Assembly-CSharp.dll` 并核对所有 Harmony patch 目标与字段访问，结论：**mod 使用的 API 表面在 v7.0.1 中完整保留**，无需修改任何源码：
  - `MoodleManager.UpdateMoodles()`、`MoodleManager.AddAllMoodles()`（private，Harmony 仍可后置）、`MoodleManager.AddMoodle(int,string,string,string,bool,bool)`（默认参数不变）签名均一致。
  - `MoodleManager.moodles`、`.main`、`.icons`、`.sideMoodles`、`ClearMoodles()`、`UpdatePrevMoodles()` 全部保留为 public。
  - `Moodle.type` / `.isSide` / `.doWarningFlash` / `.flash` / `.unTransparentTime` 仍为 public 字段（v7.0.1 新增的 `Moodle.img2` 私有字段不影响本 mod）。
  - `UIUtil.IsPointerOverUIElement()`（无参重载）仍为 public static，原样可用；v7.0.1 新增的 `IsPointerOverContextMenu()` 不冲突。
  - `PlayerCamera.main`、`.body`、`DoAlert(string, bool)` 签名一致。
- 由于 API 未变化，1.1.8 的全部 8 项修复（F8/F9/Ctrl+R/Ctrl+E 热键解耦、Enabled 即时持久化、DrawStatusBadge、单状态排序阈值 `< 1`、GamePatches 诊断标志、DumpPatchDiagnostics 等）在 v7.0.1 下行为一致。
- 重建 release artifacts：`HealthAutoArrange.Plugin.dll`（net472，BepInEx 5.4.23.5 兼容）与 `HealthAutoArrange.Core.dll`（netstandard2.0）现以 v7.0.1 公开化 `Assembly-CSharp.dll` 为引用程序集编译，0 warning / 0 error。
- 插件与程序集版本提升至 1.1.9 / 1.1.9.0。

## 兼容性

- 1.1.9 客户端在 v7.0.1 demo 上工作正常（编译期 ABI 与运行期 Harmony patch 均对齐）。
- 1.1.9 在 v6.1 demo 上仍可工作（API 表面是 v7.0.1 的真子集），但官方支持目标改为 v7.0.1。
- 配置文件向前兼容；1.1.8 的所有用户设置不丢失。

## 验证

- `python3 tools/static_smoke.py`：103 项契约全部通过。
- `python3 tools/version_check.py`：Plugin 1.1.9 / Assembly 1.1.9.0 / File 1.1.9.0 一致。
- `dotnet test HealthAutoArrange.Tests`：213 个 xUnit 测试全部通过。
- `dotnet build HealthAutoArrange.Plugin (net472)`：0 Warning, 0 Error（针对 v7.0.1 公开化 `Assembly-CSharp.dll`）。
- ilspycmd 反编译 1.1.9 Plugin.dll，确认新增方法（`DrawStatusBadge`、`SyncEnabledFromRuntime`、`DumpPatchDiagnostics` 等）在场。
