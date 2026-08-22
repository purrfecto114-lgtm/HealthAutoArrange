using HealthAutoArrange.Core;
using Xunit;

namespace HealthAutoArrange.Tests
{
    /// <summary>
    /// 视觉预设构建：从 UiReminderModel 生成 ReminderVisualPreset。
    /// 先按 PresetKind 取默认（淡入/淡出/字号/最大宽度），再覆盖 Opacity/Duration/Placement。
    /// </summary>
    public sealed class ReminderVisualPresetBuilderTests
    {
        [Fact]
        public void Build_AppliesModelOverrides_OnKindDefaults()
        {
            var model = new UiReminderModel("bleeding", true, ReminderMode.Log, 60);
            model.ApplyPreset(ReminderVisualPresetKind.CriticalCenter);
            model.Opacity = 0.42f;
            model.DurationSeconds = 9f;
            model.Placement = ReminderPlacements.BottomLeft();

            var preset = ReminderVisualPresetBuilder.Build(model);

            Assert.Equal(ReminderVisualPresetKind.CriticalCenter, preset.Kind);
            Assert.Equal(ReminderPlacementPreset.BottomLeft, preset.Placement.Preset);
            Assert.Equal(0.42f, preset.Opacity, 3);
            Assert.Equal(9f, preset.DurationSeconds, 3);
            // 种类默认值保留：CriticalCenter 的字号 22 与最大宽度 640。
            Assert.Equal(22, preset.FontSize);
            Assert.Equal(640f, preset.MaxWidth, 3);
            Assert.Equal(0.2f, preset.FadeInSeconds, 3);
        }

        [Fact]
        public void Build_NullModel_ReturnsDefault()
        {
            var preset = ReminderVisualPresetBuilder.Build(null);

            Assert.Equal(ReminderVisualPresetKind.SubtleBottom, preset.Kind);
            Assert.Equal(ReminderPlacementPreset.Bottom, preset.Placement.Preset);
        }

        [Fact]
        public void Build_ClampsOutOfRangeValues()
        {
            var model = new UiReminderModel("bleeding", true, ReminderMode.Log, 60);
            model.Opacity = 5f;
            model.DurationSeconds = -3f;
            model.Placement = ReminderPlacements.Custom(9f, 9f, 99999f, -99999f);

            var preset = ReminderVisualPresetBuilder.Build(model);

            Assert.Equal(ReminderVisualPreset.MaxOpacity, preset.Opacity, 3);
            Assert.Equal(ReminderVisualPreset.MinDurationSeconds, preset.DurationSeconds, 3);
            Assert.Equal(1f, preset.Placement.NormalizedX, 3);
            Assert.Equal(ReminderPlacement.MaxPixelOffset, preset.Placement.PixelOffsetX, 3);
        }
    }
}