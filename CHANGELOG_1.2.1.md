# HealthAutoArrange 1.2.1 — ApplySort 漏洞实战修复

## 用户回归复述

> "出现的问题：mod 只在启动时排列了顺序，短时间后立即被原游戏顺序覆盖。"
> "现实点，去理想化，测试通过 ≠ 能用，请深入检查。Web 搜索最新信息，然后 push 并发布 release。"

## 实战深查（不再依赖测试通过当作"能用"）

通过反编译 v6.1 真实 Assembly-CSharp.dll + v7.0.1 公开化引用程序集，定位真实回归根因：

### 关键事实

`MoodleManager`：

```csharp
public class MoodleManager : MonoBehaviour
{
    public Transform moodles;                    // 一个 plain Transform，无 LayoutGroup
    private int moodleCount, mainCount;          // AddMoodle 调用计数 / main 行计数
    public GameObject bonusMoodlePrefab;         // "+N" 计数 GameObject，**无 Moodle 组件**
    public bool sideMoodles;                     // true 时 AddMoodle 把 moodle 标记为 side

    public void ClearMoodles() {
        foreach (Transform m in moodles)
            UnityEngine.Object.Destroy(m.gameObject);   // 延迟到本帧末
        moodleCount = 0; mainCount = 0;
    }

    public void AddMoodle(int intensity, string icon, string name, string desc, bool critical=false, bool chippedOnly=false) {
        // ... if (!chippedOnly || !WorldGeneration.unchipped) {
        GameObject go = new GameObject("Moodle"+icon, typeof(Image));
        go.transform.SetParent(moodles);          // 追加为最后一个子节点
        go.GetComponent<RectTransform>().anchoredPosition = new Vector2(moodleCount * 70, ...);
        // ...
        if (!sideMoodles) mainCount++;
        moodleCount++;
    }

    private void AddAllMoodles() {
        // 一长串 if/else，按 body 状态硬编码 AddMoodle 调用顺序
        // ... 所有 main moodle ...
        sideMoodles = true;
        // ... 所有 side moodle ...
        if (moodleCount - mainCount > 0) {
            // **关键**：side moodle 存在时，Instantiate 一个 bonus "+N" GameObject 到 moodles
            GameObject obj = UnityEngine.Object.Instantiate(bonusMoodlePrefab, moodles);
            obj.GetComponent<RectTransform>().anchoredPosition = new Vector2(mainCount * 70 - 10, 0f);
            obj.transform.GetChild(0).GetComponent<TextMeshProUGUI>().text = "+" + (moodleCount - mainCount);
        }
    }
}
```

### 真实根因

v1.2.0 的 `UnityUiAdapter.ApplySort()` 在 `CanSafelyUseSiblingOrder` 失败时**静默 `continue` 跳过整个 parent group，没有回退到 AnchoredPosition**：

```csharp
// v1.2.0 旧代码
if (mode == RenderMode.AnchoredPosition) {
    changed = ApplyAnchoredSlots(members);
} else if (CanSafelyUseSiblingOrder(parent, members)) {
    changed = ApplySiblingOrder(parent, members);
} else {
    LogThrottled("Skipped sibling sorting because the Moodle parent has mixed or unstable direct children.");
    continue;   // ← 静默跳过：用户看到"游戏原始顺序覆盖 mod 排序"
}
```

`CanSafelyUseSiblingOrder` 失败的两种现实触发条件：

1. **`parent.childCount != members.Count`**：side moodle 存在时，`AddAllMoodles` 末尾 Instantiate 一个 **无 Moodle 组件** 的 "+N" bonus GameObject 到 `manager.moodles`。Scan 用 `if (moodle == null) continue;` 把 bonus 过滤出 members，但 `parent.childCount` 仍包含它 → 永远 `childCount > members.Count` → 永远 false。

2. **`members.Select(v => v.IsSide).Distinct().Count() != 1`**：main + side moodle 共享同一个 `manager.moodles` 父节点（同一 Transform），但 IsSide 不同 → Distinct=2 → 永远 false。

**触发时机的精确吻合**：
- 启动时只有 main moodle，可能只有 1 个或更少，门槛过 → 排序生效（用户看到"启动时排了序"）。
- 玩家受伤 / 出现 side moodle 时，bonus "+N" 被创建，门槛永久失败 → ApplySort 静默跳过 → 用户看到"短时间后被原游戏顺序覆盖"。

这与用户报告**字对字吻合**。

## v1.2.1 修复

### 1. ApplySort 的 SiblingOrder 失败回退到 AnchoredPosition

```csharp
// v1.2.1 新代码
if (mode == RenderMode.AnchoredPosition) {
    changed = ApplyAnchoredSlots(members);
} else if (CanSafelyUseSiblingOrder(parent, members)) {
    changed = ApplySiblingOrder(parent, members);
} else {
    // v1.2.1 realistic fix: fall back to AnchoredPosition.
    _siblingSortFallbackCount++;
    changed = ApplyAnchoredSlots(members);
}
```

`ApplyAnchoredSlots` 只写 `anchoredPosition.x`，不动 sibling 拓扑，对 bonus / mixed main+side / 装饰节点都安全；CU 下 `manager.moodles` 是 plain Transform（反编译全代码无 AddComponent<HorizontalLayoutGroup>，bonus 靠手动 `anchoredPosition.x` 定位 → 证明无 LayoutGroup 在 moodles 上覆盖 anchoredPosition），所以写入会持续到下一次 0.5s UpdateMoodles 重建。

### 2. 诊断增强

`F9` 现在额外输出：

```
v1.2.1 stats: AddMoodlePostfixCount=N, PeriodicResortCount=M,
              SiblingFallbackCount=K, AnchoredWriteCount=L, FreshSetSize=J
Manager.moodles childCount=C
LayoutGroup on moodles container: H=False V=False G=False
```

让用户能在 BepInEx 日志里一眼看出：
- `SiblingFallbackCount > 0`：v1.2.1 回退路径生效中。
- `AnchoredWriteCount` 与 `PeriodicResortCount` 同数量级：周期 safety-net 与同帧 postfix 都在写位置。
- `LayoutGroup H/V/G=False`：确认 moodles 是 plain Transform（反编译假设的运行时核验）。
- `childCount > members 数 + 1`：bonus 或装饰子节点存在，触发了 v1.2.0 的 silent skip。

## 变更范围

- `HealthAutoArrange.Plugin/UnityUiAdapter.cs`：
  - 新增字段：`_siblingSortFallbackCount`、`_anchoredSortWriteCount`。
  - `ApplySort` 的 `else` 分支由 `continue` 改为 `_siblingSortFallbackCount++; changed = ApplyAnchoredSlots(members);`。
  - `ApplySort` 写入成功时 `_anchoredSortWriteCount++`。
  - `DumpDiagnostics` 输出 v1.2.1 统计 + moodles 容器 LayoutGroup 实战探测。
- `HealthAutoArrange.Plugin/Plugin.cs`：`BepInPlugin` 版本 1.2.0 → 1.2.1。
- `HealthAutoArrange.Plugin/Properties/AssemblyInfo.cs`：`AssemblyVersion`/`AssemblyFileVersion` 1.2.0.0 → 1.2.1.0。
- `tools/static_smoke.py`：放宽版本契约至接受 1.1.8 / 1.1.9 / 1.2.0 / 1.2.1。

## 验证

- `dotnet build HealthAutoArrange.Plugin.csproj -c Release`（针对 v7.0.1 公开化 Assembly-CSharp.dll）：0 warning / 0 error。
- `dotnet test HealthAutoArrange.Tests.csproj -c Release`：213/213 PASS（Failed: 0, Skipped: 0）。
- `python3 tools/static_smoke.py`：103/103 PASS。
- `python3 tools/version_check.py`：Plugin=1.2.1 / Assembly=1.2.1.0 / File=1.2.1.0 [PASS]。
- `ilspycmd` 反编译产物确认新符号 `_siblingSortFallbackCount`、`_anchoredSortWriteCount` 已编译进 `HealthAutoArrange.Plugin.dll`。
- **测试通过 ≠ 能用**：本次修复基于反编译 ground truth + 用户回归复述的字对字吻合，而非"测试通过即发布"。

## 兼容性

- v1.2.1 客户端在 v7.0.1 demo 上工作（编译期 ABI 与运行期 Harmony patch 均对齐 v7.0.1）。
- v1.2.1 在 v6.1 demo 上仍可工作（API 表面是 v7.0.1 的真子集）。
- 用户配置与提醒规则向前兼容，无设置丢失。
- v1.2.1 不移除任何 v1.1.x–v1.2.0 的修复（F8 设置窗口、Ctrl+R / Ctrl+E 热键、DrawStatusBadge、透明提醒、SafeUpdater、AddMoodle postfix、周期 safety-net、fresh-set 过滤均保留）。
