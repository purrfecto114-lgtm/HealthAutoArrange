# HealthAutoArrange

一个可以把 Casualties: Unknown（游戏）里的状态图标（Moodles）按自定义规则自动整理，并可在本地显示可配置状态提醒的 Mod。它不修改生命值、状态触发条件或网络同步数据；但会修改客户端 Moodle UI 排列、绘制本地提醒，并在 F8 设置窗口打开时让游戏把指针视为位于 UI 上以阻止误攻击/交互。

---
重要提醒：本 Mod 为非官方第三方项目，使用风险由用户自行承担。

兼容性说明：MoodleManager 与 Unity UI 层级是多个 C:U Mod 可能共同补丁的热点。当前源码会把游戏刷新后的排序严格推迟到后续 Unity frame，并在检测到刷新重入/层级变化时中止旧排序计划；Sibling 模式只在父节点可证明为“单行、纯活动 Moodle”时写层级，混合/不稳定父节点会保守跳过本次层级重排。仍建议反馈崩溃时同时提供游戏版本、BepInEx 版本、完整 Mod 列表、`LogOutput.log`，若是进程级退出再附 `Player.log`/崩溃转储。

## 快速上手（最短路径）
1. 从本仓库 Releases 下载最新版发布包（Release）。
   你只需要两个 DLL：`HealthAutoArrange.Plugin.dll` 和 `HealthAutoArrange.Core.dll`。
2. 将这两个文件复制到游戏目录的 BepInEx 插件文件夹，例如：
   `<game root>/BepInEx/plugins/HealthAutoArrange/`
3. 启动游戏（或重启）。启动成功时，BepInEx 日志会包含 `HealthAutoArrange.Plugin loaded.`。

## 在游戏中如何使用
- F8：打开 / 关闭设置窗口（内置 IMGUI）。在窗口里你可以：
  - 开启/关闭“自动整理状态图标”
  - 查看“状态目录”（仅显示本次游戏观察到的状态），将状态加入分组或调整顺序
  - 配置或预览提醒（reminders）
  - 切换界面语言（右上角 EN / 中文）
- F9：将当前 Moodle 诊断输出到 `BepInEx/LogOutput.log`（用于反馈问题时的诊断信息）
- 配置会写入规则文件：`BepInEx/config/com.healthautoarrange.plugin.rules.cfg`（点击“保存并应用”会写入）

默认行为要点：
- 未识别的新状态默认为 “Keep”（保留原位），不会被自动移到末尾，以避免误分类。
- 自动整理只改变 Moodle UI 的客户端排列；提醒 overlay 也仅在本地绘制。
- F8 设置窗口打开时，插件会把 `UIUtil.IsPointerOverUIElement()` 的最终结果提升为 `true`，用于阻止拖动设置窗口时触发游戏攻击/交互；窗口关闭后保留游戏原判断。
- 插件不写入生命/伤势等游戏数值，不改变状态触发条件，也不修改网络同步数据。

## 常见问题（FAQ）
Q: 插件放好了但没有生效？
A: 检查 `BepInEx/LogOutput.log`（或 BepInEx 日志）中是否出现 `HealthAutoArrange.Plugin loaded.`。如无，请确认：
- DLL 是否放在 `BepInEx/plugins/HealthAutoArrange/`；
- 你使用的 BepInEx 版本（本工程以 BepInEx 5.x 编译；对 6.x 有社区兼容案例但不保证）。

Q: 想撤销 GUI 中未保存的修改？
A: 在 F8 设置窗口中点击“放弃修改并重载”（Reload），会从磁盘的 rules 文件读取并恢复界面设置（不修改磁盘文件）。

Q: 想恢复到默认配置（出厂设置）？
A: 退出游戏后删除或重命名 `BepInEx/config/com.healthautoarrange.plugin.rules.cfg`，然后重启游戏。插件将在找不到该文件时用内置模板重新生成并应用默认配置。请在删除前备份你现有的配置文件（如需保留）。

Q: 如何报告 bug？需要提供哪些信息？
A: 在 Issues 中请尽量包含：
- 游戏版本（Steam / Demo 版本号）；
- BepInEx 版本与插件的放置路径说明或截图；
- BepInEx/LogOutput.log 中与 HealthAutoArrange 相关的日志（F9 生成的诊断很有帮助）；
- 简要复现步骤与期望结果。

## 界面与设置速览
- 主界面四项：自动整理开关、未识别状态策略、分组优先级（可上下移动）、状态目录（仅显示实际观察到的状态）。
- “高级设置”默认折叠：包括手动编辑 ID、立即重排、输出诊断、提醒高级选项等；普通玩家通常不需要修改这些。

---

## 开发 / 高级用户（放在文件末尾）
如果你想从源码构建或运行测试：

- 需要 .NET SDK。本仓库的 Core 项目目标为 `netstandard2.0`。
- 构建示例：
  ```powershell
  dotnet build HealthAutoArrange.Plugin/HealthAutoArrange.Plugin.csproj -c Release
  dotnet test  HealthAutoArrange.Tests/HealthAutoArrange.Tests.csproj
