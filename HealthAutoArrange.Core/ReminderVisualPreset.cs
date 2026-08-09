using System;

namespace HealthAutoArrange.Core
{
    /// <summary>
    /// 提醒显示预设种类：SubtleBottom（默认）、SubtleTop、CriticalCenter、CompactBottomLeft。
    /// </summary>
    public enum ReminderVisualPresetKind
    {
        /// <summary>底部居中、半透明（默认）。</summary>
        SubtleBottom = 0,

        /// <summary>顶部居中、半透明。</summary>
        SubtleTop = 1,

        /// <summary>屏幕中央、高不透明度（关键状态）。</summary>
        CriticalCenter = 2,

        /// <summary>左下角、紧凑小字。</summary>
        CompactBottomLeft = 3,
    }

    /// <summary>
    /// 提醒显示预设：位置、不透明度、时长、淡入/淡出、字号、最大宽度。
    /// 所有数值在构造时 clamp 到合理范围。纯 C#，无 Unity 依赖。
    /// </summary>
    public sealed class ReminderVisualPreset
    {
        /// <summary>不透明度下限。</summary>
        public const float MinOpacity = 0f;

        /// <summary>不透明度上限。</summary>
        public const float MaxOpacity = 1f;

        /// <summary>停留时长下限（秒）。</summary>
        public const float MinDurationSeconds = 0.1f;

        /// <summary>停留时长上限（秒）。</summary>
        public const float MaxDurationSeconds = 600f;

        /// <summary>淡入/淡出时长下限（秒）。</summary>
        public const float MinFadeSeconds = 0f;

        /// <summary>淡入/淡出时长上限（秒）。</summary>
        public const float MaxFadeSeconds = 120f;

        /// <summary>字号下限。</summary>
        public const int MinFontSize = 6;

        /// <summary>字号上限。</summary>
        public const int MaxFontSize = 72;

        /// <summary>最大宽度下限（像素）。</summary>
        public const float MinMaxWidth = 50f;

        /// <summary>最大宽度上限（像素）。</summary>
        public const float MaxMaxWidth = 4000f;

        /// <summary>预设种类。</summary>
        public ReminderVisualPresetKind Kind { get; }

        /// <summary>位置。</summary>
        public ReminderPlacement Placement { get; }

        /// <summary>停留期不透明度（0..1，已 clamp）。</summary>
        public float Opacity { get; }

        /// <summary>停留时长（秒，已 clamp）。</summary>
        public float DurationSeconds { get; }

        /// <summary>淡入时长（秒，已 clamp）。</summary>
        public float FadeInSeconds { get; }

        /// <summary>淡出时长（秒，已 clamp）。</summary>
        public float FadeOutSeconds { get; }

        /// <summary>字号（已 clamp）。</summary>
        public int FontSize { get; }

        /// <summary>最大宽度（像素，已 clamp）。</summary>
        public float MaxWidth { get; }

        public ReminderVisualPreset(
            ReminderVisualPresetKind kind,
            ReminderPlacement placement,
            float opacity,
            float durationSeconds,
            float fadeInSeconds,
            float fadeOutSeconds,
            int fontSize,
            float maxWidth)
        {
            Kind = kind;
            Placement = placement ?? throw new ArgumentNullException(nameof(placement));
            Opacity = NumericSafety.ClampFinite(opacity, MinOpacity, MaxOpacity, 0.55f);
            DurationSeconds = NumericSafety.ClampFinite(durationSeconds, MinDurationSeconds, MaxDurationSeconds, 5f);
            FadeInSeconds = NumericSafety.ClampFinite(fadeInSeconds, MinFadeSeconds, MaxFadeSeconds, 0.3f);
            FadeOutSeconds = NumericSafety.ClampFinite(fadeOutSeconds, MinFadeSeconds, MaxFadeSeconds, 0.6f);
            FontSize = (int)Math.Max(MinFontSize, Math.Min(MaxFontSize, fontSize));
            MaxWidth = NumericSafety.ClampFinite(maxWidth, MinMaxWidth, MaxMaxWidth, 480f);
        }

        /// <summary>默认预设：SubtleBottom。</summary>
        public static ReminderVisualPreset Default()
        {
            return SubtleBottom();
        }

        /// <summary>底部居中、半透明（默认）。</summary>
        public static ReminderVisualPreset SubtleBottom()
        {
            return new ReminderVisualPreset(
                ReminderVisualPresetKind.SubtleBottom, ReminderPlacements.Bottom(),
                0.55f, 5f, 0.3f, 0.6f, 16, 480f);
        }

        /// <summary>顶部居中、半透明。</summary>
        public static ReminderVisualPreset SubtleTop()
        {
            return new ReminderVisualPreset(
                ReminderVisualPresetKind.SubtleTop, ReminderPlacements.Top(),
                0.5f, 4f, 0.3f, 0.5f, 16, 480f);
        }

        /// <summary>屏幕中央、高不透明度（关键状态）。</summary>
        public static ReminderVisualPreset CriticalCenter()
        {
            return new ReminderVisualPreset(
                ReminderVisualPresetKind.CriticalCenter, ReminderPlacements.Center(),
                0.9f, 6f, 0.2f, 0.8f, 22, 640f);
        }

        /// <summary>左下角、紧凑小字。</summary>
        public static ReminderVisualPreset CompactBottomLeft()
        {
            return new ReminderVisualPreset(
                ReminderVisualPresetKind.CompactBottomLeft, ReminderPlacements.BottomLeft(),
                0.7f, 3f, 0.2f, 0.4f, 13, 320f);
        }

        /// <summary>按种类取预设；未知种类回退 SubtleBottom。</summary>
        public static ReminderVisualPreset FromKind(ReminderVisualPresetKind kind)
        {
            switch (kind)
            {
                case ReminderVisualPresetKind.SubtleTop:
                    return SubtleTop();
                case ReminderVisualPresetKind.CriticalCenter:
                    return CriticalCenter();
                case ReminderVisualPresetKind.CompactBottomLeft:
                    return CompactBottomLeft();
                default:
                    return SubtleBottom();
            }
        }
    }
}