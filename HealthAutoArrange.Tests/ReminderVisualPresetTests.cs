using HealthAutoArrange.Core;
using Xunit;

namespace HealthAutoArrange.Tests
{
    /// <summary>
    /// 提醒视觉预设：SubtleBottom（默认）/SubtleTop/CriticalCenter/CompactBottomLeft，
    /// 所有数值有合理 clamp。
    /// </summary>
    public sealed class ReminderVisualPresetTests
    {
        [Fact]
        public void Default_IsSubtleBottom()
        {
            var preset = ReminderVisualPreset.Default();
            Assert.Equal(ReminderVisualPresetKind.SubtleBottom, preset.Kind);
            Assert.Equal(ReminderPlacementPreset.Bottom, preset.Placement.Preset);
        }

        [Fact]
        public void FourPresets_DefineValues()
        {
            var subtle = ReminderVisualPreset.SubtleBottom();
            Assert.Equal(ReminderPlacementPreset.Bottom, subtle.Placement.Preset);
            Assert.InRange(subtle.Opacity, 0f, 1f);
            Assert.True(subtle.DurationSeconds > 0);

            var top = ReminderVisualPreset.SubtleTop();
            Assert.Equal(ReminderPlacementPreset.Top, top.Placement.Preset);

            var critical = ReminderVisualPreset.CriticalCenter();
            Assert.Equal(ReminderPlacementPreset.Center, critical.Placement.Preset);
            Assert.True(critical.Opacity >= 0.8f);

            var compact = ReminderVisualPreset.CompactBottomLeft();
            Assert.Equal(ReminderPlacementPreset.BottomLeft, compact.Placement.Preset);
        }

        [Fact]
        public void Values_AreClamped()
        {
            var preset = new ReminderVisualPreset(
                ReminderVisualPresetKind.SubtleBottom,
                ReminderPlacements.Center(),
                opacity: 2f,
                durationSeconds: -5f,
                fadeInSeconds: -1f,
                fadeOutSeconds: 999f,
                fontSize: 3,
                maxWidth: 10f);

            Assert.Equal(1f, preset.Opacity, 3);
            Assert.Equal(ReminderVisualPreset.MinDurationSeconds, preset.DurationSeconds, 3);
            Assert.Equal(0f, preset.FadeInSeconds, 3);
            Assert.Equal(ReminderVisualPreset.MaxFadeSeconds, preset.FadeOutSeconds, 3);
            Assert.Equal(ReminderVisualPreset.MinFontSize, preset.FontSize);
            Assert.Equal(ReminderVisualPreset.MinMaxWidth, preset.MaxWidth, 3);
        }

        [Fact]
        public void FromKind_MapsCorrectly()
        {
            Assert.Equal(ReminderVisualPresetKind.SubtleTop, ReminderVisualPreset.FromKind(ReminderVisualPresetKind.SubtleTop).Kind);
            Assert.Equal(ReminderVisualPresetKind.CriticalCenter, ReminderVisualPreset.FromKind(ReminderVisualPresetKind.CriticalCenter).Kind);
            Assert.Equal(ReminderVisualPresetKind.CompactBottomLeft, ReminderVisualPreset.FromKind(ReminderVisualPresetKind.CompactBottomLeft).Kind);
            Assert.Equal(ReminderVisualPresetKind.SubtleBottom, ReminderVisualPreset.FromKind(ReminderVisualPresetKind.SubtleBottom).Kind);
        }


        [Fact]
        public void NonFiniteVisualValues_FallBackToFiniteDefaults()
        {
            var preset = new ReminderVisualPreset(
                ReminderVisualPresetKind.SubtleBottom,
                ReminderPlacements.Bottom(),
                float.NaN,
                float.PositiveInfinity,
                float.NaN,
                float.NegativeInfinity,
                16,
                float.NaN);

            Assert.True(NumericSafety.IsFinite(preset.Opacity));
            Assert.True(NumericSafety.IsFinite(preset.DurationSeconds));
            Assert.True(NumericSafety.IsFinite(preset.FadeInSeconds));
            Assert.True(NumericSafety.IsFinite(preset.FadeOutSeconds));
            Assert.True(NumericSafety.IsFinite(preset.MaxWidth));
        }
    }
}