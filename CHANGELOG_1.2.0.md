# HealthAutoArrange 1.2.0

## 用户反馈回归

在 v1.1.9（针对 Casualties: Unknown Demo 7.0.1 重新评估）下，部分用户反馈：

> "出现的问题：mod 只在启动时排列了顺序，短时间后立即被原游戏顺序覆盖，这属于回归问题。"

## 根因分析（基于 v6.1 Assembly-CSharp.dll 反编译 + v7.0.1 公开化引用程序集核对）

游戏的 Moodle 刷新循环（`MoodleManager.Update()` + `UpdateMoodles()`）：

```csharp
// MoodleManager.Update() — 每帧执行
private void Update()
{
    updateTime -= Time.unscaledDeltaTime;
    if (updateTime <= 0f) UpdateMoodles();   // 约 0.5s 触发一次
}

// UpdateMoodles() — 重置 0.5s 倒计时，然后清空并重建所有 moodle
public void UpdateMoodles()
{
    sideMoodles = false;
    updateTime = 0.5f;
    UpdatePrevMoodles();   // 快照当前 moodle.type 列表
    ClearMoodles();        // foreach child: UnityEngine.Object.Destroy(child.gameObject)
                           // — Destroy 是延迟到本帧末执行的；postfix 时旧 moodle 仍可能在层级里
    AddAllMoodles();       // 根据 body 状态调用 AddMoodle 重新创建所有 moodle
                           // — 每次 AddMoodle 把新 GameObject SetParent(moodles) 作为最后一个子节点
                           // — anchoredPosition.x = moodleCount * 70（按调用顺序分配槽位）
}
```

关键事实：
1. 游戏每 0.5s 完全销毁并重建 moodle 层级（不是修改顺序，而是销毁所有 GameObject 再重建）。
2. `UnityEngine.Object.Destroy` 是延迟操作：在调用当帧末才真正移除；同帧 postfix 内，C# wrapper 仍可能不为 null（取决于 Unity 内部的 "marked destroyed" 判定时机）。
3. v1.1.3+ 把扫描/排序改到 `UpdateMoodles` postfix 同帧执行，初衷是消除 v1.1.1 跨帧排序带来的"新图标按默认顺序渲染一帧"的闪烁。但同帧执行时，ClearMoodles 的 Destroy 尚未完成，Scan 可能同时看到"待销毁旧 moodle + 新建 moodle"，导致排序对一组半销毁的层级操作，最终渲染出的位置并非预期的排列顺序。
4. 因为游戏每 0.5s 重建一次，第一轮（启动时）排序看起来生效了（manager.moodles 初始可能为空或仅少量遗留），但后续每轮 UpdateMoodles 都会再次清空+重建，并在同帧 postfix 上面对不稳定层级执行排序，效果不稳定或被覆盖。

## v1.2.0 新方案（"换种思路"）

不再依赖 `UpdateMoodles` postfix 单一入口。改为多入口冗余策略，任一入口失败时其他入口兜底：

### 1. AddMoodle postfix（新增）—— 创建时刻精确追踪存活节点

新增 `MoodleManager.AddMoodle(...)` 的 postfix。原方法刚把新 GameObject `SetParent(manager.moodles)` 作为最后一个子节点，postfix 立即读取它的 `Transform.GetInstanceID()` 并写入一个 per-manager 的 "fresh-instance set"。

下次 `Scan()` 在 fresh-set 非空时，只接受 instance id 在集合里的子节点。这精确过滤掉 ClearMoodles 遗留的 Destroy-pending 节点，即便 Unity 的 `obj == null` 假 null 判定在同帧内不可靠。

### 2. Plugin.Update 周期性 safety-net 重排（新增，4 Hz）

在 `UnityUiAdapter.Update()` 中加入 `_nextPeriodicResortRealtime` 计时器，每 0.25s 主动调用一次 `ProcessRefresh()`，**不依赖游戏的 UpdateMoodles 是否触发或是否成功**。

- 计时器到点时：失效签名缓存 → `_scheduler.TryRunNow()` → `ProcessRefresh()` → `Scan` + `ApplySort`。
- 因为 `Plugin.Update`（BepInEx BaseUnityPlugin）通常与 `MoodleManager.Update` 不在同一 Unity 帧序列的同一时刻执行，本轮周期性重排执行时，上一轮 ClearMoodles 的 Destroy 已完成，Scan 看到的是纯净的存活层级。
- 即使 `UpdateMoodles` postfix 因任何原因（重入保护、托管异常、未来游戏更新改变了 moodle 重建路径）没触发或没生效，周期性 safety-net 仍会在 ≤0.25s 内把顺序修正回来。
- 周期性重排结束后调用 `ClearFreshInstanceSet()` 让下一轮从干净状态开始。

### 3. Scan 过滤强化（增强）

在原有 `if (child == null) continue;` 与 `if (!child.gameObject.activeInHierarchy) continue;` 之后，加入：

```csharp
if (_freshManagerKey != 0
    && _freshInstanceIdsByManager.TryGetValue(_freshManagerKey, out var freshSet)
    && freshSet != null && freshSet.Count > 0
    && !freshSet.Contains(child.GetInstanceID()))
{
    continue;   // 不在 fresh-set 中 = ClearMoodles 遗留的 Destroy-pending 节点
}
```

### 4. 诊断增强

`F9` 诊断 dump 现在输出 v1.2.0 统计：

```
v1.2.0 stats: AddMoodlePostfixCount=N, PeriodicResortCount=M, FreshSetSize=K
```

`DumpPatchDiagnostics`（F9 + F8 窗口 Diagnostics 按钮）现在报告 `AddMoodlePostfix` 是否被调用及调用次数，让用户能在日志里一眼看出三道入口分别有没有触发、触发多少次。

## 变更范围

- `HealthAutoArrange.Plugin/UnityUiAdapter.cs`：
  - 新增字段：`PeriodicResortIntervalSeconds`、`_nextPeriodicResortRealtime`、`_periodicResortCount`、`_addMoodlePostfixCount`、`_freshInstanceIdsByManager`、`_freshManagerKey`。
  - 新增 `OnMoodleCreated(MoodleManager)` 方法（被 AddMoodle postfix 调用，记录 fresh-set）。
  - 新增 `ClearFreshInstanceSet()` 方法（在 OnMoodlesUpdated、Reconfigure、ResetLostManagerState、周期性重排结束时调用）。
  - `OnMoodlesUpdated`：绑定 `_freshManagerKey`、调用 ProcessRefresh 后清理 fresh-set。
  - `Update()`：新增周期性 safety-net 重排块（4 Hz）。
  - `Scan`：新增 fresh-set 过滤分支。
  - `DumpDiagnostics`：新增 v1.2.0 统计输出。
- `HealthAutoArrange.Plugin/GamePatches.cs`：
  - 新增 `AddMoodlePostfixInvoked` / `AddMoodlePostfixInvokeCount` 诊断标志。
  - 新增 `AddMoodlePostfix(MoodleManager __instance)` 方法。
- `HealthAutoArrange.Plugin/Plugin.cs`：
  - 注册 `MoodleManager.AddMoodle` postfix。
  - `DumpPatchDiagnostics` 输出 `AddMoodlePostfix` 调用情况。
  - `BepInPlugin` 版本：1.1.9 → 1.2.0。
- `HealthAutoArrange.Plugin/Properties/AssemblyInfo.cs`：`AssemblyVersion` / `AssemblyFileVersion` 1.1.9.0 → 1.2.0.0。
- `tools/static_smoke.py`：放宽版本契约至接受 1.1.8 / 1.1.9 / 1.2.0。

## 验证

- `dotnet build HealthAutoArrange.Plugin.csproj -c Release`：0 warning / 0 error（针对 v7.0.1 公开化 Assembly-CSharp.dll）。
- `dotnet test HealthAutoArrange.Tests.csproj -c Release`：213/213 PASS（Failed: 0, Skipped: 0）。
- `python3 tools/static_smoke.py`：103/103 PASS。
- `python3 tools/version_check.py`：Plugin=1.2.0 / Assembly=1.2.0.0 / File=1.2.0.0 [PASS]。
- `ilspycmd` 反编译产物确认新方法/字段均已编译到 `HealthAutoArrange.Plugin.dll`：`OnMoodleCreated`、`ClearFreshInstanceSet`、`PeriodicResortIntervalSeconds`、`_freshInstanceIdsByManager`、`AddMoodlePostfix`、Scan 内的 fresh-set 过滤分支。

## 兼容性

- v1.2.0 客户端在 v7.0.1 demo 上工作（编译期 ABI 与运行期 Harmony patch 均对齐 v7.0.1）。
- v1.2.0 在 v6.1 demo 上仍可工作（API 表面是 v7.0.1 的真子集）。
- 用户配置与提醒规则向前兼容，无设置丢失。
- v1.2.0 不移除任何 v1.1.x 的修复（F8 设置窗口、Ctrl+R / Ctrl+E 热键、DrawStatusBadge、透明提醒、SafeUpdater 等均保留）。
