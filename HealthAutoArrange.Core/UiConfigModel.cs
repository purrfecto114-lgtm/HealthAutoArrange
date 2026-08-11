using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace HealthAutoArrange.Core
{
    /// <summary>供配置界面编辑的可变纯数据模型。</summary>
    public sealed class UiConfigModel
    {
        public bool Enabled { get; set; } = true;
        public List<string> GroupOrder { get; set; } = new List<string>();
        public List<UiGroupModel> Groups { get; } = new List<UiGroupModel>();
        public UnknownStatePolicy UnknownStatePolicy { get; set; } = UnknownStatePolicy.Keep;
        public List<UiReminderModel> Reminders { get; } = new List<UiReminderModel>();

        public static UiConfigModel FromConfig(ArrangeConfig config, bool enabled)
        {
            if (config == null) throw new ArgumentNullException(nameof(config));
            var model = new UiConfigModel
            {
                Enabled = enabled,
                GroupOrder = config.GroupOrder.ToList(),
                UnknownStatePolicy = config.UnknownStatePolicy
            };
            foreach (var name in config.GroupOrder)
            {
                if (config.GroupStates.TryGetValue(name, out var states))
                    model.Groups.Add(new UiGroupModel(name, string.Join(", ", states)));
            }
            foreach (var group in config.GroupStates)
            {
                if (!model.Groups.Any(x => string.Equals(x.Name, group.Key, StringComparison.OrdinalIgnoreCase)))
                    model.Groups.Add(new UiGroupModel(group.Key, string.Join(", ", group.Value)));
            }
            foreach (var rule in config.Reminders)
                model.Reminders.Add(new UiReminderModel(
                    rule.Name,
                    rule.Enabled,
                    rule.Mode,
                    rule.RepeatMode,
                    rule.PeriodSeconds,
                    rule.SendsPerPeriod));
            return model;
        }

        /// <summary>Create an independent editable copy for UI staging/rollback.</summary>
        public UiConfigModel Clone()
        {
            var copy = new UiConfigModel
            {
                Enabled = Enabled,
                GroupOrder = new List<string>(GroupOrder ?? new List<string>()),
                UnknownStatePolicy = UnknownStatePolicy
            };
            foreach (var group in Groups)
            {
                if (group != null) copy.Groups.Add(new UiGroupModel(group.Name, group.StatesText));
            }
            foreach (var rule in Reminders)
            {
                if (rule == null) continue;
                var cloned = new UiReminderModel(rule.Name, rule.Enabled, rule.Mode, rule.RepeatMode, rule.PeriodSeconds, rule.SendsPerPeriod)
                {
                    Template = rule.Template,
                    PresetKind = rule.PresetKind,
                    Opacity = rule.Opacity,
                    DurationSeconds = rule.DurationSeconds,
                    Placement = rule.Placement == null ? null : new ReminderPlacement(
                        rule.Placement.Preset,
                        rule.Placement.NormalizedX,
                        rule.Placement.NormalizedY,
                        rule.Placement.PixelOffsetX,
                        rule.Placement.PixelOffsetY)
                };
                copy.Reminders.Add(cloned);
            }
            return copy;
        }

        public ArrangeConfig ToConfig()
        {
            Normalize();
            var groups = new Dictionary<string, IReadOnlyList<string>>(StringComparer.OrdinalIgnoreCase);
            foreach (var group in Groups)
                groups[group.Name] = Split(group.StatesText).ToList();
            return new ArrangeConfig(
                GroupOrder.Where(x => !string.IsNullOrWhiteSpace(x)).Select(x => x.Trim()).Distinct(StringComparer.OrdinalIgnoreCase).ToList(),
                groups,
                UnknownStatePolicy,
                Reminders.Select(x => new ReminderRule(
                    x.Name,
                    x.Name,
                    x.Enabled,
                    x.Mode,
                    x.RepeatMode,
                    x.PeriodSeconds,
                    x.SendsPerPeriod)).ToList());
        }

        /// <summary>将当前已生成的分组模式转换为状态选择编辑器。</summary>
        public GroupSelectionEditor CreateSelectionEditor()
        {
            var editor = new GroupSelectionEditor();
            foreach (var group in Groups)
            {
                if (group == null) continue;
                editor.EnsureGroup(group.Name);
                var stateBases = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                foreach (var state in Split(group.StatesText))
                {
                    var baseId = MoodleIdentity.PatternBaseId(state);
                    if (baseId.Length > 0 && stateBases.Add(baseId)) editor.AddState(group.Name, baseId);
                }
            }
            editor.Normalize();
            return editor;
        }

        /// <summary>将状态选择编辑器结果写回 UI 分组模型。</summary>
        public void ApplySelectionEditor(GroupSelectionEditor editor)
        {
            if (editor == null) throw new ArgumentNullException(nameof(editor));
            editor.Normalize();

            // 保真映射：(分组名, 基础 id) → 原有模式文本。旧配置的 '*' 通配与 exact 模式
            // 在无关修改/保存时必须原样保留，不能被静默改写为 GUI 新生成的 '#'。
            var originalByBase = new Dictionary<string, Dictionary<string, string>>(
                StringComparer.OrdinalIgnoreCase);
            foreach (var group in Groups)
            {
                if (group == null || string.IsNullOrWhiteSpace(group.Name)) continue;
                var byBase = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                foreach (var state in Split(group.StatesText))
                {
                    var baseId = MoodleIdentity.PatternBaseId(state);
                    if (baseId.Length > 0 && !byBase.ContainsKey(baseId))
                        byBase[baseId] = state;
                }
                originalByBase[group.Name.Trim()] = byBase;
            }

            Groups.Clear();
            GroupOrder = new List<string>();
            foreach (var group in editor.Groups)
            {
                var name = (group.Name ?? string.Empty).Trim();
                if (name.Length == 0) continue;
                GroupOrder.Add(name);

                var patterns = new List<string>();
                originalByBase.TryGetValue(name, out var byBase);
                foreach (var state in group.States)
                {
                    var baseId = MoodleIdentity.NormalizeRuntimeId(state);
                    if (baseId.Length == 0) continue;
                    // 已有模式保留原样；新目录分配才生成严重度族 "#" 模式。
                    if (byBase != null
                        && byBase.TryGetValue(baseId, out var original)
                        && original.Length > 0)
                    {
                        patterns.Add(original);
                    }
                    else
                    {
                        patterns.Add(baseId + "#");
                    }
                }
                Groups.Add(new UiGroupModel(name, string.Join(", ", patterns)));
            }
        }

        /// <summary>
        /// 清理 UI 编辑产生的空白项，并按首次出现顺序消除重复名称。
        /// 名称比较不区分大小写，保留首项的其它字段。
        /// </summary>
        public void Normalize()
        {
            var order = new List<string>();
            var orderSeen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var item in GroupOrder ?? new List<string>())
            {
                var name = (item ?? string.Empty).Trim();
                if (name.Length > 0 && orderSeen.Add(name)) order.Add(name);
            }
            GroupOrder = order;

            var groups = new List<UiGroupModel>();
            var groupSeen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var group in Groups)
            {
                if (group == null) continue;
                var name = (group.Name ?? string.Empty).Trim();
                if (name.Length == 0 || !groupSeen.Add(name)) continue;
                group.Name = name;
                group.StatesText = (group.StatesText ?? string.Empty).Trim();
                groups.Add(group);
            }
            Groups.Clear();
            Groups.AddRange(groups);

            var reminders = new List<UiReminderModel>();
            var reminderSeen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var reminder in Reminders)
            {
                if (reminder == null) continue;
                var name = (reminder.Name ?? string.Empty).Trim();
                if (name.Length == 0 || !reminderSeen.Add(name)) continue;
                reminder.Name = name;
                reminder.PeriodSeconds = ReminderRule.NormalizePeriod(reminder.PeriodSeconds);
                reminder.SendsPerPeriod = ReminderRule.NormalizeSends(reminder.PeriodSeconds, reminder.SendsPerPeriod);
                reminder.Opacity = NumericSafety.ClampFinite(reminder.Opacity, 0f, 1f, 0.55f);
                reminder.DurationSeconds = NumericSafety.ClampFinite(reminder.DurationSeconds, 0.1f, 600f, 5f);
                if (reminder.Placement == null)
                    reminder.Placement = ReminderVisualPreset.FromKind(reminder.PresetKind).Placement;
                reminders.Add(reminder);
            }
            Reminders.Clear();
            Reminders.AddRange(reminders);
        }

        internal static IEnumerable<string> Split(string value)
        {
            return (value ?? string.Empty).Split(',').Select(x => x.Trim()).Where(x => x.Length > 0);
        }
    }

    public sealed class UiGroupModel
    {
        public string Name { get; set; }
        public string StatesText { get; set; }

        public UiGroupModel(string name, string statesText)
        {
            Name = name ?? string.Empty;
            StatesText = statesText ?? string.Empty;
        }
    }

    public sealed class UiReminderModel
    {
        public string Name { get; set; }
        public bool Enabled { get; set; }
        public ReminderMode Mode { get; set; }
        public ReminderRepeatMode RepeatMode { get; set; }
        public double PeriodSeconds { get; set; }
        public int SendsPerPeriod { get; set; }
        public string Template { get; set; }
        public ReminderVisualPresetKind PresetKind { get; set; }
        public float Opacity { get; set; }
        public float DurationSeconds { get; set; }
        public ReminderPlacement Placement { get; set; }

        /// <summary>
        /// Legacy v1.1.1 constructor. Positive cooldown becomes one send per cooldown period;
        /// zero becomes Once to avoid the historical every-refresh spam behaviour.
        /// </summary>
        public UiReminderModel(string name, bool enabled, ReminderMode mode, double cooldownSeconds)
            : this(
                name,
                enabled,
                mode,
                cooldownSeconds > 0d ? ReminderRepeatMode.WhilePresent : ReminderRepeatMode.Once,
                cooldownSeconds > 0d ? cooldownSeconds : ReminderRule.DefaultPeriodSeconds,
                ReminderRule.DefaultSendsPerPeriod)
        {
        }

        public UiReminderModel(
            string name,
            bool enabled,
            ReminderMode mode,
            ReminderRepeatMode repeatMode,
            double periodSeconds,
            int sendsPerPeriod)
        {
            Name = name ?? string.Empty;
            Enabled = enabled;
            Mode = mode;
            RepeatMode = repeatMode;
            PeriodSeconds = ReminderRule.NormalizePeriod(periodSeconds);
            SendsPerPeriod = ReminderRule.NormalizeSends(PeriodSeconds, sendsPerPeriod);
            Template = ReminderTemplateFormatter.DefaultTemplate;
            ApplyPreset(ReminderVisualPresetKind.SubtleBottom);
        }

        public double EffectiveIntervalSeconds => RepeatMode == ReminderRepeatMode.WhilePresent
            ? PeriodSeconds / SendsPerPeriod
            : double.PositiveInfinity;

        /// <summary>
        /// Legacy v1.1.1 compatibility view used by older callers/tests. A repeating rule reports
        /// its effective interval; Once reports 0. New code should use RepeatMode/PeriodSeconds/
        /// SendsPerPeriod instead.
        /// </summary>
        public double CooldownSeconds => RepeatMode == ReminderRepeatMode.WhilePresent
            ? EffectiveIntervalSeconds
            : 0d;

        public void ApplyPreset(ReminderVisualPresetKind kind)
        {
            var preset = ReminderVisualPreset.FromKind(kind);
            PresetKind = kind;
            Opacity = preset.Opacity;
            DurationSeconds = preset.DurationSeconds;
            Placement = preset.Placement;
        }
    }

    /// <summary>将 UI 模型写回 BepInEx 风格配置文本。</summary>
    public static class UiConfigTextSerializer
    {
        public static string Serialize(UiConfigModel model)
        {
            if (model == null) throw new ArgumentNullException(nameof(model));
            model.Normalize();
            var lines = new List<string>
            {
                "Enabled = " + model.Enabled.ToString().ToLowerInvariant(),
                "GroupOrder = " + string.Join(", ", model.GroupOrder ?? new List<string>()),
                "UnknownStatePolicy = " + model.UnknownStatePolicy
            };
            foreach (var group in model.Groups)
                lines.Add("Group." + (group.Name ?? string.Empty).Trim() + ".States = " + (group.StatesText ?? string.Empty).Trim());
            foreach (var rule in model.Reminders)
            {
                lines.Add("Reminder." + (rule.Name ?? string.Empty).Trim() + ".Enabled = " + rule.Enabled.ToString().ToLowerInvariant());
                lines.Add("Reminder." + (rule.Name ?? string.Empty).Trim() + ".Mode = " + rule.Mode);
                lines.Add("Reminder." + (rule.Name ?? string.Empty).Trim() + ".RepeatMode = " + rule.RepeatMode);
                lines.Add("Reminder." + (rule.Name ?? string.Empty).Trim() + ".PeriodSeconds = "
                    + rule.PeriodSeconds.ToString("0.################", CultureInfo.InvariantCulture));
                lines.Add("Reminder." + (rule.Name ?? string.Empty).Trim() + ".SendsPerPeriod = "
                    + rule.SendsPerPeriod.ToString(CultureInfo.InvariantCulture));
                lines.Add("Reminder." + (rule.Name ?? string.Empty).Trim() + ".Template = " + (rule.Template ?? ReminderTemplateFormatter.DefaultTemplate));
                lines.Add("Reminder." + (rule.Name ?? string.Empty).Trim() + ".Preset = " + rule.PresetKind);
                lines.Add("Reminder." + (rule.Name ?? string.Empty).Trim() + ".Opacity = " + rule.Opacity.ToString("0.####", CultureInfo.InvariantCulture));
                lines.Add("Reminder." + (rule.Name ?? string.Empty).Trim() + ".DurationSeconds = " + rule.DurationSeconds.ToString("0.####", CultureInfo.InvariantCulture));
                lines.Add("Reminder." + (rule.Name ?? string.Empty).Trim() + ".PlacementPreset = " + rule.Placement.Preset);
                lines.Add("Reminder." + (rule.Name ?? string.Empty).Trim() + ".NormalizedX = " + rule.Placement.NormalizedX.ToString("0.####", CultureInfo.InvariantCulture));
                lines.Add("Reminder." + (rule.Name ?? string.Empty).Trim() + ".NormalizedY = " + rule.Placement.NormalizedY.ToString("0.####", CultureInfo.InvariantCulture));
                lines.Add("Reminder." + (rule.Name ?? string.Empty).Trim() + ".PixelOffsetX = " + rule.Placement.PixelOffsetX.ToString("0.####", CultureInfo.InvariantCulture));
                lines.Add("Reminder." + (rule.Name ?? string.Empty).Trim() + ".PixelOffsetY = " + rule.Placement.PixelOffsetY.ToString("0.####", CultureInfo.InvariantCulture));
            }
            return string.Join(Environment.NewLine, lines) + Environment.NewLine;
        }

        public static UiConfigModel Parse(string text)
        {
            if (text == null) throw new ArgumentNullException(nameof(text));
            var result = ConfigTextParser.Parse(text);
            var enabled = true;
            foreach (var line in text.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries))
            {
                var parts = line.Split(new[] { '=' }, 2);
                if (parts.Length == 2 && string.Equals(parts[0].Trim(), "Enabled", StringComparison.OrdinalIgnoreCase))
                {
                    if (bool.TryParse(parts[1].Trim(), out var parsedEnabled))
                        enabled = parsedEnabled;
                }
            }
            var model = UiConfigModel.FromConfig(result.Config, enabled);
            var values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (var line in text.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries))
            {
                var parts = line.Split(new[] { '=' }, 2);
                if (parts.Length == 2) values[parts[0].Trim()] = parts[1].Trim();
            }
            foreach (var reminder in model.Reminders)
            {
                var prefix = "Reminder." + reminder.Name + ".";
                if (values.TryGetValue(prefix + "Template", out var template)) reminder.Template = template;
                if (values.TryGetValue(prefix + "Preset", out var preset)
                    && Enum.TryParse(preset, true, out ReminderVisualPresetKind presetKind))
                    reminder.ApplyPreset(presetKind);
                if (values.TryGetValue(prefix + "Opacity", out var opacity)
                    && float.TryParse(opacity, NumberStyles.Float, CultureInfo.InvariantCulture, out var opacityValue))
                    reminder.Opacity = NumericSafety.IsFinite(opacityValue) ? opacityValue : reminder.Opacity;
                if (values.TryGetValue(prefix + "DurationSeconds", out var duration)
                    && float.TryParse(duration, NumberStyles.Float, CultureInfo.InvariantCulture, out var durationValue))
                    reminder.DurationSeconds = NumericSafety.IsFinite(durationValue) ? durationValue : reminder.DurationSeconds;
                var placement = reminder.Placement;
                var placementPreset = placement.Preset;
                if (values.TryGetValue(prefix + "PlacementPreset", out var placementText)
                    && Enum.TryParse(placementText, true, out ReminderPlacementPreset parsedPlacementPreset))
                    placementPreset = parsedPlacementPreset;
                var x = ReadFloat(values, prefix + "NormalizedX", placement.NormalizedX);
                var y = ReadFloat(values, prefix + "NormalizedY", placement.NormalizedY);
                var px = ReadFloat(values, prefix + "PixelOffsetX", placement.PixelOffsetX);
                var py = ReadFloat(values, prefix + "PixelOffsetY", placement.PixelOffsetY);
                reminder.Placement = new ReminderPlacement(placementPreset, x, y, px, py);
            }
            return model;
        }

        private static float ReadFloat(Dictionary<string, string> values, string key, float fallback)
        {
            if (!values.TryGetValue(key, out var text)) return fallback;
            if (!float.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var value)) return fallback;
            return NumericSafety.IsFinite(value) ? value : fallback;
        }
    }
}
