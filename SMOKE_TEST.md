# Smoke test

## 自动/静态检查

运行仓库根目录：

```bash
python3 tools/static_smoke.py
```

它检查源码结构与本次关键安全契约，不等同于编译或实机测试。

## 本机编译

```powershell
dotnet test HealthAutoArrange.Tests/HealthAutoArrange.Tests.csproj
dotnet build HealthAutoArrange.Plugin/HealthAutoArrange.Plugin.csproj -c Release
```

## 实机冒烟顺序

1. 无 QoL/CUCoreLib：启动，确认日志出现 refresh hook 与 AddMoodle capture。
2. F8：点击右上角 `EN/中文`，确认窗口标题、所有分区、`i` hover、提醒设置与诊断反馈即时切换；重启后保持手动选择。
3. 切英文后检查长标签在 1080p/较窄窗口不重叠；切回中文不能残留英文标题。
4. F8：创建空分组，确认它立刻可作为“加入到”目标。
5. 触发至少 3 种 main Moodle，刷新状态目录并分到两个组；保存后顺序变化。
6. 产生 side Moodle；确认 main/side 不跨行。
7. Unknown=Keep：制造一个未分组状态，确认它占原槽位。
8. 切换 Unchipped/chipped；只对本轮实际出现的节点排序，不制造缺失图标。
9. 触发感染揭示前/后；目录只在 UI 节点真实出现后新增。
10. F9：确认 main/side、runtime id、intensity、critical、位置日志可读。
11. 与 CUCoreLib + 至少一个 custom Moodle 同装重复状态目录/排序测试。
12. 与 QoL: Unknown 同装，在 16:9、21:9、4K/UI Scale 改动后检查 F8 tooltip 和状态栏无明显闪烁。

### 失败判定

- 游戏健康/伤病计算发生变化：立即回退，属于越界修改。
- main/side 被混排：阻断发布。
- 未知状态在默认 Keep 下被移动：阻断发布。
- 每 0.5s 可见跳闪：优先禁用 AnchoredPosition 路径，再排查 Harmony 刷新顺序。
- F8 关闭后仍拦截攻击/交互：检查 `UIUtil.IsPointerOverUIElement` prefix。

### 1.1.3 专项回归

- 关闭“自动整理状态图标”，保留一条已启用提醒：排序应停止，但状态提醒仍正常。
- `Once` 长期状态持续 2 分钟：只发送一次；保存无关排序/视觉设置后不得再次发送。
- `60 秒 / 3 次`：约 0/20/40 秒发送，不得在卡顿后补发连刷；同状态提示卡应替换而非叠加。
- 若能制造第三方语义数字 ID（例如 `drug2` 与 `drug3`），两者不得被捕获 fallback 合并；GUI 新规则应显示 `drug2#` / `drug3#`。
- 高级手工旧规则 `pain*` 保持广义前缀兼容；GUI 新建 `pain#` 不得误匹配 `painshock`。
- 在一次刷新捕获缺失后再恢复捕获：状态目录不应长期保留“runtime 临时条目 + 正式 base 条目”两份重复。
