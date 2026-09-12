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
        private bool _processingRefresh;  // Reentrancy guard: prevents StackOverflow when
                                          // SetSiblingIndex triggers OnTransformChildrenChanged
                                          // → game Moodle refresh → OnMoodlesUpdated → ProcessRefresh
        private float _nextReminderTickRealtime;
        private int _captureFloorSequence;
        private readonly Dictionary<int, int> _captureBoundaryByManager = new Dictionary<int, int>();
        private const float ReminderTickSeconds = 0.25f;
        private const int MaxRememberedErrorMessages = 64;
        private readonly Dictionary<string, float> _lastErrorLogTime = new Dictionary<string, float>();

        // --- v1.2.0 multi-pronged refresh strategy -----------------------------------
        // Regression background: v1.1.3+ moved the sort into UpdateMoodles postfix (same-frame).
        // Under Casualties Unknown Demo 7.0.1 the game calls MoodleManager.Update 
        // every 0.5s; each call runs ClearMoodles (UnityEngine.Object.Destroy, deferred to end of
        // frame) + AddAllMoodles (creates new GameObjects). If the postfix sort is skipped or
        // operates on a half-destroyed hierarchy, the user sees the original game order
        // "stick" after the first cycle.
        //
        // New approach (v1.2.0):
        // 1. AddMoodle postfix: capture the just-created moodle's instance ID into a fresh set.
        //    This gives the scanner a precise "alive set" so it can skip Destroy-pending nodes
        //    even when Unity's fake-null check is ambiguous in the same frame.
        // 2. Plugin.Update periodic re-sort: a safety-net timer that fires ~4 Hz independently of
        //    the game's refresh cycle. If the UpdateMoodles postfix is dropped/skipped for any
        //    reason, the periodic check still re-sorts. The periodic pass runs on a *different*
        //    Unity frame than the game's ClearMoodles+AddAllMoodles, so Destroy has completed
        //    and the scan sees only live moodles.
        // 3. Scan filter: when the fresh set is populated (during an active refresh cycle),
        //    only children whose GetInstanceID() is in the set are admitted. This eliminates
        //    the "old + new moodles in the same scan" double-count bug.
        // -----------------------------------------------------------------------------
        private const float PeriodicResortIntervalSeconds = 0.25f;  // 4 Hz safety-net sort
        private float _nextPeriodicResortRealtime;
        private long _periodicResortCount;
        private long _addMoodlePostfixCount;
        // Per-manager "fresh instance id" set. Reset when the manager reference changes or
        // when an UpdateMoodles cycle completes. Populated by AddMoodle postfix.
        private readonly Dictionary<int, HashSet<int>> _freshInstanceIdsByManager = new Dictionary<int, HashSet<int>>();
        private int _freshManagerKey;  // current manager's GetInstanceID(), 0 if none

        // v1.2.1 diagnostics: how many times we fell back from SiblingOrder to
        // AnchoredPosition because CanSafelyUseSiblingOrder failed (e.g., bonus
        // moodle present, mixed main+side rows, or extra non-Moodle children).
        // F9 dump prints this so users can confirm the fallback path is firing.
        private long _siblingSortFallbackCount;
        private long _anchoredSortWriteCount;

        // --- v1.2.2 creation-time positioning + per-frame watchdog + ghost-proof scans ---
        // Root cause addressed (user report: "the two orders still take turns, just less
        // often"): v1.2.1 hooked only UpdateMoodles and corrected positions AFTER THE FACT
        // (same-frame postfix + 4 Hz net). Three real defects combined into the visible
        // alternation:
        //   (a) Any rebuild path that did not go through UpdateMoodles rendered the game's
        //       source order until the next correction (up to 250 ms with the 4 Hz net).
        //   (b) The 4 Hz net could fire in the same frame as a rebuild, after the fresh-set
        //       had been consumed: the scan then saw the destroy-pending OLD children as
        //       live members (Object.Destroy defers removal to end of frame, so Unity's
        //       fake-null check cannot filter them), duplicating slot values and writing
        //       overlapping/offset layouts every cycle.
        //   (c) AddMoodle no-ops (chippedOnly && WorldGeneration.unchipped) made the
        //       unconditional "last child" fresh-id record admit a ghost from the previous
        //       cycle.
        // v1.2.2 makes the mod's order authoritative at creation time and verifies it
        // every frame:
        //   1. AddMoodle postfix repositions the just-created moodle immediately
        //      (incremental re-sort of this cycle's members; anchoredPosition.x only, the
        //      pop-in animation on y is untouched). Rendering happens at frame end, so the
        //      game's creation order is never displayed - regardless of which rebuild path
        //      is running.
        //   2. AddAllMoodles postfix (new lowest-level boundary) finalizes the full sort
        //      the same frame; UpdateMoodles postfix dedupes against it per manager+frame.
        //   3. A per-frame watchdog verifies cached (rect, expected x) pairs. Any drift -
        //      unknown reposition path, rebuild, piecemeal add - triggers a full re-sort
        //      within one frame (<= 16 ms at 60 fps).
        //   4. All scans are ghost-proof: while a rebuild is in progress this frame, scans
        //      either use the fresh-instance filter or are deferred (frame gate).
        // -----------------------------------------------------------------------------
        private readonly List<MoodleVisual> _cycleMembers = new List<MoodleVisual>();
        private int _cycleManagerKey;
        private int _freshInstanceFrame;  // frame in which the fresh set was last populated
        private int _lastClearFrame = -1;  // frame of the most recent ClearMoodles postfix
        private int _lastClearManagerKey;
        private int _lastFinalizeFrame = -1;  // v1.2.2: same-frame finalize dedupe (per manager)
        private int _lastFinalizeManagerKey;
        private readonly List<RectTransform> _watchdogRects = new List<RectTransform>();
        private readonly List<float> _watchdogExpectedX = new List<float>();
        private Transform _watchdogContainer;
        private int _watchdogChildCount = -1;
        private long _creationTimePositionCount;  // diagnostics: creation-time incremental writes
        private long _watchdogCorrectionCount;    // diagnostics: watchdog-triggered full resorts (UNEXPECTED drift)
        private long _watchdogFinalizeCount;      // v1.2.3 diagnostics: expected post-rebuild finalize passes (silent)
        private long _moodlesClearedCount;        // diagnostics: ClearMoodles postfix invocations
        private long _ghostsHiddenCount;          // v1.2.3 diagnostics: destroy-pending icons hidden at clear time
        private long _preFadeCount;               // v1.2.3 diagnostics: creation-frame Start-color pre-applies
        private bool _watchdogPlanIncomplete;     // v1.2.3: plan built during a rebuild frame (childCount unknown)

        // v1.2.3 render-phase flash fixes (user report: "v1.2.2 没作用，还是会闪现").
        // The v1.2.2 fixes made the mod's x-slot order authoritative within the rebuild
        // frame, but TWO render-phase artifacts of the game's 0.5s full-rebuild cycle
        // remained visible (they are game behaviors, yet the mod can neutralize both):
        //   (a) ClearMoodles uses deferred Object.Destroy: the OLD icons stay in the
        //       hierarchy (and render) for the whole rebuild frame, so the user sees the
        //       old arrangement AND the new arrangement simultaneously for one frame -
        //       shifted/duplicated icons and ghosts of expired states. Fix: hide every
        //       destroy-pending child immediately in the ClearMoodles postfix; the game
        //       never touches them afterwards (decompile: no other caller, no OnDisable
        //       callbacks on Image/UITooltip/Moodle, no hierarchy-change callbacks).
        //   (b) AddMoodle creates the icon's Image components with Unity's default color
        //       (white, alpha 1). Moodle.Start() - which sets the intended fade-in alpha
        //       (unTransparentTime = 0.5 for newly-appearing states) - only runs on the
        //       NEXT frame, so every newly-appearing state renders one fully-opaque frame
        //       and then blinks to transparent before fading in. Fix: replicate Start's
        //       exact color formula in the AddMoodle postfix so the creation frame renders
        //       the correct fade-in state.
        // Both fixes run in the same frame as the rebuild; rendering happens at frame end.
        // Prefix stash used by OnMoodleCreated to detect AddMoodle no-op calls
        // (chippedOnly && WorldGeneration.unchipped creates nothing; the "last child"
        // is then a leftover, possibly a destroy-pending ghost from the previous cycle).
        private int _pendingAddManagerKey;
        private string _pendingAddExpectedType;
        private int _pendingAddChildCountBefore = -1;

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
            // v1.2.0: also clear the fresh-set so the next scan picks up the new plan
            // against the live hierarchy without stale instance-id filters.
            ClearFreshInstanceSet();
            // v1.2.2: the watchdog plan is bound to the previous plan's expected slots;
            // rebuild it from the next sort pass.
            InvalidateWatchdog();

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
                // v1.2.2: stash the expected post-conditions of THIS call so the postfix can
                // detect the no-op path (AddMoodle returns without creating a child when
                // chippedOnly && WorldGeneration.unchipped). The game sets moodle.type =
                // icon + intensity on creation; childCount grows by exactly one.
                _pendingAddExpectedType = (icon ?? string.Empty) + intensity.ToString();
                _pendingAddChildCountBefore = -1;
                _pendingAddManagerKey = 0;
                try
                {
                    var moodles = manager != null ? manager.moodles : null;
                    if (moodles != null)
                    {
                        _pendingAddChildCountBefore = moodles.childCount;
                        _pendingAddManagerKey = manager.GetInstanceID();
                    }
                }
                catch
                {
                    // Manager state unavailable: leave the stash invalid; OnMoodleCreated
                    // then falls back to trusting the last child (v1.2.1 behavior).
                }
            }
            catch (Exception ex)
            {
                LogThrottled($"AddMoodle capture failed: {ex.Message}");
            }
        }

        /// <summary>
        /// v1.2.0: AddMoodle postfix callback. Records the just-created moodle's
        /// Transform instance ID into a per-manager "fresh set". The next Scan
        /// (invoked from OnMoodlesUpdated or the periodic safety-net timer) uses
        /// this set to filter out Destroy-pending nodes left over by ClearMoodles.
        /// </summary>
        public void OnMoodleCreated(MoodleManager manager)
        {
            _addMoodlePostfixCount++;
            try
            {
                if (manager == null) { ClearPendingAdd(); return; }
                Transform moodles;
                try { moodles = manager.moodles; }
                catch { ClearPendingAdd(); return; }
                if (moodles == null) { ClearPendingAdd(); return; }

                int key;
                try { key = manager.GetInstanceID(); }
                catch { ClearPendingAdd(); return; }
                if (key == 0) { ClearPendingAdd(); return; }

                // The just-created moodle is the last child of manager.moodles.
                int lastIdx = moodles.childCount - 1;
                if (lastIdx < 0) { ClearPendingAdd(); return; }
                var newChild = moodles.GetChild(lastIdx);
                if (newChild == null) { ClearPendingAdd(); return; }  // already destroyed by another Mod

                // v1.2.2: validate that AddMoodle actually created a child on THIS call.
                // AddMoodle no-ops when chippedOnly && WorldGeneration.unchipped; the last
                // child is then a leftover from a previous call (possibly a destroy-pending
                // ghost). v1.2.1 recorded it unconditionally, which could admit a ghost
                // into the fresh set and misalign slot assignment for the whole cycle.
                bool created;
                if (_pendingAddManagerKey == key && _pendingAddChildCountBefore >= 0)
                {
                    created = moodles.childCount == _pendingAddChildCountBefore + 1;
                    if (created)
                    {
                        var candidate = newChild.GetComponent<Moodle>();
                        created = candidate != null && candidate.type == _pendingAddExpectedType;
                    }
                }
                else
                {
                    // Prefix stash unavailable (patch pair split): fall back to v1.2.1
                    // behavior of trusting the last child.
                    created = true;
                }
                ClearPendingAdd();
                if (!created) return;

                int childId;
                try { childId = newChild.GetInstanceID(); }
                catch { return; }
                if (childId == 0) return;

                // v1.2.0: if the manager changed, start a new fresh set. This guards
                // against stale instance IDs from a recycled manager.
                if (_freshManagerKey != 0 && _freshManagerKey != key)
                {
                    _freshInstanceIdsByManager.Clear();
                }
                _freshManagerKey = key;
                _freshInstanceFrame = Time.frameCount;

                if (!_freshInstanceIdsByManager.TryGetValue(key, out var set) || set == null)
                {
                    set = new HashSet<int>();
                    _freshInstanceIdsByManager[key] = set;
                }
                set.Add(childId);

                // v1.2.2 creation-time positioning: while a rebuild is running in this
                // frame (ClearMoodles postfix fired), the cycle list holds every moodle
                // created this cycle, so an incremental re-sort can place each moodle at
                // its mod slot IMMEDIATELY - before the frame is rendered. Piecemeal adds
                // outside a rebuild (not observed in the decompiled game, but kept safe)
                // skip this and rely on the per-frame watchdog, because a partial cycle
                // list must never be used to write slots (it could overlap pre-existing
                // moodles that are not part of the cycle).
                var createdMoodle = newChild.GetComponent<Moodle>();

                // v1.2.3 render-phase fix (b): replicate Moodle.Start()'s color
                // initialization NOW (the creation frame) instead of one frame later.
                // AddMoodle builds the icon with Unity's default Image color (white,
                // alpha 1); Start only runs next frame, so a newly-appearing state (its
                // fade-in alpha is 0 at creation) rendered one fully-opaque frame first.
                if (_runtime.Enabled) PrefadeNewMoodle(newChild, createdMoodle);

                if (_runtime.Enabled && _lastClearFrame == Time.frameCount && _lastClearManagerKey == key)
                {
                    TryPositionCycleMembers(key, newChild, createdMoodle);
                }
            }
            catch (Exception ex)
            {
                LogThrottled($"OnMoodleCreated failed: {ex.Message}");
            }
        }

        /// <summary>
        /// v1.2.3 render-phase fix (b): pre-apply the colors Moodle.Start() will set next
        /// frame, using the game's own formula (decompiled 7.0.1):
        ///   img  .color = (1, 1, 1, ((PlayerCamera.main.blackAmount &lt; PlayerCamera.GetUnconsciousBlack()) ? 1 : 0) - unTransparentTime * 2)
        ///   img2 .color = (1, 1, 1, 1 - unTransparentTime * 2)
        /// where img is the root background Image and img2 the "MoodleInside" foreground.
        /// PlayerCamera.blackAmount and GetUnconsciousBlack() are public in the game
        /// assembly; on any access failure we fall back to the conscious-case values.
        /// </summary>
        private void PrefadeNewMoodle(Transform newChild, Moodle moodle)
        {
            try
            {
                if (newChild == null) return;
                var img = newChild.GetComponent<Image>();
                Transform inside = newChild.childCount > 0 ? newChild.GetChild(0) : null;
                var img2 = inside != null ? inside.GetComponent<Image>() : null;
                if (img == null || img2 == null) return;

                float unTransparentTime = moodle != null ? moodle.unTransparentTime : 0f;
                float backgroundAlpha;
                try
                {
                    var camera = PlayerCamera.main;
                    backgroundAlpha = (camera != null && camera.blackAmount < PlayerCamera.GetUnconsciousBlack())
                        ? 1f - unTransparentTime * 2f
                        : 0f - unTransparentTime * 2f;
                }
                catch
                {
                    // PlayerCamera unavailable (scene transition): conscious-case values.
                    backgroundAlpha = 1f - unTransparentTime * 2f;
                }
                img.color = new Color(1f, 1f, 1f, backgroundAlpha);
                img2.color = new Color(1f, 1f, 1f, 1f - unTransparentTime * 2f);
                _preFadeCount++;
            }
            catch (Exception ex)
            {
                LogThrottled($"Pre-fade failed: {ex.Message}");
            }
        }

        /// <summary>
        /// v1.2.2: ClearMoodles postfix callback. ClearMoodles runs at the START of every
        /// rebuild cycle: old nodes are destroy-pending (Object.Destroy removes them at the
        /// end of the frame) and new nodes do not exist yet. Reset the per-cycle
        /// accumulation and the watchdog plan so the subsequent AddMoodle postfixes rebuild
        /// both from zero. Also marks the frame as "rebuild in progress" for the scan gate.
        /// </summary>
        public void OnMoodlesCleared(MoodleManager manager)
        {
            _moodlesClearedCount++;
            try
            {
                if (manager == null) return;
                int key;
                try { key = manager.GetInstanceID(); }
                catch { key = 0; }
                _lastClearFrame = Time.frameCount;
                _lastClearManagerKey = key;

                // Bind the manager early so the watchdog/periodic paths can act on the
                // manager that is about to rebuild, even before the finalize boundary.
                if (key != 0 && !ReferenceEquals(_manager, manager))
                {
                    _captureBoundaryByManager.Clear();
                    _captureFloorSequence = 0;
                    _manager = manager;
                }

                _cycleMembers.Clear();
                _cycleManagerKey = 0;
                ClearFreshInstanceSet();
                InvalidateWatchdog();

                // v1.2.3: kill the rebuild-frame double-render (see HidePendingGhosts).
                // Gated on the master toggle: with the mod disabled the user gets pure
                // vanilla rendering, exactly as the GUI promises.
                if (_runtime.Enabled) HidePendingGhosts(manager);
            }
            catch (Exception ex)
            {
                LogThrottled($"OnMoodlesCleared failed: {ex.Message}");
            }
        }

        /// <summary>
        /// v1.2.3 render-phase fix (a): hide every destroy-pending child of the moodles
        /// container right after ClearMoodles returns. Object.Destroy is deferred to the
        /// end of the frame, so without this the OLD icons render one more frame and the
        /// rebuild frame shows BOTH arrangements overlapping ("闪现"). Deactivating them
        /// is safe: the game itself destroyed them already, never touches them again, and
        /// none of their components (Image / UITooltip / Moodle) has OnDisable logic.
        /// The mod's own Scan skips inactive children anyway; Destroy still completes.
        /// </summary>
        private void HidePendingGhosts(MoodleManager manager)
        {
            try
            {
                Transform moodles;
                try { moodles = manager != null ? manager.moodles : null; }
                catch { return; }
                if (moodles == null) return;
                int hidden = 0;
                for (int i = 0; i < moodles.childCount; i++)
                {
                    var child = moodles.GetChild(i);
                    if (child == null) continue;
                    if (!child.gameObject.activeSelf) continue;
                    try
                    {
                        child.gameObject.SetActive(false);
                        hidden++;
                    }
                    catch
                    {
                        // A child that cannot be deactivated is destroyed at frame end
                        // anyway; skipping it changes nothing semantically.
                    }
                }
                if (hidden > 0) _ghostsHiddenCount += hidden;
            }
            catch (Exception ex)
            {
                LogThrottled($"Ghost-hide failed: {ex.Message}");
            }
        }

        /// <summary>Clear the prefix stash used to validate OnMoodleCreated.</summary>
        private void ClearPendingAdd()
        {
            _pendingAddManagerKey = 0;
            _pendingAddExpectedType = null;
            _pendingAddChildCountBefore = -1;
        }

        /// <summary>
        /// v1.2.2 creation-time positioning: incremental re-sort of the current cycle's
        /// members. Called from OnMoodleCreated while a rebuild is in progress, i.e. inside
        /// the game's own AddMoodle call stack. Writes anchoredPosition.x only - the
        /// game's pop-in animation (y offset + scale) is left untouched. Slots are the
        /// ascending set of the row's current x positions: the game assigns every new
        /// moodle a fresh sequential slot, and permuting members among their own slots
        /// keeps the slot set invariant, so the final layout after the last AddMoodle of
        /// the cycle is exactly the mod order. Rendering happens at frame end, so the
        /// user never sees the game's creation order.
        /// </summary>
        private void TryPositionCycleMembers(int managerKey, Transform newChild, Moodle newMoodle)
        {
            try
            {
                var rect = newChild as RectTransform;
                if (rect == null || newMoodle == null) return;
                var runtimeId = newMoodle.type;
                if (string.IsNullOrWhiteSpace(runtimeId)) runtimeId = newChild.name;

                if (_cycleManagerKey != 0 && _cycleManagerKey != managerKey)
                {
                    _cycleMembers.Clear();
                }
                _cycleManagerKey = managerKey;
                _cycleMembers.Add(new MoodleVisual
                {
                    Component = newChild,
                    RectTransform = rect,
                    RuntimeId = runtimeId,
                    IsSide = newMoodle.isSide,
                    SiblingIndex = newChild.GetSiblingIndex(),
                    OriginalAnchoredPosition = rect.anchoredPosition,
                    Capture = null
                });
                if (_cycleMembers.Count < 2) return;

                var orders = PlanRows(_cycleMembers);
                bool anyWrite = false;
                foreach (var kv in orders)
                {
                    var rowMembers = _cycleMembers
                        .Where(v => v.IsSide == kv.Key)
                        .OrderBy(v => v.SiblingIndex)
                        .ToList();
                    if (rowMembers.Any(v => v.RectTransform == null)) continue;

                    var slots = rowMembers.Select(v => v.RectTransform.anchoredPosition.x).OrderBy(x => x).ToList();
                    var order = kv.Value;
                    for (int i = 0; i < order.Count && i < slots.Count; i++)
                    {
                        var target = rowMembers[order[i]];
                        var current = target.RectTransform.anchoredPosition;
                        if (Mathf.Abs(current.x - slots[i]) > 0.001f)
                        {
                            target.RectTransform.anchoredPosition = new Vector2(slots[i], current.y);
                            anyWrite = true;
                        }
                    }
                }
                if (anyWrite) _creationTimePositionCount++;
            }
            catch (Exception ex)
            {
                LogThrottled($"Creation-time positioning failed: {ex.Message}");
            }
        }

        /// <summary>
        /// v1.2.2 per-frame position watchdog: verifies the cached (rect, expected x)
        /// plan against the live hierarchy every frame. Any drift - an unknown game
        /// reposition path, a rebuild we did not see, a piecemeal add/remove - triggers a
        /// full re-scan and re-sort within the same frame. Steady state is ~a dozen null
        /// checks and float compares per frame; no writes happen when positions match.
        /// </summary>
        private void RunWatchdog()
        {
            if (_watchdogRects.Count == 0) return;
            if (_lastClearFrame == Time.frameCount) return;  // rebuild in progress this frame
            if (_processingRefresh) return;

            Transform container;
            try { container = _manager.moodles; }
            catch { InvalidateWatchdog(); return; }
            if (container == null || !ReferenceEquals(container, _watchdogContainer))
            {
                RequestFullRefresh("container changed");
                return;
            }
            if (container.childCount != _watchdogChildCount)
            {
                if (_watchdogPlanIncomplete)
                {
                    // v1.2.3: expected, healthy path. The plan was built during a rebuild
                    // frame while destroy-pending ghosts were still counted, so the child
                    // count is intentionally unknown (-1). This pass finalizes the plan on
                    // the first clean frame. Not a warning: the old log spammed
                    // "child count drift" ~1x per 0.5s rebuild and mislead users (and us)
                    // into hunting a defect that is by-design.
                    _watchdogFinalizeCount++;
                    _lastSignature = string.Empty;
                    _log?.Invoke(LogLevel.Debug, "Watchdog finalize pass (expected after rebuild).");
                    ProcessRefresh();  // re-entrancy guarded; rebuild frame is over
                    return;
                }
                RequestFullRefresh("child count drift");
                return;
            }
            for (int i = 0; i < _watchdogRects.Count; i++)
            {
                var rect = _watchdogRects[i];
                if (rect == null)
                {
                    // Destroyed since the plan was built (fake-null): full refresh.
                    RequestFullRefresh("member destroyed");
                    return;
                }
                var currentX = rect.anchoredPosition.x;
                if (Mathf.Abs(currentX - _watchdogExpectedX[i]) > 0.5f)
                {
                    RequestFullRefresh("position drift");
                    return;
                }
            }
        }

        private void RequestFullRefresh(string reason)
        {
            _watchdogCorrectionCount++;
            _lastSignature = string.Empty;
            LogThrottled($"Watchdog full refresh: {reason}");
            ProcessRefresh();  // re-entrancy guarded; Scan is ghost-proof via the frame gate
        }

        /// <summary>
        /// Rebuild the watchdog plan from a completed sort pass. <paramref name="complete"/>
        /// is false when the scan ran with the fresh-instance filter during a rebuild
        /// frame: the container's childCount then still includes destroy-pending ghosts,
        /// so the count is marked unknown (-1) and the first clean-frame watchdog pass
        /// triggers one full refresh to finalize the plan.
        /// </summary>
        private void RebuildWatchdogPlan(List<MoodleVisual> visuals, bool complete)
        {
            _watchdogRects.Clear();
            _watchdogExpectedX.Clear();
            _watchdogContainer = null;
            _watchdogChildCount = -1;
            _watchdogPlanIncomplete = !complete;
            try
            {
                if (_manager == null) return;
                var container = _manager.moodles;
                if (container == null) return;
                _watchdogContainer = container;
                _watchdogChildCount = complete ? container.childCount : -1;
                foreach (var v in visuals)
                {
                    if (v.RectTransform == null) continue;
                    _watchdogRects.Add(v.RectTransform);
                    _watchdogExpectedX.Add(v.RectTransform.anchoredPosition.x);
                }
            }
            catch
            {
                InvalidateWatchdog();
            }
        }

        private void InvalidateWatchdog()
        {
            _watchdogRects.Clear();
            _watchdogExpectedX.Clear();
            _watchdogContainer = null;
            _watchdogChildCount = -1;
            _watchdogPlanIncomplete = false;
        }

        /// <summary>
        /// v1.2.0: drop the fresh-instance set. Called after ProcessRefresh in
        /// OnMoodlesUpdated (the set has been consumed by the Scan) and also on
        /// manager loss / Reconfigure so the periodic safety-net sort sees a clean
        /// hierarchy without stale entries.
        /// </summary>
        private void ClearFreshInstanceSet()
        {
            _freshManagerKey = 0;
            _freshInstanceIdsByManager.Clear();
            _freshInstanceFrame = 0;
        }

        /// <summary>
        /// UpdateMoodles 后置调用：刷新完成边界。
        /// 安排下一帧扫描/排序/提醒，避免同一刷新帧仍包含待销毁旧节点。
        /// </summary>
        public void OnMoodlesUpdated(MoodleManager manager)
        {
            if (manager == null) return;

            // Reentrancy guard: if ProcessRefresh (called below via TryRunNow) synchronously
            // triggers SetSiblingIndex → OnTransformChildrenChanged → game Moodle refresh →
            // OnMoodlesUpdated again, the recursive call must not re-enter the sort path.
            // The inner call still updates the capture boundary (metadata only) but skips
            // the same-frame sort, breaking the recursion chain.
            if (_processingRefresh)
            {
                UpdateCaptureBoundary(manager);
                return;
            }

            // v1.2.2: same-frame, same-manager finalize dedupe. UpdateMoodles calls
            // ClearMoodles + AddAllMoodles, so BOTH postfixes funnel here within one
            // rebuild: AddAllMoodles postfix arrives first and finalizes; the UpdateMoodles
            // postfix must not rescan - at that point the fresh-instance filter has been
            // consumed and the still-destroy-pending old children would be admitted as
            // ghost members, misaligning the slot assignment every cycle.
            int finalizeKey;
            try { finalizeKey = manager.GetInstanceID(); }
            catch { finalizeKey = 0; }
            if (finalizeKey != 0
                && finalizeKey == _lastFinalizeManagerKey
                && _lastFinalizeFrame == Time.frameCount)
            {
                UpdateCaptureBoundary(manager);
                return;
            }
            _lastFinalizeManagerKey = finalizeKey;
            _lastFinalizeFrame = Time.frameCount;

            // v1.2.2: mark this frame as a rebuild frame for the scan gate. AddMoodle
            // postfixes have populated the fresh set during AddAllMoodles, i.e. new nodes
            // exist and destroy-pending ghosts are still in the hierarchy. Even when the
            // ClearMoodles postfix itself is unavailable (degraded patch set), this keeps
            // every later same-frame scan (4 Hz net, watchdog, F8/F9) from admitting
            // ghosts once the finalize consumes the fresh set.
            if (_freshManagerKey != 0
                && _freshInstanceIdsByManager.TryGetValue(_freshManagerKey, out var rebuildSet)
                && rebuildSet != null && rebuildSet.Count > 0)
            {
                _lastClearFrame = Time.frameCount;
                _lastClearManagerKey = finalizeKey;
            }

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

            UpdateCaptureBoundary(manager);

            // v1.2.0: bind the fresh-instance filter to the current manager. AddMoodle
            // postfix has been populating this set during AddAllMoodles (which runs
            // BEFORE this UpdateMoodles postfix). The Scan below will use this set to
            // skip Destroy-pending nodes from ClearMoodles.
            try { _freshManagerKey = manager.GetInstanceID(); }
            catch { _freshManagerKey = 0; }

            // 刷新边界：即使关闭排序，也继续观察 Moodle 并驱动已启用的提醒；
            // 只有实际 UI 重排受主开关控制。
            if (_runtime.Enabled) _lastSignature = string.Empty;

            // 同帧执行扫描/排序：postfix 仍在游戏刷新方法栈内，渲染发生在帧末，
            // 此时 SetSiblingIndex 在本帧渲染时生效，避免新图标先以默认顺序显示一帧
            // （v1.1.5 v2 引入的跨帧排序是 1.1.4 不存在的回归 —— 状态栏会闪烁，交替
            // 显示排列前/排列后顺序）。失败路径（manager 为空 / 扫描异常 / 层级不稳定）
            // 由 ProcessRefresh 内部不再重试 + 下一次游戏刷新触发兜底；ApplySiblingOrder
            // 检测到 topology drift 时仍会主动 ScheduleAfterCurrentFrame（保留 v2 的
            // nested-refresh / sibling 拓扑保护，仅恢复主路径的同帧语义）。
            if (_scheduler.TryRunNow() == SortDispatchDecision.RunNow)
            {
                ProcessRefresh();
            }

            // v1.2.0: clear the fresh-instance set now that ProcessRefresh has consumed
            // it. The next AddMoodle cycle (next UpdateMoodles call ~0.5s later) will
            // rebuild the set via AddMoodle postfix. Clearing here prevents stale
            // instance IDs from leaking into the periodic safety-net sort.
            ClearFreshInstanceSet();
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

            // v1.2.2: the fresh-instance set is only meaningful within the rebuild frame it
            // was populated in. If a rebuild finalized without consuming it (piecemeal
            // AddMoodle without AddAllMoodles, or a degraded patch set), drop it as soon as
            // the frame rolls over: ghosts are gone by then and an unfiltered scan is safe,
            // while a stale filter would hide live moodles from later scans.
            if (_freshManagerKey != 0
                && _freshInstanceFrame != 0
                && Time.frameCount > _freshInstanceFrame)
            {
                ClearFreshInstanceSet();
            }

            if (_scheduler.TryDeferred(Time.frameCount))
            {
                ProcessRefresh();
            }

            // v1.2.2 per-frame position watchdog: runs before the periodic net so drift is
            // corrected within one frame (<= 16 ms at 60 fps) instead of waiting for the
            // 0.25 s periodic timer.
            try
            {
                if (_runtime.Enabled
                    && !ReferenceEquals(_manager, null)
                    && _manager != null)
                {
                    RunWatchdog();
                }
            }
            catch (Exception ex)
            {
                LogThrottled($"Watchdog failed: {ex.Message}");
            }

            // v1.2.0 periodic safety-net re-sort. Independent of the game's
            // UpdateMoodles cycle (which fires ~ every 0.5s and may be skipped or
            // operate on a half-destroyed hierarchy). The periodic pass runs on
            // every Plugin.Update tick where the timer has elapsed AND the manager
            // is alive AND sorting is enabled. Because Plugin.Update typically fires
            // on a different Unity frame than the game's ClearMoodles+AddAllMoodles,
            // Destroy has completed by the time we scan here, so the fresh-set
            // filter (if still populated) plus the child == null filter give us a
            // clean view of only the live moodles. This is what fixes the v1.1.x
            // regression where the sort only stuck at startup.
            try
            {
                if (_runtime.Enabled
                    && !ReferenceEquals(_manager, null)
                    && _manager != null
                    && Time.realtimeSinceStartup >= _nextPeriodicResortRealtime)
                {
                    _nextPeriodicResortRealtime = Time.realtimeSinceStartup + PeriodicResortIntervalSeconds;
                    _periodicResortCount++;
                    // Invalidate the signature so ProcessRefresh actually re-evaluates,
                    // even if the previous UpdateMoodles postfix already cached it.
                    _lastSignature = string.Empty;
                    // Use TryRunNow so the periodic pass runs immediately even when the
                    // scheduler has no pending task (the common case between game
                    // refreshes). The re-entrancy guard inside ProcessRefresh handles
                    // the rare case where OnMoodlesUpdated is still on the stack.
                    _scheduler.TryRunNow();
                    ProcessRefresh();
                    // The periodic pass consumes the fresh set; clear it so the next
                    // cycle starts clean.
                    ClearFreshInstanceSet();
                }
            }
            catch (Exception ex)
            {
                LogThrottled($"Periodic safety-net re-sort failed: {ex.Message}");
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
            // Reentrancy guard: when OnMoodlesUpdated calls ProcessRefresh synchronously
            // (same-frame sort), SetSiblingIndex inside ApplySort can synchronously trigger
            // OnTransformChildrenChanged → game Moodle refresh → OnMoodlesUpdated → ProcessRefresh.
            // Without this guard, the recursive call would re-enter ApplySort on a stale
            // hierarchy and risk StackOverflow (which would crash the plugin and disable
            // F8/F9 and all other functionality). The recursive call is dropped; the
            // outer call completes its sort and the next game refresh will re-evaluate.
            if (_processingRefresh) return;
            _processingRefresh = true;
            try
            {
                ProcessRefreshCore();
            }
            finally
            {
                _processingRefresh = false;
            }
        }

        private void ProcessRefreshCore()
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

            if (!_runtime.Enabled || visuals.Count < 1) return;

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
                // v1.2.2: the post-sort rescan feeds both the change signature and the
                // per-frame watchdog plan. When this scan ran with the fresh-instance filter
                // (rebuild frame), childCount still includes ghosts, so the plan is marked
                // incomplete and finalized by the first clean-frame watchdog pass.
                var postSortVisuals = Scan(_manager);
                _lastSignature = BuildSignature(postSortVisuals);
                bool scanWasFiltered = _freshManagerKey != 0
                    && _freshInstanceIdsByManager.TryGetValue(_freshManagerKey, out var planSet)
                    && planSet != null && planSet.Count > 0;
                RebuildWatchdogPlan(postSortVisuals, complete: !scanWasFiltered);
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
            // v1.2.0: also drop the fresh-instance set so stale ids cannot leak
            // into the next manager's scan.
            ClearFreshInstanceSet();
            // v1.2.2: drop the cycle accumulation and watchdog plan bound to the lost manager.
            _cycleMembers.Clear();
            _cycleManagerKey = 0;
            _lastClearFrame = -1;
            _lastClearManagerKey = 0;
            _lastFinalizeFrame = -1;
            _lastFinalizeManagerKey = 0;
            InvalidateWatchdog();
        }

        /// <summary>
        /// 更新 AddMoodle 捕获窗口的 per-manager sequence 边界。
        /// 从 OnMoodlesUpdated 调用；也可从 reentrancy guard 的内层递归调用路径单独调用
        /// 以确保捕获边界元数据始终推进，即使主路径跳过了同帧排序。
        /// </summary>
        private void UpdateCaptureBoundary(MoodleManager manager)
        {
            var managerKey = manager.GetInstanceID();
            if (!_captureBoundaryByManager.TryGetValue(managerKey, out _captureFloorSequence))
                _captureFloorSequence = 0;
            _captureBoundaryByManager[managerKey] = _captures.LatestSequence;
            if (_captureBoundaryByManager.Count > 16)
            {
                var keep = _captureBoundaryByManager[managerKey];
                _captureBoundaryByManager.Clear();
                _captureBoundaryByManager[managerKey] = keep;
            }
        }

        /// <summary>
        /// F9 诊断入口（v1.2.3 全面刷新，与当前管线一致）：
        /// - 单一 stats 行（旧版 v1.2.1/v1.2.2 分版本统计已过时且互相漂移）；
        /// - frame/sort 行：看门狗计划状态、组内排序模式、未知状态策略；
        /// - 每条 moodle：id/基础名/显示名/行/分组/强度/critical/捕获序号/槽位/sibling/锚点位置；
        ///   强度优先取捕获，回退 runtime id 末尾数字（游戏 type = 图标名+强度，恒可解析）；
        ///   捕获解析不设 sequence 下限——F9 是手动读取当前屏幕状态，需要最新元数据，
        ///   旧版在刷新边界之后一律打印 intensity=unknown/name=''/seq=-1 即此缺陷。
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
                _log?.Invoke(LogLevel.Info, $"stats: AddMoodlePostfix={_addMoodlePostfixCount}, PeriodicResort={_periodicResortCount}, SiblingFallback={_siblingSortFallbackCount}, AnchoredWrite={_anchoredSortWriteCount}, MoodlesCleared={_moodlesClearedCount}, CreationTimePosition={_creationTimePositionCount}, GhostsHidden={_ghostsHiddenCount}, PreFade={_preFadeCount}, WatchdogFinalize(expected)={_watchdogFinalizeCount}, WatchdogCorrection(unexpected)={_watchdogCorrectionCount}, FreshSetSize={(_freshManagerKey != 0 && _freshInstanceIdsByManager.TryGetValue(_freshManagerKey, out var fs) ? fs.Count : 0)}");
                _log?.Invoke(LogLevel.Info, $"frame: Frame={Time.frameCount}, LastClearFrame={_lastClearFrame}, LastFinalizeFrame={_lastFinalizeFrame}, WatchdogPlanSize={_watchdogRects.Count}, WatchdogPlan={(_watchdogRects.Count == 0 ? "none" : (_watchdogPlanIncomplete ? "incomplete-pending-finalize" : "complete"))}");
                _log?.Invoke(LogLevel.Info, $"sort: Enabled={_runtime.Enabled}, InGroup={DescribeInGroupSort(_config)}, UnknownPolicy={(_config != null ? _config.UnknownStatePolicy.ToString() : "unknown")}");
                if (_manager != null)
                {
                    Transform moodlesContainer = null;
                    try { moodlesContainer = _manager.moodles; } catch { }
                    if (moodlesContainer != null)
                    {
                        _log?.Invoke(LogLevel.Info, $"Manager.moodles childCount={moodlesContainer.childCount}");
                        bool hasHLG = false, hasVLG = false, hasGLG = false;
                        try { hasHLG = moodlesContainer.GetComponent<HorizontalLayoutGroup>() != null; } catch { }
                        try { hasVLG = moodlesContainer.GetComponent<VerticalLayoutGroup>() != null; } catch { }
                        try { hasGLG = moodlesContainer.GetComponent<GridLayoutGroup>() != null; } catch { }
                        _log?.Invoke(LogLevel.Info, $"LayoutGroup on moodles container: H={hasHLG} V={hasVLG} G={hasGLG}");
                    }
                }
                var visuals = _manager != null ? Scan(_manager) : new List<MoodleVisual>();
                if (visuals.Count == 0)
                {
                    _log?.Invoke(LogLevel.Info, "No Moodle components currently found.");
                    _log?.Invoke(LogLevel.Info, "===== end HealthAutoArrange dump =====");
                    return;
                }

                foreach (var v in visuals.OrderBy(x => x.SiblingIndex))
                {
                    // v1.2.3: scan-time capture may be null because the refresh-boundary floor
                    // (correctly) excludes consumed captures; a manual dump wants the latest
                    // metadata, so resolve without the floor.
                    var capture = v.Capture ?? _captures.Resolve(v.RuntimeId, _manager, 0);
                    var pos = v.RectTransform != null ? v.RectTransform.anchoredPosition.ToString() : "n/a";
                    var diagnosticBaseId = capture != null && !string.IsNullOrWhiteSpace(capture.IconId)
                        ? MoodleIdentity.NormalizeRuntimeId(capture.IconId)
                        : MoodleIdentity.NormalizeRuntimeId(v.RuntimeId);
                    var intensity = capture != null
                        ? capture.Intensity
                        : MoodleIdentity.ParseTrailingIntensity(v.RuntimeId, -1);
                    var groupName = _config != null ? _config.ResolveGroupName(v.RuntimeId) : string.Empty;
                    _log?.Invoke(LogLevel.Info, "id=" + v.RuntimeId
                        + " base=" + diagnosticBaseId
                        + " name='" + (capture != null ? capture.DisplayName : string.Empty) + "'"
                        + " row=" + (v.IsSide ? "side" : "main")
                        + " group='" + groupName + "'"
                        + " intensity=" + (intensity >= 0 ? intensity.ToString() : "unknown")
                        + " critical=" + (capture != null && capture.Critical)
                        + " seq=" + (capture != null ? capture.Sequence : -1)
                        + " slot=" + SlotOf(visuals, v)
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

        /// <summary>v1.2.3：该 moodle 在其行内按 x 升序的槽位序号（0 起）；用于 F9 诊断。</summary>
        private static int SlotOf(List<MoodleVisual> visuals, MoodleVisual target)
        {
            if (target == null || target.RectTransform == null) return -1;
            int slot = 0;
            foreach (var v in visuals)
            {
                if (ReferenceEquals(v, target) || v.IsSide != target.IsSide || v.RectTransform == null) continue;
                if (v.RectTransform.anchoredPosition.x < target.RectTransform.anchoredPosition.x) slot++;
            }
            return slot;
        }

        /// <summary>v1.2.3：组内排序模式的人类可读描述（F9 诊断与启动日志共用）。</summary>
        public static string DescribeInGroupSort(ArrangeConfig config)
        {
            var mode = config != null ? config.InGroupSortMode : InGroupSortMode.RuleIndex;
            switch (mode)
            {
                case InGroupSortMode.IntensityDesc: return "IntensityDesc (strongest first)";
                case InGroupSortMode.IntensityAsc: return "IntensityAsc (weakest first)";
                default: return "RuleIndex (rules order)";
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

            // v1.2.2 ghost-proof scan gate: while a rebuild is in progress this frame,
            // ClearMoodles' Object.Destroy has not removed the old children yet (removal is
            // deferred to the end of the frame, so Unity's fake-null check cannot detect
            // them). Scanning without the fresh-instance filter would admit those ghosts
            // as live members, duplicating slot values and misaligning the sort. When the
            // fresh filter is not active, refuse to scan a hierarchy in a rebuild frame;
            // callers treat this like any other unstable-scan failure and retry on a clean
            // frame.
            bool freshFilterActive = _freshManagerKey != 0
                && _freshInstanceIdsByManager.TryGetValue(_freshManagerKey, out var gateSet)
                && gateSet != null && gateSet.Count > 0;
            if (!freshFilterActive && _lastClearFrame == Time.frameCount)
            {
                int scanManagerKey;
                try { scanManagerKey = manager.GetInstanceID(); }
                catch { scanManagerKey = 0; }
                bool clearedThisManager = _lastClearManagerKey == 0 || _lastClearManagerKey == scanManagerKey;
                if (clearedThisManager)
                {
                    throw new InvalidOperationException("Moodle scan deferred: rebuild in progress this frame (destroy-pending children present).");
                }
            }

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

                    // v1.2.0 fresh-instance filter: when AddMoodle postfix has populated a
                    // fresh set for the current manager, only admit children whose
                    // GetInstanceID() is in that set. This filters Destroy-pending nodes
                    // that Unity's fake-null check may miss within the same UpdateMoodles
                    // call (ClearMoodles calls Object.Destroy which is deferred to end of
                    // frame; the C# wrapper may still be non-null during the postfix).
                    if (_freshManagerKey != 0
                        && _freshInstanceIdsByManager.TryGetValue(_freshManagerKey, out var freshSet)
                        && freshSet != null && freshSet.Count > 0
                        && !freshSet.Contains(child.GetInstanceID()))
                    {
                        continue;
                    }

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
                    // v1.2.1 realistic fix for the "game overwrites mod order" regression.
                    //
                    // Root cause: under CU 7.0.1, AddAllMoodles may Instantiate a bonus
                    // "+N" GameObject into manager.moodles (no Moodle component) when side
                    // moodles exist, AND main+side moodles share the same parent. Both conditions
                    // make CanSafelyUseSiblingOrder fail (childCount != members.Count and mixed
                    // IsSide). The v1.2.0 code silently `continue`d here, so the sort was skipped
                    // every cycle -> the user saw the game's source-order positions "overwrite"
                    // the mod's startup sort. This is the regression reported by the user.
                    //
                    // Fix: fall back to AnchoredPosition, which writes anchoredPosition.x only
                    // (no sibling mutation). Safe regardless of sibling topology and bonus/
                    // decoration children, and persists in CU because manager.moodles is a plain
                    // Transform (decompile confirms no LayoutGroup component is added in code;
                    // if a scene/prefab adds one, this fallback still produces best-effort
                    // ordering each cycle rather than a silent no-op).
                    _siblingSortFallbackCount++;
                    changed = ApplyAnchoredSlots(members);
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
                    _anchoredSortWriteCount++;
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

                // v1.2.2 CRITICAL FIX: the slot set MUST be the row's ascending x values,
                // NOT the x values in sibling order. The original code took
                // OriginalAnchoredPosition in sibling order, which is ascending only for
                // a freshly game-created row. On every later pass (periodic net,
                // watchdog refresh, second finalize) the members' positions are already
                // mod-sorted, so sibling order is NOT ascending: using it as the slot
                // table permuted members against a scrambled slot list and WROTE A
                // GARBAGE LAYOUT, which the next rebuild "fixed" and the next net broke
                // again - the exact user-visible "two orders taking turns" regression
                // (found by the v1.2.2 behavior simulator, reproduced bit-for-bit).
                var slotXs = rowMembers.Select(v => v.OriginalAnchoredPosition.x).OrderBy(x => x).ToList();
                var order = kv.Value;
                for (int i = 0; i < order.Count && i < slotXs.Count; i++)
                {
                    var target = rowMembers[order[i]];
                    var slotX = slotXs[i];
                    var current = target.RectTransform.anchoredPosition;
                    // 只写 x（槽位），保留 y；x 已正确时不做多余写入。
                    if (Mathf.Abs(current.x - slotX) > 0.001f)
                    {
                        target.RectTransform.anchoredPosition = new Vector2(slotX, current.y);
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
                // v1.2.3: per-member effect strength for the in-group intensity ordering.
                // AddMoodle's captured intensity is exact; the trailing digit of the
                // runtime id (game: Moodle.type = icon + intensity) is the always-available
                // fallback and agrees with the capture for every game moodle.
                int intensity = members[i].Capture != null
                    ? members[i].Capture.Intensity
                    : MoodleIdentity.ParseTrailingIntensity(members[i].RuntimeId, -1);
                items.Add(new MoodleRowItem
                {
                    RuntimeId = members[i].RuntimeId,
                    IsSide = members[i].IsSide,
                    Intensity = intensity,
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
