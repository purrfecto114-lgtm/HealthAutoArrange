using System;
using System.Collections.Generic;

namespace HealthAutoArrange.Core
{
    /// <summary>One base Moodle state, merged across observed severity variants.</summary>
    public sealed class StateCatalogEntry
    {
        public string BaseId { get; }
        public string DisplayName { get; }
        public string Pattern { get; }
        public IReadOnlyList<int> Intensities { get; }
        public DateTimeOffset LastSeenAt { get; }
        public string LastRuntimeId { get; }
        public bool SeenInMainRow { get; }
        public bool SeenInSideRow { get; }
        public bool EverCritical { get; }
        public bool UsesChippedOnly { get; }

        public StateCatalogEntry(
            string baseId,
            string displayName,
            string pattern,
            IReadOnlyList<int> intensities,
            DateTimeOffset lastSeenAt,
            string lastRuntimeId,
            bool seenInMainRow = false,
            bool seenInSideRow = false,
            bool everCritical = false,
            bool usesChippedOnly = false)
        {
            BaseId = baseId ?? throw new ArgumentNullException(nameof(baseId));
            DisplayName = displayName ?? string.Empty;
            Pattern = pattern ?? throw new ArgumentNullException(nameof(pattern));
            Intensities = intensities ?? throw new ArgumentNullException(nameof(intensities));
            LastSeenAt = lastSeenAt;
            LastRuntimeId = lastRuntimeId ?? string.Empty;
            SeenInMainRow = seenInMainRow;
            SeenInSideRow = seenInSideRow;
            EverCritical = everCritical;
            UsesChippedOnly = usesChippedOnly;
        }
    }

    /// <summary>
    /// Builds a catalog from AddMoodle captures. Kept for diagnostics/tests and older callers.
    /// The in-game settings UI should prefer StateObservationRegistry because an AddMoodle
    /// call does not prove that a Moodle node was actually instantiated in the status bar.
    /// </summary>
    public sealed class StateCatalog
    {
        private readonly IReadOnlyList<StateCatalogEntry> _entries;
        public IReadOnlyList<StateCatalogEntry> Entries => _entries;

        private StateCatalog(IReadOnlyList<StateCatalogEntry> entries) { _entries = entries; }

        public static StateCatalog FromCaptures(IEnumerable<MoodleCaptureMetadata> captures)
        {
            if (captures == null) throw new ArgumentNullException(nameof(captures));

            var groups = new Dictionary<string, List<MoodleCaptureMetadata>>(StringComparer.OrdinalIgnoreCase);
            // 记录每个 baseId 分组是否由可靠 IconId 派生；无可靠 IconId 时不得猜测
            // severity family，须保留完整 ExpectedRuntimeId 并生成 exact pattern。
            var reliableBaseIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var capture in captures)
            {
                if (capture == null) continue;
                var hasReliableIconId = !string.IsNullOrWhiteSpace(capture.IconId);
                var baseId = hasReliableIconId
                    ? MoodleIdentity.NormalizeRuntimeId(capture.IconId)
                    : MoodleIdentity.NormalizeRuntimeId(capture.ExpectedRuntimeId);
                if (baseId.Length == 0) continue;
                if (!groups.TryGetValue(baseId, out var list))
                {
                    list = new List<MoodleCaptureMetadata>();
                    groups[baseId] = list;
                }
                list.Add(capture);
                if (hasReliableIconId) reliableBaseIds.Add(baseId);
            }

            var entries = new List<StateCatalogEntry>(groups.Count);
            foreach (var kv in groups)
            {
                var baseId = kv.Key;
                var list = kv.Value;
                list.Sort((a, b) => a.CapturedAt.CompareTo(b.CapturedAt));
                var latest = list[list.Count - 1];

                var displayName = baseId;
                for (int i = list.Count - 1; i >= 0; i--)
                {
                    if (!string.IsNullOrWhiteSpace(list[i].DisplayName))
                    {
                        displayName = list[i].DisplayName;
                        break;
                    }
                }

                var intensities = new List<int>();
                var seen = new HashSet<int>();
                var main = false;
                var side = false;
                var critical = false;
                var chippedOnly = false;
                foreach (var c in list)
                {
                    if (seen.Add(c.Intensity)) intensities.Add(c.Intensity);
                    if (c.IsSide) side = true; else main = true;
                    critical |= c.Critical;
                    chippedOnly |= c.ChippedOnly;
                }
                intensities.Sort();

                // 可靠 IconId 派生基础名时使用 severity family（"base#"）；
                // 无可靠 IconId 时保留完整 runtime id 的 exact pattern（与
                // StateObservationRegistry 的 provisional exact 策略一致，不加 "#"）。
                var pattern = reliableBaseIds.Contains(baseId) ? baseId + "#" : baseId;
                entries.Add(new StateCatalogEntry(
                    baseId, displayName, pattern, intensities,
                    latest.CapturedAt, latest.ExpectedRuntimeId,
                    main, side, critical, chippedOnly));
            }

            entries.Sort((a, b) => string.Compare(a.BaseId, b.BaseId, StringComparison.OrdinalIgnoreCase));
            return new StateCatalog(entries);
        }
    }
}
