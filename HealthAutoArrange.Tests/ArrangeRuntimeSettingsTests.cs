using HealthAutoArrange.Core;
using Xunit;

namespace HealthAutoArrange.Tests
{
    /// <summary>
    /// 运行时启用开关：UiConfigModel.Enabled 必须传递到运行时决策，
    /// 禁用时排序/提醒应停用（诊断与配置 UI 不受影响）。
    /// </summary>
    public sealed class ArrangeRuntimeSettingsTests
    {
        [Fact]
        public void DefaultsToEnabled()
        {
            var settings = new ArrangeRuntimeSettings();
            Assert.True(settings.Enabled);
            Assert.True(settings.ShouldRun());
        }

        [Fact]
        public void Disabled_ShouldNotRun()
        {
            var settings = new ArrangeRuntimeSettings { Enabled = false };
            Assert.False(settings.Enabled);
            Assert.False(settings.ShouldRun());
        }

        [Fact]
        public void FromModel_DisabledModel_ProducesDisabledSettings()
        {
            var model = new UiConfigModel { Enabled = false };
            var settings = ArrangeRuntimeSettings.FromModel(model);
            Assert.False(settings.Enabled);
            Assert.False(settings.ShouldRun());
        }

        [Fact]
        public void FromModel_EnabledModel_ProducesEnabledSettings()
        {
            var model = new UiConfigModel { Enabled = true };
            var settings = ArrangeRuntimeSettings.FromModel(model);
            Assert.True(settings.Enabled);
            Assert.True(settings.ShouldRun());
        }

        [Fact]
        public void FromModel_Null_Throws()
        {
            Assert.Throws<System.ArgumentNullException>(() => ArrangeRuntimeSettings.FromModel(null));
        }
    }
}