using System;

namespace HealthAutoArrange.Core
{
    /// <summary>
    /// 提醒位置预设：Bottom（默认底部居中）、Top、Center、BottomLeft、Custom。
    /// </summary>
    public enum ReminderPlacementPreset
    {
        /// <summary>底部居中（默认）。</summary>
        Bottom = 0,

        /// <summary>顶部居中。</summary>
        Top = 1,

        /// <summary>屏幕中央。</summary>
        Center = 2,

        /// <summary>左下角。</summary>
        BottomLeft = 3,

        /// <summary>自定义：归一化屏幕坐标 + 像素偏移。</summary>
        Custom = 4,
    }

    /// <summary>
    /// 提醒位置模型：预设 + 归一化屏幕坐标（0..1）+ 像素偏移。
    /// 纯 C#，无 Unity 依赖，供后续 IMGUI renderer 使用。
    /// </summary>
    public sealed class ReminderPlacement
    {
        /// <summary>归一化坐标下限。</summary>
        public const float MinNormalized = 0f;

        /// <summary>归一化坐标上限。</summary>
        public const float MaxNormalized = 1f;

        /// <summary>像素偏移下限。</summary>
        public const float MinPixelOffset = -2000f;

        /// <summary>像素偏移上限。</summary>
        public const float MaxPixelOffset = 2000f;

        /// <summary>位置预设。</summary>
        public ReminderPlacementPreset Preset { get; }

        /// <summary>归一化 X（0..1，已 clamp）。</summary>
        public float NormalizedX { get; }

        /// <summary>归一化 Y（0..1，已 clamp）。</summary>
        public float NormalizedY { get; }

        /// <summary>像素偏移 X（已 clamp）。</summary>
        public float PixelOffsetX { get; }

        /// <summary>像素偏移 Y（已 clamp）。</summary>
        public float PixelOffsetY { get; }

        public ReminderPlacement(
            ReminderPlacementPreset preset,
            float normalizedX,
            float normalizedY,
            float pixelOffsetX,
            float pixelOffsetY)
        {
            Preset = preset;
            NormalizedX = NumericSafety.ClampFinite(normalizedX, MinNormalized, MaxNormalized, 0.5f);
            NormalizedY = NumericSafety.ClampFinite(normalizedY, MinNormalized, MaxNormalized, 0.5f);
            PixelOffsetX = NumericSafety.ClampFinite(pixelOffsetX, MinPixelOffset, MaxPixelOffset, 0f);
            PixelOffsetY = NumericSafety.ClampFinite(pixelOffsetY, MinPixelOffset, MaxPixelOffset, 0f);
        }
    }

    /// <summary>位置预设工厂。</summary>
    public static class ReminderPlacements
    {
        /// <summary>底部居中（默认）。</summary>
        public static ReminderPlacement Bottom()
        {
            return new ReminderPlacement(ReminderPlacementPreset.Bottom, 0.5f, 1f, 0f, -20f);
        }

        /// <summary>顶部居中。</summary>
        public static ReminderPlacement Top()
        {
            return new ReminderPlacement(ReminderPlacementPreset.Top, 0.5f, 0f, 0f, 20f);
        }

        /// <summary>屏幕中央。</summary>
        public static ReminderPlacement Center()
        {
            return new ReminderPlacement(ReminderPlacementPreset.Center, 0.5f, 0.5f, 0f, 0f);
        }

        /// <summary>左下角。</summary>
        public static ReminderPlacement BottomLeft()
        {
            return new ReminderPlacement(ReminderPlacementPreset.BottomLeft, 0f, 1f, 12f, -12f);
        }

        /// <summary>自定义：归一化坐标 + 像素偏移（均自动 clamp）。</summary>
        public static ReminderPlacement Custom(float normalizedX, float normalizedY, float pixelOffsetX, float pixelOffsetY)
        {
            return new ReminderPlacement(ReminderPlacementPreset.Custom, normalizedX, normalizedY, pixelOffsetX, pixelOffsetY);
        }
    }
}