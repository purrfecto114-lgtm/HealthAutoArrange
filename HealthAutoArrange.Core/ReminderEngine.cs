using System;
using System.Collections.Generic;
using System.Linq;

namespace HealthAutoArrange.Core
{
    /// <summary>
    /// 非侵入性提醒核心，与游戏 UI 无关。
    /// 规则：状态首次出现触发一次；持续存在时只按冷却间隔触发；
    /// 状态消失后再次出现可重新触发；禁用规则不触发。
    /// 规则状态支持 exact、prefix 通配符（如 "bleeding*"）与基础名匹配
    /// （去除状态末尾数字后比较，如 "bleeding2" 匹配 "bleeding"）。
    /// </summary>
    public sealed class ReminderEngine
    {
        private readonly IReadOnlyList<ReminderRule> _rules;
        private readonly Dictionary<string, RuleRuntimeState> _runtime;

        public ReminderEngine(IEnumerable<ReminderRule> rules)
        {
            if (rules == null) throw new ArgumentNullException(nameof(rules));
            _rules = rules.ToList();
            _runtime = new Dictionary<string, RuleRuntimeState>(StringComparer.OrdinalIgnoreCase);
            foreach (var rule in _rules)
            {
                _runtime[rule.Name] = new RuleRuntimeState();
            }
        }

        /// <summary>
        /// 以当前出现的状态集合与当前时间更新引擎，返回本次应触发的消息列表。
        /// </summary>
        public IReadOnlyList<ReminderMessage> Update(
            IReadOnlyCollection<string> presentStates, DateTimeOffset now)
        {
            if (presentStates == null) throw new ArgumentNullException(nameof(presentStates));

            var messages = new List<ReminderMessage>();
            foreach (var rule in _rules)
            {
                if (!rule.Enabled) continue;

                var rt = _runtime[rule.Name];
                bool isPresent = presentStates.Any(s => Matches(rule.State, s));

                if (isPresent)
                {
                    bool shouldTrigger = !rt.WasPresent
                        || rt.LastTriggeredAt == null
                        || (now - rt.LastTriggeredAt.Value) >= TimeSpan.FromSeconds(rule.CooldownSeconds);

                    if (shouldTrigger)
                    {
                        messages.Add(new ReminderMessage(rule.Name, rule.State, rule.Mode, now));
                        rt.LastTriggeredAt = now;
                    }
                    rt.WasPresent = true;
                }
                else
                {
                    rt.WasPresent = false;
                }
            }
            return messages;
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
        }
    }
}