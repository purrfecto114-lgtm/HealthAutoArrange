# Practical review — 2026-08-09

## 已修复

- [x] GUI 主页面降噪；Advanced / Reminders 渐进折叠。
- [x] `i` hover card 取代状态 ID/强度/时间常驻调试行。
- [x] GroupOrder 文本从普通 UI 移除，改为分组 ↑/↓。
- [x] 新建空分组无法成为“加入到”目标的问题：`EnsureGroup()` 保留空分组。
- [x] 新增提醒在 Name 为空时无法选择第一个状态的问题。
- [x] `HealthPanelHint` 明明未实现却作为正常模式展示的问题。
- [x] 状态选择立即保存、其它控件延迟保存的交互不一致。
- [x] 状态目录由 AddMoodle 调用改为真实 Moodle 节点观察。
- [x] `chippedOnly` 中文注释方向错误，不再描述成“仅未削片”。
- [x] `SortPlan` 同权重显式 StateIndex tie-break。
- [x] 未知状态默认 End -> Keep。
- [x] 移除虚构的医学默认状态映射；首次规则只建空优先级分组。
- [x] 刷新 hook 优先 UpdateMoodles、缺失时 AddAllMoodles 降级。
- [x] rules 写入不再先删除原文件再移动临时文件。
- [x] csproj 可通过 Local.props / GameDir / 环境变量配置游戏目录。
- [x] 启动日志记录 Unity/Game/Assembly-CSharp 版本。
- [x] 状态目录在排序关闭时也可主动扫描，避免“必须先启用插件才能看到状态”的死锁。
- [x] AddMoodle 元数据解析按当前 MoodleManager 实例过滤，减少换场景/重建后的陈旧捕获误配。
- [x] 第三方基础 ID 以数字结尾时，UI 生成的 `baseId*` 不再误删语义数字。
- [x] AnchoredPosition 自动路径仅在每一行确实存在可区分的 x 槽位时启用；y 差异不再误判成可横向重排。
- [x] 观察目录限制为最近 256 个基础 ID，避免异常第三方动态 ID 无限增长。

## 有意不做 / 延后

- [ ] 不做“自动判断真正医学危急程度”。状态严重度跨系统，不应把 Wiki 阈值硬编码成医疗真理。
- [ ] 不强行把完整动态编辑器塞进原版 v7 Settings 页。当前 F8 编辑器更可控；等原生/库接口出现可组合列表、按钮和 tooltip 后再迁移。
- [ ] 不同步多人设置。排序是客户端视觉偏好，且 Unchipped 条件可因玩家而异。
- [ ] 不每帧强写位置。只在 Moodle 刷新/配置变更后处理，降低与其它 UI Mod 冲突概率。

## 已知风险

1. `Moodle.type` 的“末尾数字 = intensity 后缀”是当前生态强先例，但第三方 icon/key 本身若以数字结尾，runtime 字符串仍存在天然歧义。现在 UI 生成的通配模式会保留观察到的 icon 数字，且 capture 按 manager 过滤；**只有拿不到 AddMoodle 元数据时的纯 runtime fallback** 仍无法完全消除歧义。
2. IMGUI hover card 比硬 patch 原生设置页稳，但视觉不会与 v7 原生设置完全一致。
3. AnchoredPosition 模式假设同一父节点中当前 x 坐标可以视为槽位；若未来改成动画驱动 x，应该禁用该路径或加更严格探测。
4. 当前源码环境没有游戏 DLL，无法证明当前 Steam build ABI。启动探测只能降低问题，不可能消除版本断裂。
