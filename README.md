# HealthAutoArrange 1.1.8

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
  - 在“版本与更新”中查看本次启动的检查结果；有新版时可打开 GitHub Release 页面
- F9：将当前 Moodle 诊断输出到 `BepInEx/LogOutput.log`（用于反馈问题时的诊断信息）
- 配置会写入规则文件：`BepInEx/config/com.healthautoarrange.plugin.rules.cfg`（点击“保存并应用”会写入）
- 1.1.6 起，未保存修改在关闭/重载前会要求“保存 / 丢弃 / 取消”；磁盘保存失败不会清除“未保存”标记。

默认行为要点：
- 未识别的新状态默认为 “Keep”（保留原位），不会被自动移到末尾，以避免误分类。
- 自动整理只改变 Moodle UI 的客户端排列；提醒 overlay 也仅在本地绘制。
- F8 设置窗口打开时，插件会把 `UIUtil.IsPointerOverUIElement()` 的最终结果提升为 `true`，用于阻止拖动设置窗口时触发游戏攻击/交互；窗口关闭后保留游戏原判断。
- 插件不写入生命/伤势等游戏数值，不改变状态触发条件，也不修改网络同步数据。

## 1.1.8 启动更新提醒

- 默认在每次启动游戏约 10 秒后检查一次；同一游戏进程内不会周期检查，也没有“立即检查”按钮。可在 F8 →“版本与更新”关闭下次启动检查。
- 唯一网络源是 GitHub 托管的 `latest.txt`；即使发生重定向，最终地址也必须仍是 GitHub HTTPS。
- `latest.txt` 仅通过 HTTPS 从 GitHub 获取，重定向后的最终地址也必须仍是 GitHub HTTPS（`github.com` 或 `raw.githubusercontent.com`）。发现新版时，游戏内显示一次提醒，并可从 F8 打开 GitHub Release 页面。
- 插件不会下载更新包、解压内容、执行内容或替换 DLL。请退出游戏后从 GitHub Release 手动更新。
- 检查会向 GitHub 发送普通 HTTPS GET，因此会像访问任何网站一样向服务端暴露网络 IP；网络不可用只记录状态，不影响模组其余功能。

1.1.8 起，更新器不再使用 RSA 签名清单（1.1.6/1.1.7 使用的私钥已被作者在公钥传递过程中泄露，且 1.1.7 已是仅通知模式，不下载或替换任何 DLL，RSA 价值低于其复杂度与密钥轮换风险）。信任源于 HTTPS + GitHub 主机白名单 + 用户手动下载。如果未来恢复自动下载能力，会使用一对新生成的密钥重新引入签名。

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
- “版本与更新”独立折叠，不和提醒/诊断混在一起；窗口顶部显示当前 Mod 版本，底部的保存/重载/关闭操作始终固定在滚动区域之外。

---

## 开发 / 高级用户（放在文件末尾）
如果你想从源码构建或运行测试：

- 需要 .NET SDK。本仓库的 Core 项目目标为 `netstandard2.0`。
- 构建示例：
  ```powershell
  dotnet build HealthAutoArrange.Plugin/HealthAutoArrange.Plugin.csproj -c Release
  dotnet test  HealthAutoArrange.Tests/HealthAutoArrange.Tests.csproj
