# HealthAutoArrange

一个可以把 Casualties: Unknown（游戏）里状态图标（Moodles）按自定义配置自动整理，并可在本地显示可配置的状态提醒的客户端mod。只改变客户端显示顺序，不修改游戏逻辑。

---  
重要提醒：本 Mod 为非官方第三方项目，使用风险由用户自行承担。

## 快速上手（最短路径）
1. 从本仓库 Releases 下载最新的发布包（Release）。  
   你只需要两个 DLL：`HealthAutoArrange.Plugin.dll` 和 `HealthAutoArrange.Core.dll`。
2. 将这两个文件复制到你的游戏目录下的 BepInEx 插件文件夹，例如：
   - <game root>/BepInEx/plugins/HealthAutoArrange/
3. 启动游戏（或重启）。启动成功时，BepInEx 日志会包含 `HealthAutoArrange.Plugin loaded.`。

## 在游戏中如何使用
- F8：打开 / 关闭设置窗口（内置 IMGUI）。在窗口里你可以：
  - 开启/关闭“自动整理状态图标”
  - 查看“状态目录”（仅显示本次游戏观察到的状态），把状态拖入分组或调整顺序
  - 配置或预览提醒（reminders）
  - 切换界面语言（右上角 EN / 中文）
- F9：将当前 Moodle 诊断输出到 BepInEx/LogOutput.log（用于报告问题时的诊断信息）
- 配置会写入规则文件：`BepInEx/config/com.healthautoarrange.plugin.rules.cfg`（保存前请确认“保存并应用”）

默认行为要点：
- 未识别的新状态默认“Keep”（保留原位），不会被自动移到末尾，避免误分类。
- 插件只调整 UI 顺序，不改变数值、状态触发条件或网络同步。

## 常见问题（FAQ）
Q: 插件放好了但没有生效？  
A: 检查 `BepInEx/LogOutput.log`（或 BepInEx 日志）是否有 `HealthAutoArrange.Plugin loaded.`。如无，确认 DLL 是否放在正确路径（BepInEx/plugins/HealthAutoArrange/），并确认你使用的是 BepInEx 5.x（本工程按 BepInEx 5 编译；社区对 6.x 有兼容案例但不保证）。

Q: 想恢复默认设置怎么办？  
A: 点击IMGUI中的恢复默认设置或者关闭游戏，删除 `BepInEx/config/com.healthautoarrange.plugin.rules.cfg`，下次启动将从默认模板重建配置（你在 F8 中的本次修改会被重置）。

Q: 如何报告 bug？需要提供哪些信息？  
A: 请在 Issues 里提供：
- 游戏版本（Steam/ Demo 版本号）；
- BepInEx 与插件放置路径截图或说明；
- BepInEx/LogOutput.log 里与 HealthAutoArrange 相关的日志片段（F9 生成的诊断很有帮助）；
- 简要复现步骤与期望结果。

## 简短说明：界面与设置
- 主界面保留四件事：自动整理开关、未识别状态策略、分组优先级（上下移动）、状态目录（只显示实际观察到的状态）。
- “高级设置”默认折叠：手动编辑 ID、立即重排、输出诊断、提醒高级选项等放在此处，普通玩家通常不需要动它们。

---

## 开发 / 高级用户（放在文件末尾）
如果你想从源码构建或运行测试：

- 先准备 .NET SDK。本仓库的 Core 项目目标为 `netstandard2.0`。
- 构建插件（示例）：
  ```powershell
  dotnet build HealthAutoArrange.Plugin/HealthAutoArrange.Plugin.csproj -c Release
  dotnet test  HealthAutoArrange.Tests/HealthAutoArrange.Tests.csproj
