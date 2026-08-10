using System;
using System.Collections.Generic;
using System.Linq;
using BepInEx.Logging;
using System.Text;
using HealthAutoArrange.Core;
using UnityEngine;
using UnityEngine.UI;

namespace HealthAutoArrange.Plugin
{
    /// <summary>
    /// Unity/游戏适配层（融合 MoodleSorter_Source 的运行时优势）：
    /// - AddMoodle 前缀捕获元数据（runtime id、图标、强度、显示名、critical、创建顺序、manager、行）；
    /// - 刷新 postfix 只记录刷新边界；真正扫描/排序严格跨到后续 Unity frame，避免重建栈内改层级；
    /// - 按 manager.moodles 扫描 Moodle 组件，支持 main/side 行隔离；
    /// - Auto 渲染模式：LayoutGroup → anchoredPosition slots → sibling index；
    /// - 未知状态保持现有 End/Keep 策略（由 SortPlan 决定）；
    /// - 保留 BottomAlert/Log 提醒；可捕获的托管异常会隔离并记录。
    /// 注意：任何托管 try/catch 都不能承诺拦住 Unity 原生层崩溃、StackOverflow 或进程级故障，
    /// 因此适配层仍以减少重入与避开 Destroy 延迟窗口为首要稳定性策略。
    /// </summary>
    public sealed class UnityUiAdapter
    {
        private SortPlan _plan;
        private ReminderEngine _reminders;
        private readonly ReminderDispatcher _dispatcher;
        private readonly Action<LogLevel, string> _log;
        private readonly Action<ReminderMessage, ReminderRenderContext> _onReminder;
        private readonly MoodleCaptureRegistry _captures = new MoodleCaptureRegistry();
        private readonly StateObservationRegistry _observations = new StateObservationRegistry();
        private readonly ArrangeRuntimeSettings _runtime = new ArrangeRuntimeSettings();
        private readonly SortScheduler _scheduler = new SortScheduler();

        private MoodleManager _manager;
        private ArrangeConfig _config;
        private string _lastSignature = string.Empty;
        private readonly HashSet<string> _lastPresentStates = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        private List<MoodleVisual> _lastVisualSnapshot = new List<MoodleVisual>();
        private bool _hasPresentSnapshot;
        private float _nextReminderTickRealtime;
        private int _captureFloorSequence;
        private readonly Dictionary<int, int> _captureBoundaryByManager = new Dictionary<int, int>();
        private const float ReminderTickSeconds = 0.25f;
        private const int MaxRememberedErrorMessages = 64;
        private readonly Dictionary<string, float> _lastErrorLogTime = new Dictionary<string, float>();

        public UnityUiAdapter(
            SortPlan plan,
            ReminderEngine reminders,
            ReminderDispatcher dispatcher,
            Action<LogLevel, string> log,
            Action<ReminderMessage, ReminderRenderContext> onReminder = null)
        {
            _plan = plan ?? throw new ArgumentNullException(nameof(plan));
            _reminders = reminders ?? throw new ArgumentNullException(nameof(reminders));
            _dispatcher = dispatcher ?? throw new ArgumentNullException(nameof(dispatcher));
            _log = log;
            _onReminder = onReminder;
        }

        /// <summary>AddMoodle 捕获注册表（供诊断与测试）。</summary>
        public MoodleCaptureRegistry Captures => _captures;

        /// <summary>Only Moodle components actually observed in the UI hierarchy.</summary>
        public IReadOnlyList<StateCatalogEntry> ObservedStates => _observations.Snapshot();

        /// <summary>
        /// Refresh the catalog from real Moodle components even when sorting is disabled.
        /// This is read-only with respect to UI ordering: Scan only observes current nodes.
        /// </summary>
        public IReadOnlyList<StateCatalogEntry> RefreshObservedStates()
        {
            try
            {
                // Manual/catalog reads must obey the same hierarchy-stability gate as sorting.
                // F8 can be pressed in the exact refresh frame; scanning here would otherwise
                // reintroduce the stale/destroy-pending read path that the deferred scheduler avoids.
                if (!_scheduler.HasPending && _manager != null) Scan(_manager);
            }
            catch (Exception ex)
            {
                LogThrottled($"State catalog refresh failed safely: {ex.Message}");
            }
            return _observations.Snapshot();
        }

        /// <summary>
        /// 应用新的配置：重建排序计划与提醒引擎，保留当前 manager 与捕获注册表。
        /// 重置变更签名，使下一次刷新按新计划重新排序。
        /// </summary>
        public void Reconfigure(ArrangeConfig config, bool enabled)
        {
            if (config == null) throw new ArgumentNullException(nameof(config));
            _config = config;
            _plan = config.CreateSortPlan();
            // Preserve cadence/episode state for reminder rules whose trigger semantics did not
            // change. Saving an unrelated sort/visual setting must not make a continuous Once rule
            // look like a new appearance.
            _reminders.Reconfigure(config.Reminders);
            _runtime.Enabled = enabled;
            _lastSignature = string.Empty;
            _nextReminderTickRealtime = 0f;

            // Keep the last confirmed presence snapshot and request a fresh scan. The master UI
            // toggle controls sorting only; reminder rules remain independent as stated in the GUI.
            // This also avoids the historical save->retrigger reminder bug.
            if (_manager != null) ScheduleAfterCurrentFrame();
        }

        /// <summary>
        /// 按当前配置请求一次重排：失效签名缓存并在下一帧处理。
        /// 禁用时不产生视觉修改。
        /// </summary>
        public void ForceResort()
        {
            if (!_runtime.Enabled) return;
            _lastSignature = string.Empty;
            ScheduleAfterCurrentFrame();
        }

        /// <summary>
        /// AddMoodle 前缀调用：捕获本次调用的元数据。
        /// </summary>
        public void OnMoodleAdded(
            MoodleManager manager,
            int intensity,
            string icon,
            string name,
            string desc,
            bool critical,
            bool chippedOnly)
        {
            try
            {
                _captures.Capture(manager, intensity, icon, name, desc, critical, chippedOnly, manager.sideMoodles);
            }
            catch (Exception ex)
            {
                LogThrottled($"AddMoodle capture failed: {ex.Message}");
            }
        }

        /// <summary>
        /// UpdateMoodles 后置调用：刷新完成边界。
        /// 安排下一帧扫描/排序/提醒，避免同一刷新帧仍包含待销毁旧节点。
        /// </summary>
        public void OnMoodlesUpdated(MoodleManager manager)
        {
            if (manager == null) return;

            // Manager normally is singular. Clear per-manager sequence boundaries when the actual
            // object changes so a recycled Unity instance ID can never inherit a stale capture
            // floor from a previously destroyed manager. Capture resolution itself remains scoped
            // by manager reference.
            if (!ReferenceEquals(_manager, manager))
            {
                _captureBoundaryByManager.Clear();
                _captureFloorSequence = 0;
            }
            _manager = manager;

            // AddMoodle calls for this refresh have already happened before this postfix. Move the
            // metadata window forward without clearing the registry in a prefix. Other mods may
            // patch the same refresh/add paths, so a competing prefix clear would be Harmony-order
            // sensitive and could erase captures produced earlier in the same refresh.
            var managerKey = manager.GetInstanceID();
            if (!_captureBoundaryByManager.TryGetValue(managerKey, out _captureFloorSequence))
                _captureFloorSequence = 0;
            _captureBoundaryByManager[managerKey] = _captures.LatestSequence;
            if (_captureBoundaryByManager.Count > 16)
            {
                // Managers are normally singular. If scenes/mods churn them, avoid an unbounded
                // bookkeeping dictionary; stale keys are only a metadata optimization.
                var keep = _captureBoundaryByManager[managerKey];
                _captureBoundaryByManager.Clear();
                _captureBoundaryByManager[managerKey] = keep;
            }

            // 刷新边界：即使关闭排序，也继续观察 Moodle 并驱动已启用的提醒；
            // 只有实际 UI 重排受主开关控制。
            if (_runtime.Enabled) _lastSignature = string.Empty;

            // 不在 MoodleManager.UpdateMoodles/AddAllMoodles 的 Harmony postfix 调用栈内
            // 扫描或修改 Transform 层级。Unity 的 Destroy 在当前 Update 循环结束后才真正
            // 销毁对象；严格跨 frame 可避免读到待销毁旧节点，也切断 SetSiblingIndex 引起的
            // Transform 子级变化回调与 Moodle 刷新之间的同步递归链。
            ScheduleAfterCurrentFrame();
        }

        /// <summary>
        /// 将刷新合并到至少下一 Unity frame。使用 frame token 而不是仅依赖“下一次 Update”，
        /// 因为不同 MonoBehaviour 的 Update 顺序并不等同于跨帧。
        /// </summary>
        private void ScheduleAfterCurrentFrame()
        {
            _scheduler.OnGameRefreshCompleted(Time.frameCount);
        }

        /// <summary>
        /// 每帧调用：处理上一帧推迟的挂起任务，并低频推进提醒计时。
        /// 不每帧扫描/强制写位置；排序禁用时仍保持状态观察与已启用提醒。
        /// </summary>
        public void Update()
        {
            // UnityEngine.Object 销毁后会出现“托管引用非 null、Unity == null”的假 null。
            // 即使场景切换没有再触发 Moodle refresh，也要及时丢弃旧 manager/提醒快照。
            if (!ReferenceEquals(_manager, null) && _manager == null)
            {
                ResetLostManagerState();
            }

            if (_scheduler.TryDeferred(Time.frameCount))
            {
                ProcessRefresh();
            }

            // Periodic reminder cadence must not depend on how often the game chooses to rebuild
            // Moodle UI. Reuse the last confirmed snapshot and tick cheaply at 4 Hz.
            // If the game is fully paused, do not emit new reminders.
            if (_hasPresentSnapshot
                && Time.timeScale > 0f
                && Time.realtimeSinceStartup >= _nextReminderTickRealtime)
            {
                RunRemindersSnapshot();
                _nextReminderTickRealtime = Time.realtimeSinceStartup + ReminderTickSeconds;
            }
        }

        /// <summary>
        /// 执行一次刷新处理：扫描 → 提醒 → 排序。
        /// 游戏刷新已由 scheduler 保证跨帧；扫描失败时放弃本轮并等待下一刷新边界。
        /// </summary>
        private void ProcessRefresh()
        {
            if (_manager == null)
            {
                // UnityEngine.Object 的“假 null”表示 manager 已被销毁。旧实现会在这里
                // 每帧重新挂起，形成永久重试循环；同时提醒继续使用旧状态快照。
                // 现在清掉失效 manager，并把存在快照变为空，等待游戏提供新的刷新边界。
                ResetLostManagerState();
                return;
            }

            List<MoodleVisual> visuals;
            try
            {
                visuals = Scan(_manager);
            }
            catch (Exception ex)
            {
                LogThrottled($"Moodle scan failed: {ex.Message}");
                // 已经跨过刷新帧仍无法安全扫描时，不做每帧无限重试。下一次游戏刷新
                // 或用户手动重排会再次调度；这样优先保护主循环稳定性。
                return;
            }

            UpdatePresentSnapshot(visuals);
            if (Time.timeScale > 0f)
            {
                RunRemindersSnapshot();
            }
            _nextReminderTickRealtime = Time.realtimeSinceStartup + ReminderTickSeconds;

            if (!_runtime.Enabled || visuals.Count < 2) return;

            try
            {
                var signature = BuildSignature(visuals);
                if (signature == _lastSignature) return;
                ApplySort(visuals);
                // SetSiblingIndex can synchronously trigger hierarchy-change callbacks. If one of
                // those callbacks caused another Moodle refresh, its postfix has already queued a
                // later frame. Do not rescan a hierarchy that may now contain Destroy-pending nodes.
                if (_scheduler.HasPending)
                {
                    _lastSignature = string.Empty;
                    return;
                }
                _lastSignature = BuildSignature(Scan(_manager));
            }
            catch (Exception ex)
            {
                LogThrottled($"Moodle sorting failed safely: {ex.Message}");
            }
        }

        /// <summary>
        /// 丢弃已销毁/丢失的 MoodleManager 以及与其绑定的短期 UI 状态。
        /// 不触碰用户配置或提醒规则本身；新 manager 的下一次刷新会重新建立快照。
        /// </summary>
        private void ResetLostManagerState()
        {
            _manager = null;
            _captureBoundaryByManager.Clear();
            _captureFloorSequence = 0;
            _lastSignature = string.Empty;
            UpdatePresentSnapshot(new List<MoodleVisual>());
            _nextReminderTickRealtime = 0f;
        }

        /// <summary>
        /// F9 诊断入口：dump 当前 Moodle 的 id/基础名/显示名/行/强度/critical/创建顺序/位置。
        /// </summary>
        public void DumpDiagnostics()
        {
            try
            {
                _log?.Invoke(LogLevel.Info, "===== HealthAutoArrange diagnostic dump =====");
                if (_scheduler.HasPending)
                {
                    // Do not let an F9 diagnostic read bypass the same-frame/next-frame stability
                    // gate. Even reading manager.name is avoided here because the manager may be a
                    // Unity fake-null/destroy-pending object during a scene/UI rebuild.
                    _log?.Invoke(LogLevel.Info, "Pending=True; live Moodle hierarchy scan skipped until a later frame.");
                    _log?.Invoke(LogLevel.Info, "===== end HealthAutoArrange dump =====");
                    return;
                }

                _log?.Invoke(LogLevel.Info, $"Pending=False, Manager={(_manager != null ? _manager.name : "null")}");
                var visuals = _manager != null ? Scan(_manager) : new List<MoodleVisual>();
                if (visuals.Count == 0)
                {
                    _log?.Invoke(LogLevel.Info, "No Moodle components currently found.");
                    _log?.Invoke(LogLevel.Info, "===== end HealthAutoArrange dump =====");
                    return;
                }

                foreach (var v in visuals.OrderBy(x => x.SiblingIndex))
                {
                    var capture = v.Capture;
                    var pos = v.RectTransform != null ? v.RectTransform.anchoredPosition.ToString() : "n/a";
                    var diagnosticBaseId = capture != null && !string.IsNullOrWhiteSpace(capture.IconId)
                        ? MoodleIdentity.NormalizeRuntimeId(capture.IconId)
                        : MoodleIdentity.NormalizeRuntimeId(v.RuntimeId);
                    _log?.Invoke(LogLevel.Info, "id=" + v.RuntimeId
                        + " base=" + diagnosticBaseId
                        + " name='" + (capture != null ? capture.DisplayName : string.Empty) + "'"
                        + " row=" + (v.IsSide ? "side" : "main")
                        + " intensity=" + (capture != null ? capture.Intensity.ToString() : "unknown")
                        + " critical=" + (capture != null ? capture.Critical : false)
                        + " seq=" + (capture != null ? capture.Sequence : -1)
                        + " sibling=" + v.SiblingIndex
                        + " anchored=" + pos);
                }
                _log?.Invoke(LogLevel.Info, "===== end HealthAutoArrange dump =====");
            }
            catch (Exception ex)
            {
                LogThrottled($"Diagnostic dump failed: {ex.Message}");
            }
        }

        /// <summary>
        /// 扫描 manager.moodles 子节点中的 Moodle 组件。
        /// </summary>
        private List<MoodleVisual> Scan(MoodleManager manager)
        {
            if (manager == null) throw new InvalidOperationException("MoodleManager is unavailable.");

            Transform container;
            try
            {
                container = manager.moodles;
            }
            catch (Exception ex)
            {
                throw new InvalidOperationException("MoodleManager.moodles access failed.", ex);
            }
            if (container == null) return new List<MoodleVisual>();

            // Never turn a failed/partial hierarchy enumeration into a valid snapshot. A partial
            // snapshot is worse than skipping one refresh because it can drive reminders from false
            // absence data and, more importantly, can produce a stale sort plan against a hierarchy
            // that changed while it was being read.
            var result = new List<MoodleVisual>();
            try
            {
                var expectedChildCount = container.childCount;
                for (int i = 0; i < expectedChildCount; i++)
                {
                    if (container == null || container.childCount != expectedChildCount)
                        throw new InvalidOperationException("Moodle hierarchy changed during scan.");

                    var child = container.GetChild(i);
                    if (child == null) continue;
                    // 防御性跳过非激活节点。排序本身已严格跨帧，但场景切换或其他
                    // Mod 仍可能留下尚未完成生命周期清理的非激活 UI 节点。
                    if (!child.gameObject.activeInHierarchy) continue;
                    var moodle = child.GetComponent<Moodle>();
                    if (moodle == null) continue;

                    var runtimeId = moodle.type;
                    if (string.IsNullOrWhiteSpace(runtimeId)) runtimeId = child.name;
                    var rect = child as RectTransform;
                    var siblingIndex = child.GetSiblingIndex();
                    if (child.parent != container || siblingIndex != i)
                        throw new InvalidOperationException("Moodle hierarchy reordered during scan.");

                    var capture = _captures.Resolve(runtimeId, manager, _captureFloorSequence);
                    result.Add(new MoodleVisual
                    {
                        Component = child,
                        RectTransform = rect,
                        RuntimeId = runtimeId,
                        IsSide = moodle.isSide,
                        SiblingIndex = siblingIndex,
                        OriginalAnchoredPosition = rect != null ? rect.anchoredPosition : Vector2.zero,
                        Capture = capture
                    });
                }

                if (container == null || container.childCount != expectedChildCount)
                    throw new InvalidOperationException("Moodle hierarchy changed before scan completed.");

                // Commit observation metadata only after the hierarchy snapshot proved stable.
                var observedAt = DateTimeOffset.UtcNow;
                foreach (var visual in result)
                    _observations.Observe(visual.RuntimeId, visual.Capture, visual.IsSide, observedAt);
            }
            catch (Exception ex)
            {
                throw new InvalidOperationException("Moodle child enumeration was not stable.", ex);
            }
            return result;
        }

        /// <summary>
        /// 按父节点分组应用排序（Auto 渲染模式解析）。
        /// </summary>
        private void ApplySort(List<MoodleVisual> visuals)
        {
            foreach (var parentGroup in visuals
                .Where(v => v.Component != null && v.Component.parent != null)
                .GroupBy(v => v.Component.parent))
            {
                var parent = parentGroup.Key;
                var members = parentGroup.ToList();
                var mode = ResolveMode(parent, members);
                bool changed;
                if (mode == RenderMode.AnchoredPosition)
                {
                    changed = ApplyAnchoredSlots(members);
                }
                else if (CanSafelyUseSiblingOrder(parent, members))
                {
                    changed = ApplySiblingOrder(parent, members);
                }
                else
                {
                    // Sibling reordering is only deterministic when this parent is a single Moodle
                    // row with no unobserved/inactive/non-Moodle direct children. In a mixed parent,
                    // SetSiblingIndex necessarily shifts those other children and every write can
                    // synchronously invoke hierarchy callbacks. Prefer one missed arrangement over
                    // mutating an unknown/shared UI topology.
                    LogThrottled("Skipped sibling sorting because the Moodle parent has mixed or unstable direct children.");
                    continue;
                }

                // A write in the first parent group can synchronously trigger another Moodle refresh.
                // Stop the whole stale snapshot here; continuing with later parent groups would apply
                // decisions computed from pre-refresh objects. The queued frame will rescan all groups.
                if (_scheduler.HasPending)
                {
                    _lastSignature = string.Empty;
                    return;
                }

                // 仅实际写入布局时记录，且使用 Debug 级（默认不进 LogOutput.log），
                // 避免游戏每帧重建图标导致的逐帧日志堆积。
                if (changed)
                {
                    _log?.Invoke(LogLevel.Debug, $"Arranged {members.Count} moodles ({mode}).");
                }
            }
        }

        /// <summary>
        /// 异常日志节流：同一文本 5 秒内仅记录第一条，避免持续故障时逐帧刷屏；
        /// 只作用于异常站点，F9 诊断 dump 不走此路径。
        /// </summary>
        private void LogThrottled(string message)
        {
            var now = Time.realtimeSinceStartup;
            if (_lastErrorLogTime.TryGetValue(message, out var last) && now - last < 5f) return;
            if (!_lastErrorLogTime.ContainsKey(message) && _lastErrorLogTime.Count >= MaxRememberedErrorMessages)
            {
                // Exception text can contain changing object/value details. Never let a long-running
                // fault pattern turn this throttle cache itself into an unbounded memory leak.
                _lastErrorLogTime.Clear();
            }
            _lastErrorLogTime[message] = now;
            _log?.Invoke(LogLevel.Warning, message);
        }

        private RenderMode ResolveMode(Transform parent, List<MoodleVisual> members)
        {
            var hasLayout = HasLayoutGroup(parent);

            // Anchored fallback currently reorders the horizontal (x) slot only.
            // Do not select it merely because y differs between main/side rows or because
            // a vertical animation produced distinct positions. Every row with 2+ items
            // must expose distinct x slots; otherwise sibling order is the safer fallback.
            var rows = members.GroupBy(v => v.IsSide).ToList();
            var hasSortableRow = rows.Any(g => g.Count() > 1);
            var hasDistinctHorizontalSlots = hasSortableRow && rows.All(group =>
            {
                var row = group.ToList();
                if (row.Count < 2) return true;
                if (row.Any(v => v.RectTransform == null)) return false;
                return row.Select(v => Mathf.RoundToInt(v.OriginalAnchoredPosition.x * 10f))
                    .Distinct().Count() == row.Count;
            });

            return RenderModeResolver.Resolve(RenderMode.Auto, hasLayout, hasDistinctHorizontalSlots);
        }

        private static bool HasLayoutGroup(Transform parent)
        {
            if (parent == null) return false;
            try
            {
                return parent.GetComponent<HorizontalLayoutGroup>() != null
                    || parent.GetComponent<VerticalLayoutGroup>() != null
                    || parent.GetComponent<GridLayoutGroup>() != null;
            }
            catch
            {
                return false;
            }
        }


        /// <summary>
        /// Sibling mode is intentionally conservative: every direct child must be one of the active
        /// Moodle nodes in this parent, and they must all belong to the same logical row. With gaps
        /// (decorations, inactive Destroy-pending nodes, another row, third-party UI children), a
        /// sequence of SetSiblingIndex calls can displace unrelated objects between interruption
        /// points. Anchored mode does not need this restriction because it does not mutate hierarchy.
        /// </summary>
        private static bool CanSafelyUseSiblingOrder(Transform parent, List<MoodleVisual> members)
        {
            if (parent == null || members == null || members.Count < 2) return false;
            if (members.Select(v => v.IsSide).Distinct().Count() != 1) return false;
            if (parent.childCount != members.Count) return false;

            for (int i = 0; i < members.Count; i++)
            {
                var child = parent.GetChild(i);
                if (child == null) return false;
                var found = false;
                for (int j = 0; j < members.Count; j++)
                {
                    if (members[j].Component == child)
                    {
                        found = true;
                        break;
                    }
                }
                if (!found) return false;
            }
            return true;
        }

        /// <summary>
        /// 安全拓扑下的 sibling 重排：父节点必须只包含同一行的活动 Moodle。
        /// </summary>
        private bool ApplySiblingOrder(Transform parent, List<MoodleVisual> members)
        {
            var orders = PlanRows(members);
            var expectedChildCount = parent.childCount;
            bool changed = false;

            // CanSafelyUseSiblingOrder 已保证这里没有非 Moodle/另一行/非激活直系子节点。
            // 因而按槽位递进只会重排这一行本身，不会把未知 UI 子对象卷入写操作。
            foreach (var kv in orders)
            {
                var rowMembers = members
                    .Where(v => v.IsSide == kv.Key && v.Component != null && v.Component.parent == parent)
                    .ToList();
                if (rowMembers.Count < 2) continue;

                var slots = rowMembers.Select(v => v.Component.GetSiblingIndex()).OrderBy(i => i).ToList();
                var order = kv.Value;
                for (int i = 0; i < slots.Count && i < order.Count; i++)
                {
                    if (order[i] < 0 || order[i] >= rowMembers.Count) continue;
                    var child = rowMembers[order[i]].Component;
                    if (child == null || child.parent != parent)
                    {
                        // Topology drift without a nested refresh callback still invalidates every
                        // precomputed sibling slot. Queue a fresh frame before abandoning this plan.
                        ScheduleAfterCurrentFrame();
                        return changed;
                    }

                    var slot = slots[i];
                    if (child.GetSiblingIndex() == slot) continue;
                    child.SetSiblingIndex(slot);
                    changed = true;

                    // A hierarchy write may synchronously invoke OnTransformChildrenChanged. If
                    // that caused a fresh Moodle rebuild, or the parent child count changed for any
                    // other reason, the precomputed plan is stale: stop now and let the queued
                    // next-frame scan rebuild the plan from reality.
                    if (_scheduler.HasPending) return changed;
                    if (parent == null || parent.childCount != expectedChildCount)
                    {
                        ScheduleAfterCurrentFrame();
                        return changed;
                    }
                }
            }
            return changed;
        }

        /// <summary>
        /// 行隔离的 anchoredPosition 槽位重排：交换行内槽位，不触碰 sibling 顺序。
        /// 槽位使用 AddMoodle 同帧已设置好的 x/y 位置；只写 x，保留 y
        /// （Moodle.Update 后续只影响 y 时不能重新覆盖 x）。
        /// </summary>
        private bool ApplyAnchoredSlots(List<MoodleVisual> members)
        {
            bool anyWrite = false;
            var orders = PlanRows(members);
            foreach (var kv in orders)
            {
                var rowMembers = members.Where(v => v.IsSide == kv.Key).OrderBy(v => v.SiblingIndex).ToList();
                if (rowMembers.Any(v => v.RectTransform == null)) continue;

                var slots = rowMembers.Select(v => v.OriginalAnchoredPosition).ToList();
                var order = kv.Value;
                for (int i = 0; i < order.Count && i < slots.Count; i++)
                {
                    var target = rowMembers[order[i]];
                    var slot = slots[i];
                    var current = target.RectTransform.anchoredPosition;
                    // 只写 x（槽位），保留 y；x 已正确时不做多余写入。
                    if (Mathf.Abs(current.x - slot.x) > 0.001f)
                    {
                        target.RectTransform.anchoredPosition = new Vector2(slot.x, current.y);
                        anyWrite = true;
                        if (_scheduler.HasPending) return anyWrite;
                    }
                }
            }
            return anyWrite;
        }

        /// <summary>
        /// 将扫描结果转换为行隔离排序决策输入。
        /// </summary>
        private IReadOnlyDictionary<bool, IReadOnlyList<int>> PlanRows(List<MoodleVisual> members)
        {
            var items = new List<MoodleRowItem>(members.Count);
            for (int i = 0; i < members.Count; i++)
            {
                items.Add(new MoodleRowItem
                {
                    RuntimeId = members[i].RuntimeId,
                    IsSide = members[i].IsSide,
                    OriginalIndex = i
                });
            }
            return MoodleSortPlanner.PlanRows(items, _plan);
        }

        /// <summary>
        /// 以当前出现的状态集合更新提醒引擎并分发消息。
        /// 除原有分发器外，还以 ReminderMessage + ReminderRenderContext 回调宿主
        /// （Plugin 用于透明提醒展示）。
        /// </summary>
        private void UpdatePresentSnapshot(List<MoodleVisual> visuals)
        {
            _lastPresentStates.Clear();
            _lastVisualSnapshot = visuals == null
                ? new List<MoodleVisual>()
                : new List<MoodleVisual>(visuals);
            foreach (var v in _lastVisualSnapshot)
            {
                if (!string.IsNullOrEmpty(v.RuntimeId)) _lastPresentStates.Add(v.RuntimeId);
            }
            _hasPresentSnapshot = true;
        }

        /// <summary>
        /// Tick the reminder state machine from the last UI-confirmed Moodle snapshot.
        /// The engine itself owns once/repeat cadence and never burst-catches up missed slots.
        /// </summary>
        private void RunRemindersSnapshot()
        {
            try
            {
                if (!_hasPresentSnapshot) return;
                var messages = _reminders.Update(_lastPresentStates, DateTimeOffset.UtcNow);
                foreach (var message in messages)
                {
                    _dispatcher.Dispatch(message);
                    if (_onReminder != null)
                    {
                        // Reuse the last UI-confirmed snapshot rather than rescanning the Unity
                        // hierarchy for every reminder emission. Refresh hooks are the source of
                        // truth for presence; timer ticks only advance cadence between them.
                        _onReminder(message, BuildContext(message, _lastVisualSnapshot));
                    }
                }
            }
            catch (Exception ex)
            {
                LogThrottled($"Reminder update failed: {ex.Message}");
            }
        }

        /// <summary>
        /// 为一条提醒消息构建渲染上下文：真实 runtime id、捕获显示名、强度与所属分组。
        /// 未找到匹配 visual 时回退到规则状态本身（仍可显示提醒）。
        /// </summary>
        private ReminderRenderContext BuildContext(ReminderMessage message, List<MoodleVisual> visuals)
        {
            var visual = visuals.FirstOrDefault(v => RuleMatches(message.State, v.RuntimeId));
            var runtimeId = visual != null ? visual.RuntimeId : message.State;
            var capture = visual != null ? visual.Capture : null;
            var groupName = _config != null ? _config.ResolveGroupName(runtimeId) : string.Empty;
            var stableBaseId = capture != null && !string.IsNullOrWhiteSpace(capture.IconId)
                ? MoodleIdentity.NormalizeRuntimeId(capture.IconId)
                : MoodleIdentity.PatternBaseId(message.State);
            return new ReminderRenderContext(
                runtimeId,
                capture != null ? capture.DisplayName : string.Empty,
                groupName,
                capture != null ? capture.Intensity : -1,
                stableBaseId);
        }

        /// <summary>
        /// 规则状态模式匹配（与 ReminderEngine 一致）：exact / 严重度族 # / legacy prefix * / 去末尾数字基础名。
        /// </summary>
        private static bool RuleMatches(string pattern, string state)
        {
            return !string.IsNullOrEmpty(pattern)
                && !string.IsNullOrEmpty(state)
                && StateMatcher.MatchesPattern(pattern, state);
        }

        /// <summary>
        /// 变更签名：实例 id + runtime id + 行 + sibling 顺序 + anchoredPosition.x（不含 y，避免闪烁误判）。
        /// 原版每次重建 GameObject 实例，故包含实例 id；刷新边界还会失效缓存，
        /// 确保每轮新节点立即获得排序，而布局已正确时不做多余写入。
        /// </summary>
        private static string BuildSignature(List<MoodleVisual> visuals)
        {
            var sb = new StringBuilder();
            foreach (var v in visuals.OrderBy(x => x.SiblingIndex))
            {
                sb.Append(v.Component != null ? v.Component.GetInstanceID() : 0).Append(':')
                  .Append(v.RuntimeId).Append(':')
                  .Append(v.IsSide ? 'S' : 'M').Append(':')
                  .Append(v.SiblingIndex).Append(':')
                  .Append(v.RectTransform != null ? Mathf.RoundToInt(v.RectTransform.anchoredPosition.x * 10f) : 0)
                  .Append('|');
            }
            return sb.ToString();
        }

        private sealed class MoodleVisual
        {
            public Transform Component;
            public RectTransform RectTransform;
            public string RuntimeId;
            public bool IsSide;
            public int SiblingIndex;
            public Vector2 OriginalAnchoredPosition;
            public MoodleCaptureMetadata Capture;
        }
    }
}
