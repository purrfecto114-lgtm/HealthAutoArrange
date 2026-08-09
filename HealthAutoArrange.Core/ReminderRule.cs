using System;

namespace HealthAutoArrange.Core
{
    /// <summary>
    /// One reminder rule. The rule name is also the persisted state pattern
    /// (for example GUI-generated "bleeding#" or legacy/manual "bleeding*").
    /// </summary>
    public sealed class ReminderRule
    {
        public const double DefaultPeriodSeconds = 60d;
        public const int DefaultSendsPerPeriod = 1;
        public const double MinimumPeriodSeconds = 1d;
        public const double MaximumPeriodSeconds = 604800d; // 7 days; longer repeats should use Once.
        public const double MinimumEffectiveIntervalSeconds = 1d;
        public const int MaximumSendsPerPeriod = 120;

        /// <summary>Rule name / persisted configuration key.</summary>
        public string Name { get; }

        /// <summary>State identifier or prefix pattern monitored by this rule.</summary>
        public string State { get; }

        public bool Enabled { get; }
        public ReminderMode Mode { get; }
        public ReminderRepeatMode RepeatMode { get; }

        /// <summary>Length of one repeat period in seconds when RepeatMode is WhilePresent.</summary>
        public double PeriodSeconds { get; }

        /// <summary>Target number of evenly-spaced sends in each repeat period.</summary>
        public int SendsPerPeriod { get; }

        /// <summary>
        /// Effective spacing between sends while the state remains present.
        /// This is intentionally derived instead of maintaining a second independent cooldown.
        /// </summary>
        public double EffectiveIntervalSeconds => RepeatMode == ReminderRepeatMode.WhilePresent
            ? PeriodSeconds / SendsPerPeriod
            : double.PositiveInfinity;

        /// <summary>
        /// Legacy v1.1.1 compatibility view. A positive old cooldown maps to
        /// WhilePresent(period=cooldown, sends=1); zero maps to Once to avoid the historical
        /// every-refresh spam bug.
        /// </summary>
        public double CooldownSeconds => RepeatMode == ReminderRepeatMode.WhilePresent
            ? EffectiveIntervalSeconds
            : 0d;

        /// <summary>
        /// Legacy constructor used by old tests/config call sites. Positive cooldown preserves
        /// the old repeat interval; zero/negative now means Once rather than every refresh.
        /// </summary>
        public ReminderRule(string name, string state, bool enabled, ReminderMode mode, double cooldownSeconds)
            : this(
                name,
                state,
                enabled,
                mode,
                cooldownSeconds > 0d ? ReminderRepeatMode.WhilePresent : ReminderRepeatMode.Once,
                cooldownSeconds > 0d ? cooldownSeconds : DefaultPeriodSeconds,
                DefaultSendsPerPeriod)
        {
        }

        public ReminderRule(
            string name,
            string state,
            bool enabled,
            ReminderMode mode,
            ReminderRepeatMode repeatMode,
            double periodSeconds,
            int sendsPerPeriod)
        {
            Name = name ?? throw new ArgumentNullException(nameof(name));
            State = state ?? throw new ArgumentNullException(nameof(state));
            Enabled = enabled;
            Mode = mode;
            RepeatMode = repeatMode;
            PeriodSeconds = NormalizePeriod(periodSeconds);
            SendsPerPeriod = NormalizeSends(PeriodSeconds, sendsPerPeriod);
        }

        public static double NormalizePeriod(double value)
        {
            if (double.IsNaN(value) || double.IsInfinity(value)) return DefaultPeriodSeconds;
            return Math.Max(MinimumPeriodSeconds, Math.Min(MaximumPeriodSeconds, value));
        }

        public static int NormalizeSends(int value)
        {
            if (value < 1) return DefaultSendsPerPeriod;
            return Math.Min(MaximumSendsPerPeriod, value);
        }

        /// <summary>
        /// Normalize a period/count pair to a cadence the runtime can actually deliver without
        /// recreating alert spam. Repeating reminders are intentionally capped at one send/second.
        /// </summary>
        public static int NormalizeSends(double periodSeconds, int value)
        {
            var period = NormalizePeriod(periodSeconds);
            var requested = NormalizeSends(value);
            var periodCapacity = Math.Max(1, (int)Math.Floor(period / MinimumEffectiveIntervalSeconds));
            return Math.Min(requested, periodCapacity);
        }
    }
}
