using HealthAutoArrange.Core;
using Xunit;

namespace HealthAutoArrange.Tests
{
    /// <summary>
    /// 提醒模板渲染：{name} {id} {intensity} {group}；未知占位符原样保留；
    /// 空模板默认 {name}；显示名为空回退 BaseId/runtime id。
    /// </summary>
    public sealed class ReminderTemplateFormatterTests
    {
        private static ReminderRenderContext Ctx(
            string id = "bleeding3", string name = "Bleeding", string group = "Vital", int intensity = 3)
        {
            return new ReminderRenderContext(id, name, group, intensity);
        }

        [Fact]
        public void Renders_AllPlaceholders()
        {
            var result = ReminderTemplateFormatter.Render("{name} ({id}, lvl {intensity}, {group})", Ctx());
            Assert.Equal("Bleeding (bleeding3, lvl 3, Vital)", result);
        }

        [Fact]
        public void Chinese_DisplayName_Rendered()
        {
            var result = ReminderTemplateFormatter.Render("{name}", new ReminderRenderContext("bleeding1", "流血", "生命体征", 1));
            Assert.Equal("流血", result);
        }

        [Fact]
        public void EmptyTemplate_DefaultsToName()
        {
            Assert.Equal("Bleeding", ReminderTemplateFormatter.Render("", Ctx()));
            Assert.Equal("Bleeding", ReminderTemplateFormatter.Render(null, Ctx()));
            Assert.Equal("Bleeding", ReminderTemplateFormatter.Render("   ", Ctx()));
        }

        [Fact]
        public void EmptyDisplayName_FallsBackToBaseId()
        {
            var result = ReminderTemplateFormatter.Render("{name}", new ReminderRenderContext("bleeding3", "", "Vital", 3));
            Assert.Equal("bleeding", result);
        }

        [Fact]
        public void EmptyBaseId_FallsBackToRuntimeId()
        {
            var result = ReminderTemplateFormatter.Render("{name}", new ReminderRenderContext("123", "", "", 0));
            Assert.Equal("123", result);
        }

        [Fact]
        public void UnknownPlaceholder_Preserved()
        {
            var result = ReminderTemplateFormatter.Render("{name} {unknown} {id}", Ctx());
            Assert.Equal("Bleeding {unknown} bleeding3", result);
        }

        [Fact]
        public void Placeholders_AreCaseInsensitive()
        {
            var result = ReminderTemplateFormatter.Render("{NAME} {Id}", Ctx());
            Assert.Equal("Bleeding bleeding3", result);
        }

        [Fact]
        public void Intensity_FormattedInvariant()
        {
            var result = ReminderTemplateFormatter.Render("{intensity}", Ctx(intensity: 7));
            Assert.Equal("7", result);
        }


        [Fact]
        public void UnknownIntensity_RendersQuestionMarkInsteadOfGuessingTrailingDigits()
        {
            var ctx = new ReminderRenderContext("modstate1234", "", "", -1, "modstate123");
            Assert.Equal("modstate123 ?", ReminderTemplateFormatter.Render("{name} {intensity}", ctx));
            Assert.Equal("modstate123", ctx.BaseId);
        }
    }
}