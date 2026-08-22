using System;
using System.Collections.Generic;
using System.Linq;

namespace HealthAutoArrange.Core
{
    /// <summary>
    /// Non-invasive reminder state machine, independent of Unity UI.
    ///
    /// Semantics:
    /// - Once: emit exactly once per continuous appearance episode.
    /// - WhilePresent: emit immediately on appearance, then evenly at PeriodSeconds / SendsPerPeriod.
    /// - Missed slots are never replayed in a burst; at most one message per rule is emitted per Update.
    /// - When the state disappears, the episode resets. A later reappearance can emit immediately again.
    /// - Pattern matching supports exact, GUI severity-family '#', legacy/manual '*' prefix, and legacy base-name matching.
    /// - Reconfigure preserves runtime state only for cadence-equivalent rules, so saving unrelated UI
    ///   settings does not retrigger a continuous status.
    /// </summary>
    public sealed class ReminderEngine
    {
        public const double EpisodeResetGraceSeconds = 1d;

        private List<ReminderRule> _rules;
        private List<RuleRuntimeState> _runtime;

        public ReminderEngine(IEnumerable<ReminderRule> rules)
        {
            if (rules == null) throw new ArgumentNullException(nameof(rules));
            _rules = rules.ToList();
            _runtime = _rules.Select(_ => new RuleRuntimeState()).ToList();
        }

        /// <summary>
        /// Applies a new rule set without globally forgetting continuous-status episodes.
        /// Runtime cadence is preserved only when the rule's state, enabled flag and repeat cadence
        /// are unchanged. Visual/output changes do not reset cadence; a cadence/state change resets
        /// only that rule.
        /// </summary>
        public void Reconfigure(IEnumerable<ReminderRule> rules)
        {
            if (rules == null) throw new ArgumentNullException(nameof(rules));
            var nextRules = rules.ToList();
            var nextRuntime = new List<RuleRuntimeState>(nextRules.Count);
            var used = new bool[_rules.Count];

            foreach (var next in nextRules)
            {
                var match = -1;
                for (var i = 0; i < _rules.Count; i++)
                {
                    if (used[i]) continue;
                    if (!CadenceEquivalent(_rules[i], next)) continue;
                    match = i;
                    break;
                }

                if (match >= 0)
                {
                    used[match] = true;
                    nextRuntime.Add(_runtime[match]);
                }
                else
                {
                    nextRuntime.Add(new RuleRuntimeState());
                }
            }

            _rules = nextRules;
            _runtime = nextRuntime;
        }

        private static bool CadenceEquivalent(ReminderRule left, ReminderRule right)
        {
            if (left == null || right == null) return false;
            return string.Equals(left.Name, right.Name, StringComparison.OrdinalIgnoreCase)
                && string.Equals(left.State, right.State, StringComparison.OrdinalIgnoreCase)
                && left.Enabled == right.Enabled
                && left.RepeatMode == right.RepeatMode
                && Math.Abs(left.PeriodSeconds - right.PeriodSeconds) < 0.000001d
                && left.SendsPerPeriod == right.SendsPerPeriod;
        }

        public IReadOnlyList<ReminderMessage> Update(
            IReadOnlyCollection<string> presentStates, DateTimeOffset now)
        {
            if (presentStates == null) throw new ArgumentNullException(nameof(presentStates));

            var messages = new List<ReminderMessage>();
            for (int ruleIndex = 0; ruleIndex < _rules.Count; ruleIndex++)
            {
                var rule = _rules[ruleIndex];
                if (rule == null || !rule.Enabled) continue;
                var rt = _runtime[ruleIndex];

                bool isPresent = presentStates.Any(s => Matches(rule.State, s));
                if (!isPresent)
                {
                    if (rt.WasPresent)
                    {
                        if (!rt.AbsentSince.HasValue || now < rt.AbsentSince.Value)
                        {
                            rt.AbsentSince = now;
                        }
                        else if ((now - rt.AbsentSince.Value).TotalSeconds >= EpisodeResetGraceSeconds)
                        {
                            rt.ResetEpisode();
                        }
                    }
                    continue;
                }

                if (rt.AbsentSince.HasValue)
                {
                    var absentFor = now >= rt.AbsentSince.Value
                        ? (now - rt.AbsentSince.Value).TotalSeconds
                        : 0d;
                    if (absentFor >= EpisodeResetGraceSeconds)
                        rt.ResetEpisode();
                    else
                        rt.AbsentSince = null;
                }

                if (rt.LastObservedAt.HasValue && now < rt.LastObservedAt.Value)
                {
                    rt.NextDueAt = rule.RepeatMode == ReminderRepeatMode.WhilePresent
                        ? now.AddSeconds(rule.EffectiveIntervalSeconds)
                        : (DateTimeOffset?)null;
                }
                rt.LastObservedAt = now;

                if (!rt.WasPresent)
                {
                    Emit(messages, rule, rt, now);
                    rt.WasPresent = true;
                    rt.NextDueAt = rule.RepeatMode == ReminderRepeatMode.WhilePresent
                        ? now.AddSeconds(rule.EffectiveIntervalSeconds)
                        : (DateTimeOffset?)null;
                    continue;
                }

                if (rule.RepeatMode != ReminderRepeatMode.WhilePresent)
                    continue;

                if (!rt.NextDueAt.HasValue)
                {
                    rt.NextDueAt = now.AddSeconds(rule.EffectiveIntervalSeconds);
                    continue;
                }

                if (now >= rt.NextDueAt.Value)
                {
                    Emit(messages, rule, rt, now);
                    rt.NextDueAt = now.AddSeconds(rule.EffectiveIntervalSeconds);
                }
            }
            return messages;
        }

        private static void Emit(
            List<ReminderMessage> messages,
            ReminderRule rule,
            RuleRuntimeState rt,
            DateTimeOffset now)
        {
            messages.Add(new ReminderMessage(rule.Name, rule.State, rule.Mode, now));
            rt.LastTriggeredAt = now;
        }

        private static bool Matches(string pattern, string state)
        {
            return !string.IsNullOrEmpty(pattern)
                && !string.IsNullOrEmpty(state)
                && StateMatcher.MatchesPattern(pattern, state);
        }

        private sealed class RuleRuntimeState
        {
            public bool WasPresent;
            public DateTimeOffset? LastTriggeredAt;
            public DateTimeOffset? LastObservedAt;
            public DateTimeOffset? NextDueAt;
            public DateTimeOffset? AbsentSince;

            public void ResetEpisode()
            {
                WasPresent = false;
                LastTriggeredAt = null;
                LastObservedAt = null;
                NextDueAt = null;
                AbsentSince = null;
            }
        }
    }
}
