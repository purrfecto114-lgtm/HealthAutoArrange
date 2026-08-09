using System;
using System.Collections.Generic;
using System.Linq;

namespace HealthAutoArrange.Core
{
    /// <summary>添加提醒规则的结果。</summary>
    public sealed class ReminderRuleAddResult
    {
        /// <summary>是否成功添加。</summary>
        public bool Added { get; }

        /// <summary>冲突时已存在的规则名；无冲突为 null。</summary>
        public string ConflictRule { get; }

        /// <summary>诊断消息。</summary>
        public string Message { get; }

        public ReminderRuleAddResult(bool added, string conflictRule, string message)
        {
            Added = added;
            ConflictRule = conflictRule;
            Message = message ?? string.Empty;
        }
    }

    /// <summary>
    /// 提醒规则基础模型：相同基础状态只能有一个规则（去重）；
    /// 规则状态为系统生成的通配模式（baseId + "#"）；支持一次性或持续周期提醒。
    /// 纯 C#，无 Unity 依赖，可单元测试。
    /// </summary>
    public sealed class ReminderRuleEditor
    {
        private readonly List<ReminderRule> _rules = new List<ReminderRule>();

        /// <summary>当前规则（按添加顺序）。</summary>
        public IReadOnlyList<ReminderRule> Rules => _rules;

        /// <summary>
        /// 按基础状态添加规则；相同基础状态已存在时返回冲突（首次优先）。
        /// </summary>
        public ReminderRuleAddResult AddRule(
            string stateOrBase, bool enabled, ReminderMode mode, double cooldownSeconds)
        {
            // Legacy overload: positive cooldown maps to one send per cooldown period;
            // zero now maps to Once, avoiding the old every-refresh spam behaviour.
            var repeatMode = cooldownSeconds > 0d ? ReminderRepeatMode.WhilePresent : ReminderRepeatMode.Once;
            var period = cooldownSeconds > 0d ? cooldownSeconds : ReminderRule.DefaultPeriodSeconds;
            return AddRule(stateOrBase, enabled, mode, repeatMode, period, 1);
        }

        public ReminderRuleAddResult AddRule(
            string stateOrBase,
            bool enabled,
            ReminderMode mode,
            ReminderRepeatMode repeatMode,
            double periodSeconds,
            int sendsPerPeriod)
        {
            var baseId = MoodleIdentity.PatternBaseId(stateOrBase);
            if (baseId.Length == 0) return new ReminderRuleAddResult(false, null, "empty state");

            foreach (var rule in _rules)
            {
                if (string.Equals(BaseOf(rule), baseId, StringComparison.OrdinalIgnoreCase))
                    return new ReminderRuleAddResult(false, rule.Name, $"rule for '{baseId}' already exists");
            }

            _rules.Add(new ReminderRule(
                baseId,
                baseId + "#",
                enabled,
                mode,
                repeatMode,
                periodSeconds,
                sendsPerPeriod));
            return new ReminderRuleAddResult(true, null, "added");
        }

        /// <summary>按基础状态移除规则；成功返回 true。</summary>
        public bool RemoveRule(string stateOrBase)
        {
            var baseId = MoodleIdentity.PatternBaseId(stateOrBase);
            return _rules.RemoveAll(r => string.Equals(BaseOf(r), baseId, StringComparison.OrdinalIgnoreCase)) > 0;
        }

        private static string BaseOf(ReminderRule rule)
        {
            return MoodleIdentity.PatternBaseId(rule.State ?? string.Empty);
        }
    }
}