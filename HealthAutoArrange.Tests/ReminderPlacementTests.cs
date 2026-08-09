using HealthAutoArrange.Core;
using Xunit;

namespace HealthAutoArrange.Tests
{
    /// <summary>
    /// 提醒位置模型：预设（Bottom/Top/Center/BottomLeft）+ Custom 归一化坐标/像素偏移。
    /// </summary>
    public sealed class ReminderPlacementTests
    {
        [Fact]
        public void Bottom_IsBottomCenter()
        {
            var p = ReminderPlacements.Bottom();
            Assert.Equal(ReminderPlacementPreset.Bottom, p.Preset);
            Assert.Equal(0.5f, p.NormalizedX, 3);
            Assert.Equal(1f, p.NormalizedY, 3);
        }

        [Fact]
        public void Custom_NormalizedCoords_Clamped()
        {
            var p = ReminderPlacements.Custom(1.7f, -0.3f, 10f, 20f);
            Assert.Equal(ReminderPlacementPreset.Custom, p.Preset);
            Assert.Equal(1f, p.NormalizedX, 3);
            Assert.Equal(0f, p.NormalizedY, 3);
        }

        [Fact]
        public void Custom_PixelOffsets_Clamped()
        {
            var p = ReminderPlacements.Custom(0.5f, 0.5f, 99999f, -99999f);
            Assert.Equal(ReminderPlacement.MaxPixelOffset, p.PixelOffsetX, 0);
            Assert.Equal(ReminderPlacement.MinPixelOffset, p.PixelOffsetY, 0);
        }

        [Fact]
        public void Presets_AllAvailable()
        {
            Assert.Equal(ReminderPlacementPreset.Top, ReminderPlacements.Top().Preset);
            Assert.Equal(ReminderPlacementPreset.Center, ReminderPlacements.Center().Preset);
            Assert.Equal(ReminderPlacementPreset.BottomLeft, ReminderPlacements.BottomLeft().Preset);
        }


        [Fact]
        public void Custom_NonFiniteValues_FallBackToSafeFiniteCoordinates()
        {
            var p = ReminderPlacements.Custom(float.NaN, float.PositiveInfinity, float.NaN, float.NegativeInfinity);
            Assert.Equal(0.5f, p.NormalizedX, 3);
            Assert.Equal(0.5f, p.NormalizedY, 3);
            Assert.Equal(0f, p.PixelOffsetX, 3);
            Assert.Equal(0f, p.PixelOffsetY, 3);
        }
    }
}