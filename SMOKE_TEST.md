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
2. F8：创建空分组，确认它立刻可作为“加入到”目标。
3. 触发至少 3 种 main Moodle，刷新状态目录并分到两个组；保存后顺序变化。
4. 产生 side Moodle；确认 main/side 不跨行。
5. Unknown=Keep：制造一个未分组状态，确认它占原槽位。
6. 切换 Unchipped/chipped；只对本轮实际出现的节点排序，不制造缺失图标。
7. 触发感染揭示前/后；目录只在 UI 节点真实出现后新增。
8. F9：确认 main/side、runtime id、intensity、critical、位置日志可读。
9. 与 CUCoreLib + 至少一个 custom Moodle 同装重复 2-8。
10. 与 QoL: Unknown 同装，在 16:9、21:9、4K/UI Scale 改动后检查 F8 tooltip 和状态栏无明显闪烁。

### 失败判定

- 游戏健康/伤病计算发生变化：立即回退，属于越界修改。
- main/side 被混排：阻断发布。
- 未知状态在默认 Keep 下被移动：阻断发布。
- 每 0.5s 可见跳闪：优先禁用 AnchoredPosition 路径，再排查 Harmony 刷新顺序。
- F8 关闭后仍拦截攻击/交互：检查 `UIUtil.IsPointerOverUIElement` prefix。
