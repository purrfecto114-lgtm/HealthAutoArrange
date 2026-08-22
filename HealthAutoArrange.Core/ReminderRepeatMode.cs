namespace HealthAutoArrange.Core
{
    /// <summary>
    /// Controls how often one reminder rule may emit while its Moodle stays present.
    /// </summary>
    public enum ReminderRepeatMode
    {
        /// <summary>Emit once when the state first appears; reset only after it disappears.</summary>
        Once = 0,

        /// <summary>
        /// Keep emitting while the state remains present. Emissions are spread evenly over
        /// PeriodSeconds according to SendsPerPeriod; missed slots are never burst-replayed.
        /// </summary>
        WhilePresent = 1,
    }
}
