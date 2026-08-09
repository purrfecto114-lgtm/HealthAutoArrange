# HealthAutoArrange

《Casualties: Unknown》状态图标（Moodle）**客户端 UI 排序器**。设计目标是保守地重新排列游戏已经生成的图标，而不是重新实现医疗系统。

> **非官方项目：** 本项目与《Casualties: Unknown》的开发者及发行商无隶属或授权关系。使用 Mod 的风险由用户自行承担。

## 这版的现实边界

- 目标生态：Unity Mono + BepInEx 5.x；社区也存在 BepInEx 6 兼容案例，但本项目工程仍按 BepInEx 5 / net472 编译。
- 源码最初按 Demo 7.0.1 反编译契约开发；游戏和社区 Mod 仍在快速变化，因此启动时会记录 Unity/Game/Assembly-CSharp 版本并探测刷新方法。
- 刷新 hook：优先 `MoodleManager.UpdateMoodles`，缺失时降级到 `AddAllMoodles`；两者只 patch 一个。
- 状态目录只收录**实际扫描到的 Moodle 组件**。`AddMoodle` prefix 仅用于补充显示名、强度、critical、chippedOnly 等元数据。
- main / side Moodle 永远分开排序。插件不改变脑芯片/Unchipped 可见性、状态出现条件、严重度、critical glow 或医学数值。
- 第三方/新版本未知状态默认 `Keep`（保留原位）。只有用户明确选择 `End` 才会把未知状态推到末尾。
- 不提供“医学优先级 = 真正病情严重度”的默认 preset。多系统状态具有上下文依赖，默认规则只提供空的 Priority 1 / Priority 2 分组，状态由玩家从运行时目录分配。

## F8 设置界面

主界面只保留四件事：

1. **自动整理状态图标**
2. **未识别状态**：默认“保持原位（推荐）”
3. **排序分组**：上/下按钮直接调整优先级，不再要求普通用户编辑 `GroupOrder` 文本
4. **状态目录**：只显示实际观察到的 Moodle；加入/移动到目标分组

复杂说明放在每段右侧的 `i` hover card。技术 ID、强度、main/side、critical、chippedOnly 和最近观察时间都收进状态行右侧 `i`，避免常驻两行调试文字。

“高级设置”默认折叠，包含：

- 手动编辑状态 ID（兼容/诊断用途）
- 立即重排、输出诊断
- 状态提醒（实验性，二次折叠）

`HealthPanelHint` 仍保留枚举用于旧配置兼容，但因为没有可靠实现，不再作为新规则可选模式；旧规则会明确标注“旧/未实现”，可一键改成日志模式。

**保存语义统一**：状态加入/移动只改内存模型，点击“保存并应用”才写 `rules.cfg`；“放弃修改并重载”会恢复磁盘版本。窗口底部会显示“有未保存修改”，提醒规则的细项变化也参与这个标记。此前“状态选择立即落盘、其它控件等待 Save”的混合行为已移除。

## 排序与渲染

刷新完成后延迟一帧处理，以避开 Unity 同帧延迟销毁的旧 Moodle 节点：

```
游戏刷新 -> 下一帧扫描真实 Moodle -> 按 main/side 分行 -> SortPlan -> 应用 UI 顺序
```

渲染策略仍是保守双路径：

- 父节点存在 Unity `LayoutGroup`：调整 sibling order。
- 没有 LayoutGroup、且**每一行**都能确认存在可区分的横向 `RectTransform.anchoredPosition.x` 槽位：交换行内 x 槽位，保留 y。仅有 y 差异不会触发该路径。
- 不能确认坐标槽位时：回退 sibling order。

`SortPlan` 现在显式用原始 `StateIndex` 作为最终 tie-break，真正保证同权重状态的原始相对顺序；不再依赖 `List<T>.Sort` 的未保证稳定性。

## 状态目录为何改为“实际观察”

旧版直接把最近的 `AddMoodle(...)` 调用当成状态目录。这个假设过强：调用发生不等于最终一定生成玩家可见的 Moodle，尤其在 chippedOnly、游戏内部过滤、side row 和第三方 Mod 路径下。

现在：

- `MoodleCaptureRegistry`：仅保存 `AddMoodle` 元数据。
- `StateObservationRegistry`：只有扫描到真实 `Moodle` 组件才记入设置目录；会保留本次游戏会话中最近观察的基础状态，并限制总量为 256。
- 状态条目会合并已观察的强度，并记录是否在 main/side 出现、是否曾 critical/chippedOnly。

## 默认配置

首次生成的 BepInEx 模板不再伪造 `Vital / Infection / Bleeding / Fracture` 等医学默认映射：

```ini
GroupOrder = Priority 1, Priority 2
UnknownStatePolicy = Keep
Group.Priority 1.States =
Group.Priority 2.States =
```

玩家先实际触发状态，再在 F8 的状态目录分组。这样新版本或 Mod 新状态不会因为我们的猜测被错误降级。

## 构建

需要 .NET SDK，以及本地游戏程序集。插件项目支持三种设置游戏路径的方法：

1. `HealthAutoArrange.Local.props`（推荐）：复制 `HealthAutoArrange.Local.props.example` 并修改 `GameDir`。
2. MSBuild 属性：`dotnet build ... -p:GameDir="X:\...\Casualties Unknown Demo"`
3. 环境变量：`HEALTHAUTOARRANGE_GAME_DIR`

仓库不包含游戏 DLL。未设置游戏目录时，Core 与测试仍可构建，但 Plugin 项目无法解析游戏程序集。

```powershell
dotnet build HealthAutoArrange.Plugin/HealthAutoArrange.Plugin.csproj -c Release
dotnet test HealthAutoArrange.Tests/HealthAutoArrange.Tests.csproj
```

部署 `HealthAutoArrange.Plugin.dll` 与 `HealthAutoArrange.Core.dll` 到 `BepInEx/plugins/HealthAutoArrange/`。

## 自动化构建

- push 与 pull request 会运行静态 smoke 和全部 Core 单元测试，不下载或提交游戏程序集。
- Release workflow 需要维护者配置私有 Secret `GAME_REFS_URL`。该地址指向包含本地编译引用的私有压缩包。
- Release 产物只包含 `HealthAutoArrange.Plugin.dll` 与 `HealthAutoArrange.Core.dll`。
- `Assembly-CSharp.dll`、Unity、BepInEx 和 Harmony 程序集不会上传到仓库或公开 Release。

## 热键

- `F8`：打开/关闭设置窗口
- `F9`：输出当前 Moodle 诊断到 `BepInEx/LogOutput.log`

## 兼容策略

- 与 CUCoreLib：不接管其状态生成；当前 CUCoreLib 会在 `AddAllMoodles` 中注入自定义状态，并使用 `important` 区分 main/side。我们只在生成之后排序。
- 与 QoL: Unknown：不修改它的设置注册或 Moodle 视觉效果。F8 自有 IMGUI 编辑器避免复制/强 patch v7 设置页；CUCoreLib 的原生注册表当前以 Bool/Int/Float/Dropdown/Keybind 为主，无法自然表达“运行时动态状态目录 + 分组移动 + hover 诊断”，因此本版不做两套设置源的重复同步。
- 多人：排序和设置是客户端视觉偏好，不做网络同步。每个玩家可能拥有不同的 Unchipped 条件，因此同步 UI 顺序反而可能错误。

## 仍需实机验证（不能用静态测试代替）

- 当前 Steam build 的 `MoodleManager.moodles` / `Moodle.type` / `Moodle.isSide` 契约是否仍一致。
- 16:9、21:9、4K、非 100% UI Scale 下 F8 hover card 是否越界。
- main/side 状态同时大量出现时，hover/健康面板展开是否保持正确行与 tooltip/glow。
- QoL: Unknown、CUCoreLib、自定义 Moodle Mod 并装时是否有 Harmony 顺序或重建闪烁。
- Unchipped / chipped、感染揭示、critical 状态、层切换/重生后的目录观察是否符合预期。

详见 `PRACTICAL_REVIEW_20260809.md` 与 `SMOKE_TEST.md`。

## 许可证

MIT LICENSE

## 辅助
gpt 5.6 sol
