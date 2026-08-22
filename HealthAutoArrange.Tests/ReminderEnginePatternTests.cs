using System;
using System.Linq;
using HealthAutoArrange.Core;
using Xunit;

namespace HealthAutoArrange.Tests
{
    /// <summary>
    /// 提醒引擎对基础状态模式（bleeding*）的匹配行为：
    /// 状态持续更新不能重复产生首次提醒；消失再出现才允许再次提醒；兼容旧 cooldown 配置。
    /// </summary>
    public sealed class ReminderEnginePatternTests
    {
        private static readonly DateTimeOffset T0 =
            new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);

        private static ReminderRule PatternRule(string pattern, double cooldown)
        {
            return new ReminderRule(pattern.TrimEnd('*'), pattern, true, ReminderMode.Log, cooldown);
        }

        [Fact]
        public void PatternRule_MatchesBaseStates()
        {
            var engine = new ReminderEngine(new[] { PatternRule("bleeding*", 60) });

            var result = engine.Update(new[] { "bleeding1", "bleeding2" }, T0);
            Assert.Single(result);
            Assert.Equal("bleeding*", result[0].State);
        }

        [Fact]
        public void PatternRule_ContinuousPresence_DoesNotRepeatFirstReminder()
        {
            var engine = new ReminderEngine(new[] { PatternRule("bleeding*", 60) });

            Assert.Single(engine.Update(new[] { "bleeding1" }, T0));
            Assert.Empty(engine.Update(new[] { "bleeding1", "bleeding2" }, T0.AddSeconds(1)));
        }

        [Fact]
        public void PatternRule_DisappearThenReappear_Retriggers()
        {
            var engine = new ReminderEngine(new[] { PatternRule("bleeding*", 0) });

            Assert.Single(engine.Update(new[] { "bleeding1" }, T0));
            Assert.Empty(engine.Update(Array.Empty<string>(), T0.AddSeconds(10)));
            Assert.Single(engine.Update(new[] { "bleeding2" }, T0.AddSeconds(20)));
        }

        [Fact]
        public void BaseNameRule_MatchesIntensityStates()
        {
            var engine = new ReminderEngine(new[] { PatternRule("bleeding", 60) });

            Assert.Single(engine.Update(new[] { "bleeding2" }, T0));
        }
    }
}