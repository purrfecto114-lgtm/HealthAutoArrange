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

        private static ReminderRule LegacyRule(
            string state,
            bool enabled = true,
            ReminderMode mode = ReminderMode.Log,
            double cooldown = 0)
        {
            return new ReminderRule(state, state, enabled, mode, cooldown);
        }

        private static ReminderRule Rule(
            string state,
            ReminderRepeatMode repeatMode,
            double period = 60,
            int sends = 1,
            ReminderMode mode = ReminderMode.Log)
        {
            return new ReminderRule(state, state, true, mode, repeatMode, period, sends);
        }

        [Fact]
        public void Once_FirstAppearance_TriggersOnceForWholeEpisode()
        {
            var engine = new ReminderEngine(new[] { Rule("Bleeding", ReminderRepeatMode.Once) });

            Assert.Single(engine.Update(new[] { "Bleeding" }, T0));
            Assert.Empty(engine.Update(new[] { "Bleeding" }, T0.AddSeconds(1)));
            Assert.Empty(engine.Update(new[] { "Bleeding" }, T0.AddHours(1)));
        }

        [Fact]
        public void Once_DisappearThenReappear_RetriggersImmediately()
        {
            var engine = new ReminderEngine(new[] { Rule("Bleeding", ReminderRepeatMode.Once) });

            Assert.Single(engine.Update(new[] { "Bleeding" }, T0));
            Assert.Empty(engine.Update(Array.Empty<string>(), T0.AddSeconds(10)));
            Assert.Single(engine.Update(new[] { "Bleeding" }, T0.AddSeconds(11)));
        }

        [Fact]
        public void Once_TransientUiAbsence_DoesNotStartNewEpisode()
        {
            var engine = new ReminderEngine(new[] { Rule("Bleeding", ReminderRepeatMode.Once) });

            Assert.Single(engine.Update(new[] { "Bleeding" }, T0));
            Assert.Empty(engine.Update(Array.Empty<string>(), T0.AddSeconds(10)));
            Assert.Empty(engine.Update(new[] { "Bleeding" }, T0.AddSeconds(10.5)));
        }

        [Fact]
        public void Once_ConfirmedAbsenceThenReappear_StartsNewEpisodeEvenWithoutIntermediateTick()
        {
            var engine = new ReminderEngine(new[] { Rule("Bleeding", ReminderRepeatMode.Once) });

            Assert.Single(engine.Update(new[] { "Bleeding" }, T0));
            Assert.Empty(engine.Update(Array.Empty<string>(), T0.AddSeconds(10)));
            Assert.Single(engine.Update(new[] { "Bleeding" }, T0.AddSeconds(12)));
        }

        [Fact]
        public void WhilePresent_OnePerPeriod_PreservesLegacyPositiveCooldownSemantics()
        {
            var engine = new ReminderEngine(new[] { LegacyRule("Bleeding", cooldown: 60) });

            Assert.Single(engine.Update(new[] { "Bleeding" }, T0));
            Assert.Empty(engine.Update(new[] { "Bleeding" }, T0.AddSeconds(59)));
            Assert.Single(engine.Update(new[] { "Bleeding" }, T0.AddSeconds(60)));
            Assert.Empty(engine.Update(new[] { "Bleeding" }, T0.AddSeconds(61)));
        }

        [Fact]
        public void LegacyZeroCooldown_IsNowOnceInsteadOfEveryRefresh()
        {
            var engine = new ReminderEngine(new[] { LegacyRule("Bleeding", cooldown: 0) });

            Assert.Single(engine.Update(new[] { "Bleeding" }, T0));
            Assert.Empty(engine.Update(new[] { "Bleeding" }, T0.AddSeconds(1)));
            Assert.Empty(engine.Update(new[] { "Bleeding" }, T0.AddSeconds(2)));
        }

        [Fact]
        public void WhilePresent_ThreePerMinute_AreEvenlySpaced()
        {
            var engine = new ReminderEngine(new[]
            {
                Rule("ConsciousnessImpaired", ReminderRepeatMode.WhilePresent, period: 60, sends: 3)
            });

            Assert.Single(engine.Update(new[] { "ConsciousnessImpaired" }, T0));
            Assert.Empty(engine.Update(new[] { "ConsciousnessImpaired" }, T0.AddSeconds(19.9)));
            Assert.Single(engine.Update(new[] { "ConsciousnessImpaired" }, T0.AddSeconds(20)));
            Assert.Empty(engine.Update(new[] { "ConsciousnessImpaired" }, T0.AddSeconds(39.9)));
            Assert.Single(engine.Update(new[] { "ConsciousnessImpaired" }, T0.AddSeconds(40)));
            Assert.Single(engine.Update(new[] { "ConsciousnessImpaired" }, T0.AddSeconds(60)));
        }

        [Fact]
        public void WhilePresent_MissedIntervals_DoNotBurstCatchUp()
        {
            var engine = new ReminderEngine(new[]
            {
                Rule("Bleeding", ReminderRepeatMode.WhilePresent, period: 60, sends: 3)
            });

            Assert.Single(engine.Update(new[] { "Bleeding" }, T0));

            // Five minutes later: emit one message only, not fifteen catch-up messages.
            var afterPause = engine.Update(new[] { "Bleeding" }, T0.AddMinutes(5));
            Assert.Single(afterPause);
            Assert.Empty(engine.Update(new[] { "Bleeding" }, T0.AddMinutes(5).AddSeconds(1)));
            Assert.Single(engine.Update(new[] { "Bleeding" }, T0.AddMinutes(5).AddSeconds(20)));
        }

        [Fact]
        public void WhilePresent_DisappearResetsSchedule()
        {
            var engine = new ReminderEngine(new[]
            {
                Rule("Bleeding", ReminderRepeatMode.WhilePresent, period: 60, sends: 1)
            });

            Assert.Single(engine.Update(new[] { "Bleeding" }, T0));
            Assert.Empty(engine.Update(Array.Empty<string>(), T0.AddSeconds(10)));
            Assert.Single(engine.Update(new[] { "Bleeding" }, T0.AddSeconds(11)));
        }

        [Fact]
        public void DisabledRule_NeverTriggers()
        {
            var engine = new ReminderEngine(new[] { LegacyRule("Bleeding", enabled: false) });
            Assert.Empty(engine.Update(new[] { "Bleeding" }, T0));
        }

        [Fact]
        public void MessageCarriesConfiguredMode()
        {
            var engine = new ReminderEngine(new[]
            {
                Rule("Bleeding", ReminderRepeatMode.Once, mode: ReminderMode.BottomAlert)
            });
            var result = engine.Update(new[] { "Bleeding" }, T0);
            Assert.Equal(ReminderMode.BottomAlert, result[0].Mode);
        }

        [Fact]
        public void MultipleRules_TriggerIndependently()
        {
            var engine = new ReminderEngine(new[]
            {
                Rule("Bleeding", ReminderRepeatMode.Once, mode: ReminderMode.BottomAlert),
                Rule("Infection", ReminderRepeatMode.Once, mode: ReminderMode.HealthPanelHint),
            });

            var result = engine.Update(new[] { "Bleeding", "Infection" }, T0);
            Assert.Equal(2, result.Count);
            Assert.Contains(result, m => m.State == "Bleeding" && m.Mode == ReminderMode.BottomAlert);
            Assert.Contains(result, m => m.State == "Infection" && m.Mode == ReminderMode.HealthPanelHint);
        }

        [Fact]
        public void RuleForStateNotPresent_NeverTriggers()
        {
            var engine = new ReminderEngine(new[] { Rule("Bleeding", ReminderRepeatMode.Once) });
            Assert.Empty(engine.Update(new[] { "Infection" }, T0));
            Assert.Empty(engine.Update(new[] { "Infection" }, T0.AddSeconds(5)));
        }

        [Fact]
        public void MessageRecordsTriggerTime()
        {
            var engine = new ReminderEngine(new[] { Rule("Bleeding", ReminderRepeatMode.Once) });
            var result = engine.Update(new[] { "Bleeding" }, T0);
            Assert.Equal(T0, result[0].TriggeredAt);
        }

        [Fact]
        public void ClockMovingBackward_DoesNotCauseImmediateRepeat()
        {
            var engine = new ReminderEngine(new[]
            {
                Rule("Bleeding", ReminderRepeatMode.WhilePresent, period: 60, sends: 1)
            });

            Assert.Single(engine.Update(new[] { "Bleeding" }, T0));
            Assert.Empty(engine.Update(new[] { "Bleeding" }, T0.AddSeconds(-10)));
            Assert.Empty(engine.Update(new[] { "Bleeding" }, T0.AddSeconds(40)));
            Assert.Single(engine.Update(new[] { "Bleeding" }, T0.AddSeconds(50)));
        }
        [Fact]
        public void DuplicateRuleNames_DoNotShareEpisodeState()
        {
            var engine = new ReminderEngine(new[]
            {
                new ReminderRule("same-name", "Bleeding", true, ReminderMode.Log, ReminderRepeatMode.Once, 60, 1),
                new ReminderRule("same-name", "Infection", true, ReminderMode.Log, ReminderRepeatMode.Once, 60, 1),
            });

            Assert.Single(engine.Update(new[] { "Bleeding" }, T0));
            var second = engine.Update(new[] { "Bleeding", "Infection" }, T0.AddSeconds(2));
            Assert.Single(second);
            Assert.Equal("Infection", second[0].State);
        }

        [Fact]
        public void WhilePresent_UnsafeHighRate_IsClampedToOnePerSecond()
        {
            var rule = Rule("Bleeding", ReminderRepeatMode.WhilePresent, period: 2, sends: 10);
            Assert.Equal(2, rule.SendsPerPeriod);
            Assert.Equal(1, rule.EffectiveIntervalSeconds);

            var engine = new ReminderEngine(new[] { rule });
            Assert.Single(engine.Update(new[] { "Bleeding" }, T0));
            Assert.Empty(engine.Update(new[] { "Bleeding" }, T0.AddSeconds(0.5)));
            Assert.Single(engine.Update(new[] { "Bleeding" }, T0.AddSeconds(1)));
        }

        [Fact]
        public void RepeatPeriod_IsCappedToAvoidDateTimeOverflow()
        {
            var rule = Rule("Bleeding", ReminderRepeatMode.WhilePresent, period: double.MaxValue, sends: 1);
            Assert.Equal(ReminderRule.MaximumPeriodSeconds, rule.PeriodSeconds);
            var engine = new ReminderEngine(new[] { rule });
            Assert.Single(engine.Update(new[] { "Bleeding" }, T0));
            Assert.Empty(engine.Update(new[] { "Bleeding" }, T0.AddDays(1)));
        }


        [Fact]
        public void Reconfigure_UnchangedOnceRule_PreservesContinuousEpisode()
        {
            var engine = new ReminderEngine(new[]
            {
                Rule("ConsciousnessImpaired", ReminderRepeatMode.Once, mode: ReminderMode.Log)
            });
            Assert.Single(engine.Update(new[] { "ConsciousnessImpaired" }, T0));

            // Output/visual-facing changes must not create a fake new appearance.
            engine.Reconfigure(new[]
            {
                Rule("ConsciousnessImpaired", ReminderRepeatMode.Once, mode: ReminderMode.BottomAlert)
            });
            Assert.Empty(engine.Update(new[] { "ConsciousnessImpaired" }, T0.AddSeconds(2)));
        }

        [Fact]
        public void Reconfigure_ChangedCadence_ResetsOnlyChangedRule()
        {
            var engine = new ReminderEngine(new[]
            {
                Rule("Bleeding", ReminderRepeatMode.Once),
                Rule("ConsciousnessImpaired", ReminderRepeatMode.Once),
            });
            Assert.Equal(2, engine.Update(new[] { "Bleeding", "ConsciousnessImpaired" }, T0).Count);

            engine.Reconfigure(new[]
            {
                Rule("Bleeding", ReminderRepeatMode.Once),
                Rule("ConsciousnessImpaired", ReminderRepeatMode.WhilePresent, period: 60, sends: 3),
            });

            var messages = engine.Update(new[] { "Bleeding", "ConsciousnessImpaired" }, T0.AddSeconds(2));
            Assert.Single(messages);
            Assert.Equal("ConsciousnessImpaired", messages[0].State);
        }
    }
}
