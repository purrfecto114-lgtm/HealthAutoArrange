using System;
using System.Collections.Generic;

namespace HealthAutoArrange.Core
{
    /// <summary>
    /// 一次 AddMoodle 调用的捕获元数据（移植自 MoodleSorter_Source 的 MoodleCallMetadata）。
    /// 纯 C#，无 Unity 依赖；Manager 以 object 形式保存（运行时为 MoodleManager 实例）。
    /// </summary>
    public sealed class MoodleCaptureMetadata
    {
        /// <summary>图标名（AddMoodle 的 icon 参数）。</summary>
        public string IconId { get; set; }

        /// <summary>期望的 runtime id（icon + intensity 规范化）。</summary>
        public string ExpectedRuntimeId { get; set; }

        /// <summary>显示名（AddMoodle 的 name 参数）。</summary>
        public string DisplayName { get; set; }

        /// <summary>描述（AddMoodle 的 desc 参数）。</summary>
        public string Description { get; set; }

        /// <summary>强度（AddMoodle 的 intensity 参数）。</summary>
        public int Intensity { get; set; }

        /// <summary>是否 critical。</summary>
        public bool Critical { get; set; }

        /// <summary>是否使用原版 chippedOnly 显示条件；实际可见性仍由游戏决定。</summary>
        public bool ChippedOnly { get; set; }

        /// <summary>所在行：true = side 行，false = main 行。</summary>
        public bool IsSide { get; set; }

        /// <summary>创建顺序（单调递增）。</summary>
        public int Sequence { get; set; }

        /// <summary>捕获时间。</summary>
        public DateTimeOffset CapturedAt { get; set; }

        /// <summary>manager 实例（MoodleManager，不透明引用）。</summary>
        public object Manager { get; set; }
    }

    /// <summary>
    /// AddMoodle 捕获注册表（移植自 MoodleSorter_Source 的 CaptureRegistry）：
    /// 记录最近一次刷新周期内的 AddMoodle 调用，供扫描阶段按 runtime id 解析回捕获元数据。
    /// 纯 C#，无 Unity 依赖，可单元测试。
    /// </summary>
    public sealed class MoodleCaptureRegistry
    {
        /// <summary>保留的最近调用数上限。</summary>
        public const int MaxRecentCalls = 192;

        private readonly List<MoodleCaptureMetadata> _recent = new List<MoodleCaptureMetadata>();
        private int _sequence;

        /// <summary>Most recent monotonic capture sequence.</summary>
        public int LatestSequence => _sequence;

        /// <summary>
        /// 记录一次 AddMoodle 调用。manager 为调用方实例（MoodleManager）。
        /// </summary>
        public void Capture(
            object manager,
            int intensity,
            string icon,
            string displayName,
            string description,
            bool critical,
            bool chippedOnly,
            bool isSide)
        {
            _recent.Add(new MoodleCaptureMetadata
            {
                IconId = icon ?? string.Empty,
                ExpectedRuntimeId = MoodleIdentity.ExpectedRuntimeId(icon, intensity),
                DisplayName = displayName ?? string.Empty,
                Description = description ?? string.Empty,
                Intensity = intensity,
                Critical = critical,
                ChippedOnly = chippedOnly,
                IsSide = isSide,
                Sequence = ++_sequence,
                CapturedAt = DateTimeOffset.UtcNow,
                Manager = manager
            });

            if (_recent.Count > MaxRecentCalls)
                _recent.RemoveRange(0, _recent.Count - MaxRecentCalls);
        }

        /// <summary>
        /// 将扫描到的 runtime id 解析回捕获元数据：
        /// 先精确匹配期望 runtime id（最新优先），再按基础图标名回退匹配。
        /// 未命中返回 null。
        /// </summary>
        public MoodleCaptureMetadata Resolve(string runtimeId, object manager = null)
        {
            return Resolve(runtimeId, manager, 0);
        }

        /// <summary>
        /// Resolves metadata only from captures newer than the supplied refresh-boundary sequence.
        /// This prevents an old AddMoodle call from donating stale name/severity data to a later UI node.
        /// </summary>
        public MoodleCaptureMetadata Resolve(string runtimeId, object manager, int minSequenceExclusive)
        {
            var normalized = MoodleIdentity.NormalizeRuntimeId(runtimeId);

            for (var i = _recent.Count - 1; i >= 0; i--)
            {
                var item = _recent[i];
                if (item.Sequence <= minSequenceExclusive) continue;
                if (manager != null && !ReferenceEquals(item.Manager, manager)) continue;
                if (item.ExpectedRuntimeId == normalized) return item;
            }

            for (var i = _recent.Count - 1; i >= 0; i--)
            {
                var item = _recent[i];
                if (item.Sequence <= minSequenceExclusive) continue;
                if (manager != null && !ReferenceEquals(item.Manager, manager)) continue;

                // Conservative fallback: an icon/base ID may be followed by severity digits,
                // but digits that are already part of the icon ID are semantic and must be kept.
                // This avoids collapsing third-party IDs such as drug2 and drug3 into "drug".
                var iconId = MoodleIdentity.NormalizeRuntimeId(item.IconId);
                if (MatchesSeverityFamily(iconId, normalized)) return item;
            }

            return null;
        }


        private static bool MatchesSeverityFamily(string baseId, string runtimeId)
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

        /// <summary>清空注册表（刷新周期开始前调用）。</summary>
        public void Clear()
        {
            _recent.Clear();
        }

        /// <summary>当前全部捕获（按捕获顺序）。</summary>
        public IReadOnlyList<MoodleCaptureMetadata> Snapshot()
        {
            return _recent.ToArray();
        }
    }
}