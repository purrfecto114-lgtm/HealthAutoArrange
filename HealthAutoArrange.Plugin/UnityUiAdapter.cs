using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using HealthAutoArrange.Core;
using UnityEngine;
using UnityEngine.UI;

namespace HealthAutoArrange.Plugin
{
    /// <summary>
    /// Unity/游戏适配层（融合 MoodleSorter_Source 的运行时优势）：
    /// - AddMoodle 前缀捕获元数据（runtime id、图标、强度、显示名、critical、创建顺序、manager、行）；
    /// - 刷新边界后延迟一帧扫描/排序/提醒，避开 Unity 同帧延迟销毁/重建；
    /// - 按 manager.moodles 扫描 Moodle 组件，支持 main/side 行隔离；
    /// - Auto 渲染模式：LayoutGroup → anchoredPosition slots → sibling index；
    /// - 未知状态保持现有 End/Keep 策略（由 SortPlan 决定）；
    /// - 保留 BottomAlert/Log 提醒；所有 Unity 访问异常均隔离并记录。
    /// </summary>
    public sealed class UnityUiAdapter
    {
        private SortPlan _plan;
        private ReminderEngine _reminders;
        private readonly ReminderDispatcher _dispatcher;
        private readonly Action<string> _log;
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

        public UnityUiAdapter(
            SortPlan plan,
            ReminderEngine reminders,
            ReminderDispatcher dispatcher,
            Action<string> log,
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
                if (_manager != null) Scan(_manager);
            }
            catch (Exception ex)
            {
                _log?.Invoke($"State catalog refresh failed safely: {ex.Message}");
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
            if (_manager != null) _scheduler.OnGameRefreshCompleted();
        }

        /// <summary>
        /// 按当前配置请求一次重排：失效签名缓存并在下一帧处理。
        /// 禁用时不产生视觉修改。
        /// </summary>
        public void ForceResort()
        {
            if (!_runtime.Enabled) return;
            _lastSignature = string.Empty;
            _scheduler.OnGameRefreshCompleted();
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
                _log?.Invoke($"AddMoodle capture failed: {ex.Message}");
            }
        }

        /// <summary>
        /// UpdateMoodles 后置调用：刷新完成边界。
        /// 安排下一帧扫描/排序/提醒，避免同一刷新帧仍包含待销毁旧节点。
        /// </summary>
        public void OnMoodlesUpdated(MoodleManager manager)
        {
            if (manager == null) return;
            _manager = manager;

            // AddMoodle calls for this refresh have already happened before this postfix. Move the
            // metadata window forward without clearing the registry in a prefix: CUCoreLib itself
            // injects custom moodles from an AddAllMoodles prefix, so a competing prefix clear would
            // be Harmony-order sensitive and could erase valid third-party captures.
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

            // 同帧执行扫描/排序：postfix 仍在游戏刷新方法栈内，渲染发生在帧末，
            // 此时 SetSiblingIndex 会在本帧渲染时生效，避免新图标先以默认顺序显示一帧（闪烁覆盖）。
            // 失败路径（manager 为空/扫描异常）由 ProcessRefresh 内部重新安排到下一帧兜底。
            if (_scheduler.TryRunNow() == SortDispatchDecision.RunNow)
            {
                ProcessRefresh();
            }
        }

        /// <summary>
        /// 每帧调用：处理上一帧推迟的挂起任务，并低频推进提醒计时。
        /// 不每帧扫描/强制写位置；排序禁用时仍保持状态观察与已启用提醒。
        /// </summary>
        public void Update()
        {
            if (_scheduler.TryDeferred())
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
        /// 无法立即处理时安排下一帧重试；所有 Unity 访问异常均隔离并记录。
        /// </summary>
        private void ProcessRefresh()
        {
            if (_manager == null)
            {
                _scheduler.OnRefreshCompleted(canRunNow: false);
                return;
            }

            List<MoodleVisual> visuals;
            try
            {
                visuals = Scan(_manager);
            }
            catch (Exception ex)
            {
                _log?.Invoke($"Moodle scan failed: {ex.Message}");
                _scheduler.OnRefreshCompleted(canRunNow: false);
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
                _lastSignature = BuildSignature(Scan(_manager));
            }
            catch (Exception ex)
            {
                _log?.Invoke($"Moodle sorting failed safely: {ex.Message}");
            }
        }

        /// <summary>
        /// F9 诊断入口：dump 当前 Moodle 的 id/基础名/显示名/行/强度/critical/创建顺序/位置。
        /// </summary>
        public void DumpDiagnostics()
        {
            try
            {
                _log?.Invoke("===== HealthAutoArrange diagnostic dump =====");
                _log?.Invoke($"Pending={_scheduler.HasPending}, Manager={(_manager != null ? _manager.name : "null")}");

                var visuals = _manager != null ? Scan(_manager) : new List<MoodleVisual>();
                if (visuals.Count == 0)
                {
                    _log?.Invoke("No Moodle components currently found.");
                    _log?.Invoke("===== end HealthAutoArrange dump =====");
                    return;
                }

                foreach (var v in visuals.OrderBy(x => x.SiblingIndex))
                {
                    var capture = v.Capture;
                    var pos = v.RectTransform != null ? v.RectTransform.anchoredPosition.ToString() : "n/a";
                    var diagnosticBaseId = capture != null && !string.IsNullOrWhiteSpace(capture.IconId)
                        ? MoodleIdentity.NormalizeRuntimeId(capture.IconId)
                        : MoodleIdentity.NormalizeRuntimeId(v.RuntimeId);
                    _log?.Invoke("id=" + v.RuntimeId
                        + " base=" + diagnosticBaseId
                        + " name='" + (capture != null ? capture.DisplayName : string.Empty) + "'"
                        + " row=" + (v.IsSide ? "side" : "main")
                        + " intensity=" + (capture != null ? capture.Intensity.ToString() : "unknown")
                        + " critical=" + (capture != null ? capture.Critical : false)
                        + " seq=" + (capture != null ? capture.Sequence : -1)
                        + " sibling=" + v.SiblingIndex
                        + " anchored=" + pos);
                }
                _log?.Invoke("===== end HealthAutoArrange dump =====");
            }
            catch (Exception ex)
            {
                _log?.Invoke($"Diagnostic dump failed: {ex.Message}");
            }
        }

        /// <summary>
        /// 扫描 manager.moodles 子节点中的 Moodle 组件。
        /// </summary>
        private List<MoodleVisual> Scan(MoodleManager manager)
        {
            var result = new List<MoodleVisual>();
            Transform container;
            try
            {
                container = manager.moodles;
            }
            catch (Exception ex)
            {
                _log?.Invoke($"MoodleManager.moodles access failed: {ex.Message}");
                return result;
            }
            if (container == null) return result;

            try
            {
                for (int i = 0; i < container.childCount; i++)
                {
                    var child = container.GetChild(i);
                    if (child == null) continue;
                    // 跳过非激活节点：游戏刷新帧内旧节点可能尚未销毁（Unity 延迟销毁），
                    // 同帧排序时避免 ghost 节点参与排序/观察。
                    if (!child.gameObject.activeInHierarchy) continue;
                    var moodle = child.GetComponent<Moodle>();
                    if (moodle == null) continue;

                    var runtimeId = moodle.type;
                    if (string.IsNullOrWhiteSpace(runtimeId)) runtimeId = child.name;
                    var rect = child as RectTransform;

                    var capture = _captures.Resolve(runtimeId, manager, _captureFloorSequence);
                    result.Add(new MoodleVisual
                    {
                        Component = child,
                        RectTransform = rect,
                        RuntimeId = runtimeId,
                        IsSide = moodle.isSide,
                        SiblingIndex = child.GetSiblingIndex(),
                        OriginalAnchoredPosition = rect != null ? rect.anchoredPosition : Vector2.zero,
                        Capture = capture
                    });
                    _observations.Observe(runtimeId, capture, moodle.isSide, DateTimeOffset.UtcNow);
                }
            }
            catch (Exception ex)
            {
                _log?.Invoke($"Moodle child enumeration failed: {ex.Message}");
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
                if (mode == RenderMode.AnchoredPosition)
                    ApplyAnchoredSlots(members);
                else
                    ApplySiblingOrder(parent, members);
                _log?.Invoke($"Arranged {members.Count} moodles ({mode}).");
            }
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
        /// 行隔离的 sibling 重排：仅移动本行成员，保持另一行位置。
        /// </summary>
        private void ApplySiblingOrder(Transform parent, List<MoodleVisual> members)
        {
            var orders = PlanRows(members);
            var desiredChildren = new List<Transform>();
            for (int i = 0; i < parent.childCount; i++) desiredChildren.Add(parent.GetChild(i));

            foreach (var kv in orders)
            {
                var rowMembers = members.Where(v => v.IsSide == kv.Key).ToList();
                var order = kv.Value;
                var slots = new List<int>();
                for (int i = 0; i < desiredChildren.Count; i++)
                {
                    if (rowMembers.Any(v => v.Component == desiredChildren[i])) slots.Add(i);
                }
                for (int i = 0; i < slots.Count && i < order.Count; i++)
                {
                    desiredChildren[slots[i]] = rowMembers[order[i]].Component;
                }
            }

            // 布局已正确时不做多余写入。
            bool changed = false;
            for (int i = 0; i < desiredChildren.Count; i++)
            {
                if (desiredChildren[i] != null && desiredChildren[i].GetSiblingIndex() != i)
                {
                    changed = true;
                    break;
                }
            }
            if (!changed) return;

            for (int i = 0; i < desiredChildren.Count; i++)
            {
                if (desiredChildren[i] != null) desiredChildren[i].SetSiblingIndex(i);
            }
        }

        /// <summary>
        /// 行隔离的 anchoredPosition 槽位重排：交换行内槽位，不触碰 sibling 顺序。
        /// 槽位使用 AddMoodle 同帧已设置好的 x/y 位置；只写 x，保留 y
        /// （Moodle.Update 后续只影响 y 时不能重新覆盖 x）。
        /// </summary>
        private void ApplyAnchoredSlots(List<MoodleVisual> members)
        {
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
                    }
                }
            }
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
                _log?.Invoke($"Reminder update failed: {ex.Message}");
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
