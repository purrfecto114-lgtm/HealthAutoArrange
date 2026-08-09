using System;

namespace HealthAutoArrange.Core
{
    /// <summary>
    /// 一条提醒规则。规则名即其监视的状态名（来自配置键 Reminder.&lt;rule&gt;.*）。
    /// </summary>
    public sealed class ReminderRule
    {
        /// <summary>规则名称（配置中的 &lt;rule&gt;）。</summary>
        public string Name { get; }

        /// <summary>被监视的状态标识。</summary>
        public string State { get; }

        /// <summary>是否启用。</summary>
        public bool Enabled { get; }

        /// <summary>输出模式。</summary>
        public ReminderMode Mode { get; }

        /// <summary>冷却秒数；状态持续存在时只按冷却间隔触发。</summary>
        public double CooldownSeconds { get; }

        public ReminderRule(string name, string state, bool enabled, ReminderMode mode, double cooldownSeconds)
        {
            Name = name ?? throw new ArgumentNullException(nameof(name));
            State = state ?? throw new ArgumentNullException(nameof(state));
            Enabled = enabled;
            Mode = mode;
            CooldownSeconds = cooldownSeconds;
        }
    }
}