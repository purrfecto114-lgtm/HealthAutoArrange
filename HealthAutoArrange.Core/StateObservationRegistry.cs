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
            var baseId = capture != null && !string.IsNullOrWhiteSpace(capture.IconId)
                ? MoodleIdentity.NormalizeRuntimeId(capture.IconId)
                : MoodleIdentity.BaseId(normalizedRuntime);
            if (baseId.Length == 0) return;

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

            state.LastRuntimeId = normalizedRuntime;
            state.LastSeenAt = seenAt;
            if (isSide) state.SeenInSideRow = true; else state.SeenInMainRow = true;
            state.Intensities.Add(capture != null
                ? capture.Intensity
                : MoodleIdentity.ParseTrailingIntensity(normalizedRuntime, 0));

            if (capture != null)
            {
                if (!string.IsNullOrWhiteSpace(capture.DisplayName)) state.DisplayName = capture.DisplayName;
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

            public Observation(string baseId)
            {
                BaseId = baseId;
                DisplayName = baseId;
                LastRuntimeId = string.Empty;
            }

            public StateCatalogEntry ToEntry()
            {
                return new StateCatalogEntry(
                    BaseId,
                    string.IsNullOrWhiteSpace(DisplayName) ? BaseId : DisplayName,
                    BaseId + "*",
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
