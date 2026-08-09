using System;
using System.Linq;
using HealthAutoArrange.Core;
using Xunit;

namespace HealthAutoArrange.Tests
{
    public class ReminderEngineTests
    {
        private static readonly DateTimeOffset T0 =
            new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);

        private static ReminderRule Rule(
            string state,
            bool enabled = true,
            ReminderMode mode = ReminderMode.Log,
            double cooldown = 0)
        {
            return new ReminderRule(state, state, enabled, mode, cooldown);
        }

        [Fact]
        public void FirstAppearance_TriggersOnce()
        {
            var engine = new ReminderEngine(new[] { Rule("Bleeding", cooldown: 60) });

            var first = engine.Update(new[] { "Bleeding" }, T0);
            Assert.Single(first);
            Assert.Equal("Bleeding", first[0].State);
            Assert.Equal(ReminderMode.Log, first[0].Mode);

            // 状态持续存在且未到冷却 -> 不触发
            var second = engine.Update(new[] { "Bleeding" }, T0.AddSeconds(1));
            Assert.Empty(second);
        }

        [Fact]
        public void PersistentState_TriggersPerCooldown()
        {
            var engine = new ReminderEngine(new[] { Rule("Bleeding", cooldown: 60) });

            Assert.Single(engine.Update(new[] { "Bleeding" }, T0));
            Assert.Empty(engine.Update(new[] { "Bleeding" }, T0.AddSeconds(59)));
            Assert.Single(engine.Update(new[] { "Bleeding" }, T0.AddSeconds(60)));
            Assert.Empty(engine.Update(new[] { "Bleeding" }, T0.AddSeconds(61)));
        }

        [Fact]
        public void DisappearThenReappear_Retriggers()
        {
            var engine = new ReminderEngine(new[] { Rule("Bleeding") });

            Assert.Single(engine.Update(new[] { "Bleeding" }, T0));
            Assert.Empty(engine.Update(Array.Empty<string>(), T0.AddSeconds(10)));
            Assert.Single(engine.Update(new[] { "Bleeding" }, T0.AddSeconds(20)));
        }

        [Fact]
        public void DisabledRule_NeverTriggers()
        {
            var engine = new ReminderEngine(new[] { Rule("Bleeding", enabled: false) });
            Assert.Empty(engine.Update(new[] { "Bleeding" }, T0));
        }

        [Fact]
        public void MessageCarriesConfiguredMode()
        {
            var engine = new ReminderEngine(new[] { Rule("Bleeding", mode: ReminderMode.BottomAlert) });
            var result = engine.Update(new[] { "Bleeding" }, T0);
            Assert.Equal(ReminderMode.BottomAlert, result[0].Mode);
        }

        [Fact]
        public void MultipleRules_TriggerIndependently()
        {
            var engine = new ReminderEngine(new[]
            {
                Rule("Bleeding", mode: ReminderMode.BottomAlert),
                Rule("Infection", mode: ReminderMode.HealthPanelHint),
            });

            var result = engine.Update(new[] { "Bleeding", "Infection" }, T0);
            Assert.Equal(2, result.Count);
            Assert.Contains(result, m => m.State == "Bleeding" && m.Mode == ReminderMode.BottomAlert);
            Assert.Contains(result, m => m.State == "Infection" && m.Mode == ReminderMode.HealthPanelHint);
        }

        [Fact]
        public void ZeroCooldown_TriggersEveryUpdateWhilePresent()
        {
            var engine = new ReminderEngine(new[] { Rule("Bleeding", cooldown: 0) });
            Assert.Single(engine.Update(new[] { "Bleeding" }, T0));
            Assert.Single(engine.Update(new[] { "Bleeding" }, T0.AddSeconds(1)));
        }

        [Fact]
        public void RuleForStateNotPresent_NeverTriggers()
        {
            var engine = new ReminderEngine(new[] { Rule("Bleeding") });
            Assert.Empty(engine.Update(new[] { "Infection" }, T0));
            Assert.Empty(engine.Update(new[] { "Infection" }, T0.AddSeconds(5)));
        }

        [Fact]
        public void MessageRecordsTriggerTime()
        {
            var engine = new ReminderEngine(new[] { Rule("Bleeding") });
            var result = engine.Update(new[] { "Bleeding" }, T0);
            Assert.Equal(T0, result[0].TriggeredAt);
        }
    }
}