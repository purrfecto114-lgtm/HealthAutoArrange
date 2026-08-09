using System;

namespace HealthAutoArrange.Core
{
    /// <summary>
    /// 提醒引擎产出的消息对象。携带规则名、状态、输出模式与触发时间，
    /// 由上层适配器按模式分发（Log / BottomAlert / HealthPanelHint）。
    /// </summary>
    public sealed class ReminderMessage
    {
        public string RuleName { get; }
        public string State { get; }
        public ReminderMode Mode { get; }
        public DateTimeOffset TriggeredAt { get; }

        public ReminderMessage(string ruleName, string state, ReminderMode mode, DateTimeOffset triggeredAt)
        {
            RuleName = ruleName ?? throw new ArgumentNullException(nameof(ruleName));
            State = state ?? throw new ArgumentNullException(nameof(state));
            Mode = mode;
            TriggeredAt = triggeredAt;
        }
    }
}