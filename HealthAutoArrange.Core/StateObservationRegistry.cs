using System;
using System.Collections.Generic;
using System.Linq;

namespace HealthAutoArrange.Core
{
    /// <summary>
    /// Catalog of Moodle nodes that were actually observed in the game's UI hierarchy.
    /// AddMoodle capture is metadata; observation confirms that a Moodle component existed.
    /// </summary>
    public sealed class StateObservationRegistry
    {
        private const int MaxStates = 256;
        private readonly Dictionary<string, Observation> _states =
            new Dictionary<string, Observation>(StringComparer.OrdinalIgnoreCase);

        public void Observe(string runtimeId, MoodleCaptureMetadata capture, bool isSide, DateTimeOffset seenAt)
        {
            var normalizedRuntime = MoodleIdentity.NormalizeRuntimeId(runtimeId);
            // Without reliable AddMoodle metadata, preserve the full observed runtime ID.
            // Guessing that trailing digits are severity can corrupt third-party semantic IDs.
            var baseId = capture != null && !string.IsNullOrWhiteSpace(capture.IconId)
                ? MoodleIdentity.NormalizeRuntimeId(capture.IconId)
                : normalizedRuntime;
            if (baseId.Length == 0) return;

            // 可靠 capture 确认 baseId 为图标基础名后，可合并此前属于其严重度族
            // （baseId 后仅跟数字）的 provisional 行，避免同状态强度观察产生多行目录项。
            // 保守边界：只合并未捕获的 provisional 行（已有可靠 capture 确认的行是权威，
            // 绝不吞并）；不猜测强度值；非族内行保持独立。
            // 只要求 IconId 有效：即使 runtimeId 恰好等于基础名（无数字后缀），
            // 也应扫描并合并此前观察到的 provisional siblings。
            List<Observation> provisionalSiblings = null;
            if (capture != null && !string.IsNullOrWhiteSpace(capture.IconId))
            {
                foreach (var key in _states.Keys.ToArray())
                {
                    if (string.Equals(key, baseId, StringComparison.OrdinalIgnoreCase)) continue;
                    if (string.Equals(key, normalizedRuntime, StringComparison.OrdinalIgnoreCase)) continue;
                    if (_states[key].HasReliableCapture) continue;
                    if (!IsSeverityVariant(baseId, key)) continue;
                    if (provisionalSiblings == null) provisionalSiblings = new List<Observation>();
                    provisionalSiblings.Add(_states[key]);
                    _states.Remove(key);
                }
            }

            // If this runtime node was previously observed without capture metadata, it was kept
            // under its exact runtime ID. Once a reliable icon/base ID arrives, merge that
            // provisional entry instead of leaving duplicate catalog rows.
            Observation provisional = null;
            if (capture != null
                && !string.Equals(baseId, normalizedRuntime, StringComparison.OrdinalIgnoreCase)
                && _states.TryGetValue(normalizedRuntime, out provisional))
            {
                _states.Remove(normalizedRuntime);
            }

            if (!_states.TryGetValue(baseId, out var state))
            {
                if (_states.Count >= MaxStates)
                {
                    var oldest = _states.Values.OrderBy(x => x.LastSeenAt).FirstOrDefault();
                    if (oldest != null) _states.Remove(oldest.BaseId);
                }
                state = new Observation(baseId);
                _states[baseId] = state;
            }

            if (provisional != null && !ReferenceEquals(provisional, state))
                state.MergeFrom(provisional);
            if (provisionalSiblings != null)
            {
                foreach (var sibling in provisionalSiblings)
                {
                    if (!ReferenceEquals(sibling, state)) state.MergeFrom(sibling);
                }
            }

            // 只有提供图标名（可靠 capture）才确认基础名并允许 severity family 模式。
            state.HasReliableCapture = capture != null && !string.IsNullOrWhiteSpace(capture.IconId);
            state.LastRuntimeId = normalizedRuntime;
            state.LastSeenAt = seenAt;
            if (isSide) state.SeenInSideRow = true; else state.SeenInMainRow = true;
            // Do not infer severity from arbitrary trailing digits when capture metadata is absent.
            // Third-party IDs may legitimately end in semantic numbers.
            if (capture != null) state.Intensities.Add(capture.Intensity);

            if (capture != null)
            {
                if (!string.IsNullOrWhiteSpace(capture.DisplayName))
                {
                    state.DisplayName = capture.DisplayName;
                    state.HasExplicitDisplayName = true;
                }
                state.EverCritical |= capture.Critical;
                state.UsesChippedOnly |= capture.ChippedOnly;
            }
        }

        public IReadOnlyList<StateCatalogEntry> Snapshot()
        {
            return _states.Values
                .OrderBy(x => x.BaseId, StringComparer.OrdinalIgnoreCase)
                .Select(x => x.ToEntry())
                .ToArray();
        }

        public void Clear() => _states.Clear();

        /// <summary>
        /// runtime id 是否属于 baseId 的严重度族：等于 baseId，或 baseId 后仅跟数字。
        /// 与 StateMatcher 的 "#" 严重度族规则一致。
        /// </summary>
        private static bool IsSeverityVariant(string baseId, string runtimeId)
        {
            if (string.IsNullOrEmpty(baseId) || string.IsNullOrEmpty(runtimeId)) return false;
            if (string.Equals(baseId, runtimeId, StringComparison.OrdinalIgnoreCase)) return true;
            if (!runtimeId.StartsWith(baseId, StringComparison.OrdinalIgnoreCase)) return false;
            var suffix = runtimeId.Substring(baseId.Length);
            if (suffix.Length == 0) return true;
            for (var i = 0; i < suffix.Length; i++)
            {
                if (!char.IsDigit(suffix[i])) return false;
            }
            return true;
        }

        private sealed class Observation
        {
            public string BaseId { get; }
            public string DisplayName { get; set; }
            public string LastRuntimeId { get; set; }
            public DateTimeOffset LastSeenAt { get; set; }
            public HashSet<int> Intensities { get; } = new HashSet<int>();
            public bool SeenInMainRow { get; set; }
            public bool SeenInSideRow { get; set; }
            public bool EverCritical { get; set; }
            public bool UsesChippedOnly { get; set; }

            /// <summary>是否被可靠 capture 确认过（未确认时为 provisional，模式取精确 runtime id）。</summary>
            public bool HasReliableCapture { get; set; }

            /// <summary>显示名是否来自 capture（合并时避免被 provisional 默认名覆盖）。</summary>
            public bool HasExplicitDisplayName { get; set; }

            public Observation(string baseId)
            {
                BaseId = baseId;
                DisplayName = baseId;
                LastRuntimeId = string.Empty;
            }

            public void MergeFrom(Observation other)
            {
                if (other == null) return;
                // 仅在当前行还没有真实显示名时采纳其它行的显示名，
                // 防止 provisional 行的默认名（其 runtime id）覆盖 capture 提供的名称。
                if (!HasExplicitDisplayName
                    && !string.IsNullOrWhiteSpace(other.DisplayName)
                    && other.HasExplicitDisplayName)
                {
                    DisplayName = other.DisplayName;
                    HasExplicitDisplayName = true;
                }
                if (other.LastSeenAt > LastSeenAt)
                {
                    LastSeenAt = other.LastSeenAt;
                    LastRuntimeId = other.LastRuntimeId;
                }
                foreach (var intensity in other.Intensities) Intensities.Add(intensity);
                SeenInMainRow |= other.SeenInMainRow;
                SeenInSideRow |= other.SeenInSideRow;
                EverCritical |= other.EverCritical;
                UsesChippedOnly |= other.UsesChippedOnly;
            }

            public StateCatalogEntry ToEntry()
            {
                return new StateCatalogEntry(
                    BaseId,
                    string.IsNullOrWhiteSpace(DisplayName) ? BaseId : DisplayName,
                    // 无 capture provisional 状态必须使用 exact pattern；
                    // 可靠 capture 后才使用 severity family "#"。
                    HasReliableCapture ? BaseId + "#" : BaseId,
                    Intensities.OrderBy(x => x).ToArray(),
                    LastSeenAt,
                    LastRuntimeId,
                    SeenInMainRow,
                    SeenInSideRow,
                    EverCritical,
                    UsesChippedOnly);
            }
        }
    }
}
