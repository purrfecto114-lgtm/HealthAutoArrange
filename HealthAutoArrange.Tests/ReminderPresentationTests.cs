using System;
using HealthAutoArrange.Core;
using Xunit;

namespace HealthAutoArrange.Tests
{
    /// <summary>
    /// 提醒展示/队列调度：接收 ReminderMessage + 上下文生成可显示项；
    /// 同基础状态在同一冷却周期内不重复入队；多状态可排队；
    /// 计算指定时间 alpha（淡入/停留/淡出）与过期清理。
    /// </summary>
    public sealed class ReminderPresentationTests
    {
        private static readonly DateTimeOffset T0 =
            new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);

        private static ReminderMessage Msg(string state = "bleeding*", string rule = "bleeding")
        {
            return new ReminderMessage(rule, state, ReminderMode.Log, T0);
        }

        private static ReminderRenderContext Ctx(
            string id = "bleeding3", string name = "Bleeding", string group = "Vital", int intensity = 3)
        {
            return new ReminderRenderContext(id, name, group, intensity);
        }

        private static ReminderVisualPreset TimedPreset()
        {
            return new ReminderVisualPreset(
                ReminderVisualPresetKind.SubtleBottom,
                ReminderPlacements.Bottom(),
                opacity: 0.8f,
                durationSeconds: 4f,
                fadeInSeconds: 1f,
                fadeOutSeconds: 1f,
                fontSize: 16,
                maxWidth: 480f);
        }

        [Fact]
        public void Enqueue_FormatsText_WithContext()
        {
            var presentation = new ReminderPresentation();
            var item = presentation.Enqueue(Msg(), Ctx(), T0, cooldownSeconds: 60);

            Assert.NotNull(item);
            Assert.Equal("Bleeding", item.Text);
            Assert.Equal("bleeding", item.BaseState);
        }

        [Fact]
        public void SameBaseState_WithinCooldown_Deduped()
        {
            var presentation = new ReminderPresentation();
            var first = presentation.Enqueue(Msg(), Ctx(), T0, cooldownSeconds: 60);
            var second = presentation.Enqueue(Msg(), Ctx(), T0.AddSeconds(10), cooldownSeconds: 60);

            Assert.NotNull(first);
            Assert.Null(second);
        }

        [Fact]
        public void SameBaseState_AfterCooldown_Enqueued()
        {
            var presentation = new ReminderPresentation();
            Assert.NotNull(presentation.Enqueue(Msg(), Ctx(), T0, cooldownSeconds: 60));
            var after = presentation.Enqueue(Msg(), Ctx(), T0.AddSeconds(70), cooldownSeconds: 60);

            Assert.NotNull(after);
        }


        [Fact]
        public void SameBaseState_NewAuthoritativeSend_ReplacesActiveVisualInsteadOfStacking()
        {
            var presentation = new ReminderPresentation();
            presentation.Enqueue(Msg(), Ctx(), T0, cooldownSeconds: 0, preset: TimedPreset());
            var second = presentation.Enqueue(Msg(), Ctx("bleeding3", "Bleeding again"), T0.AddSeconds(1), cooldownSeconds: 0, preset: TimedPreset());

            Assert.NotNull(second);
            Assert.Single(presentation.Active(T0.AddSeconds(1)));
            Assert.Equal("Bleeding again", second.Text);
        }

        [Fact]
        public void WildcardBase_PreservesMeaningfulTrailingDigitsForVisualDedupe()
        {
            var presentation = new ReminderPresentation();
            var item = presentation.Enqueue(
                Msg("drug2*", "drug2"),
                Ctx("drug20", "Drug 2"),
                T0,
                cooldownSeconds: 0);

            Assert.Equal("drug2", item.BaseState);
        }

        [Fact]
        public void DifferentStates_QueueIndependently()
        {
            var presentation = new ReminderPresentation();
            var bleeding = presentation.Enqueue(Msg("bleeding*", "bleeding"), Ctx("bleeding3", "Bleeding"), T0, 60);
            var infection = presentation.Enqueue(Msg("infection*", "infection"), Ctx("infection2", "Infection"), T0, 60);

            Assert.NotNull(bleeding);
            Assert.NotNull(infection);
            Assert.Equal(2, presentation.Active(T0).Count);
        }

        [Fact]
        public void Active_RemovesExpired()
        {
            var presentation = new ReminderPresentation();
            presentation.Enqueue(Msg(), Ctx(), T0, 60, TimedPreset());

            Assert.Single(presentation.Active(T0));
            Assert.Empty(presentation.Active(T0.AddSeconds(7)));
        }

        [Fact]
        public void Alpha_FadeInBoundary()
        {
            var presentation = new ReminderPresentation();
            var item = presentation.Enqueue(Msg(), Ctx(), T0, 60, TimedPreset());

            Assert.Equal(0f, presentation.Alpha(item, T0), 3);
            Assert.Equal(0.4f, presentation.Alpha(item, T0.AddSeconds(0.5)), 3);
            Assert.Equal(0.8f, presentation.Alpha(item, T0.AddSeconds(1)), 3);
        }

        [Fact]
        public void Alpha_Hold()
        {
            var presentation = new ReminderPresentation();
            var item = presentation.Enqueue(Msg(), Ctx(), T0, 60, TimedPreset());

            Assert.Equal(0.8f, presentation.Alpha(item, T0.AddSeconds(3)), 3);
        }

        [Fact]
        public void Alpha_FadeOut()
        {
            var presentation = new ReminderPresentation();
            var item = presentation.Enqueue(Msg(), Ctx(), T0, 60, TimedPreset());

            Assert.Equal(0.8f, presentation.Alpha(item, T0.AddSeconds(5)), 3);
            Assert.Equal(0.4f, presentation.Alpha(item, T0.AddSeconds(5.5)), 3);
            Assert.Equal(0f, presentation.Alpha(item, T0.AddSeconds(6)), 3);
        }

        [Fact]
        public void Alpha_AfterExpiry_Zero()
        {
            var presentation = new ReminderPresentation();
            var item = presentation.Enqueue(Msg(), Ctx(), T0, 60, TimedPreset());

            Assert.Equal(0f, presentation.Alpha(item, T0.AddSeconds(10)), 3);
        }

        [Fact]
        public void CustomTemplate_Used()
        {
            var presentation = new ReminderPresentation();
            var item = presentation.Enqueue(Msg(), Ctx(), T0, 60, template: "状态 {name} [{id}]");

            Assert.Equal("状态 Bleeding [bleeding3]", item.Text);
        }

        [Fact]
        public void Preview_IgnoresFormalCooldown()
        {
            var presentation = new ReminderPresentation();
            presentation.Enqueue(Msg(), Ctx(), T0, cooldownSeconds: 60);

            var preview = presentation.Preview(Ctx(), T0.AddSeconds(10));

            Assert.NotNull(preview);
        }

        [Fact]
        public void Preview_DoesNotBlockFormalReminder()
        {
            var presentation = new ReminderPresentation();

            presentation.Preview(Ctx(), T0);

            var formal = presentation.Enqueue(Msg(), Ctx(), T0.AddSeconds(5), cooldownSeconds: 60);

            Assert.NotNull(formal);
        }

        [Fact]
        public void Preview_ReplacesPreviousPreviewForSameState()
        {
            var presentation = new ReminderPresentation();
            presentation.Preview(Ctx(), T0);

            var second = presentation.Preview(Ctx("bleeding3", "Bleeding v2"), T0.AddSeconds(1));

            Assert.NotNull(second);
            Assert.Single(presentation.Active(T0.AddSeconds(1)));
            Assert.Equal("Bleeding v2", second.Text);
        }

        [Fact]
        public void Preview_DoesNotDeleteActiveFormalReminderForSameState()
        {
            var presentation = new ReminderPresentation();
            presentation.Enqueue(Msg(), Ctx(), T0, cooldownSeconds: 60);

            var preview = presentation.Preview(Ctx("bleeding3", "Preview bleeding"), T0.AddSeconds(1));

            Assert.NotNull(preview);
            var active = presentation.Active(T0.AddSeconds(1));
            Assert.Equal(2, active.Count);
            Assert.Contains(active, i => i.BaseState == "bleeding" && i.Text == "Bleeding");
            Assert.Contains(active, i => i.BaseState == "bleeding" && i.Text == "Preview bleeding");
        }

        [Fact]
        public void Preview_ReplacesOldPreviewButKeepsFormalReminder()
        {
            var presentation = new ReminderPresentation();
            presentation.Enqueue(Msg(), Ctx(), T0, cooldownSeconds: 60);
            presentation.Preview(Ctx("bleeding3", "Preview v1"), T0.AddSeconds(1));

            var second = presentation.Preview(Ctx("bleeding3", "Preview v2"), T0.AddSeconds(2));

            Assert.NotNull(second);
            var active = presentation.Active(T0.AddSeconds(2));
            Assert.Equal(2, active.Count);
            Assert.Contains(active, i => i.BaseState == "bleeding" && i.Text == "Bleeding");
            Assert.Contains(active, i => i.BaseState == "bleeding" && i.Text == "Preview v2");
            Assert.DoesNotContain(active, i => i.Text == "Preview v1");
        }
    }
}