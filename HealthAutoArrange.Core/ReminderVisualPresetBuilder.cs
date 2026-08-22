namespace HealthAutoArrange.Core
{
    /// <summary>
    /// 从 UI 提醒模型构建视觉预设：先按 PresetKind 取默认（淡入/淡出/字号/最大宽度），
    /// 再覆盖 Opacity / DurationSeconds / Placement。所有数值由 ReminderVisualPreset 构造器 clamp。
    /// 纯 C#，无 Unity 依赖，可单元测试。
    /// </summary>
    public static class ReminderVisualPresetBuilder
    {
        /// <summary>
        /// 构建视觉预设。null 模型回退默认 SubtleBottom。
        /// </summary>
        public static ReminderVisualPreset Build(UiReminderModel model)
        {
            if (model == null) return ReminderVisualPreset.Default();

            var basePreset = ReminderVisualPreset.FromKind(model.PresetKind);
            var placement = model.Placement ?? basePreset.Placement;
            return new ReminderVisualPreset(
                model.PresetKind,
                placement,
                model.Opacity,
                model.DurationSeconds,
                basePreset.FadeInSeconds,
                basePreset.FadeOutSeconds,
                basePreset.FontSize,
                basePreset.MaxWidth);
        }
    }
}