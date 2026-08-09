using System;
using System.Collections.Generic;
using System.Linq;

namespace HealthAutoArrange.Core
{
    /// <summary>
    /// 一个可显示的提醒项：已渲染文本 + 视觉预设 + 入队时间。
    /// 纯 C#，无 Unity 依赖。
    /// </summary>
    public sealed class ReminderPresentationItem
    {
        /// <summary>基础状态（去重键）。</summary>
        public string BaseState { get; }

        /// <summary>已渲染文本。</summary>
        public string Text { get; }

        /// <summary>视觉预设。</summary>
        public ReminderVisualPreset Preset { get; }

        /// <summary>入队时间。</summary>
        public DateTimeOffset QueuedAt { get; }

        public ReminderPresentationItem(
            string baseState, string text, ReminderVisualPreset preset, DateTimeOffset queuedAt)
        {
            BaseState = baseState ?? string.Empty;
            Text = text ?? string.Empty;
            Preset = preset ?? throw new ArgumentNullException(nameof(preset));
            QueuedAt = queuedAt;
        }
    }

    /// <summary>
    /// 透明提醒展示队列调度器：接收 ReminderMessage + 渲染上下文生成可显示项。
    /// 同一基础状态在同一冷却周期内不能重复入队；不同状态可同时排队；
    /// 计算指定时间的 alpha（淡入、停留、淡出）并清理过期项。
    /// 纯 C#，无 Unity 依赖，供后续 IMGUI renderer 使用。
    /// </summary>
    public sealed class ReminderPresentation
    {
        private readonly List<ReminderPresentationItem> _items = new List<ReminderPresentationItem>();
        private readonly Dictionary<string, DateTimeOffset> _lastEnqueued =
            new Dictionary<string, DateTimeOffset>(StringComparer.OrdinalIgnoreCase);
        private readonly ReminderVisualPreset _defaultPreset;
        private readonly string _defaultTemplate;

        public ReminderPresentation(ReminderVisualPreset defaultPreset = null, string defaultTemplate = null)
        {
            _defaultPreset = defaultPreset ?? ReminderVisualPreset.Default();
            _defaultTemplate = string.IsNullOrWhiteSpace(defaultTemplate)
                ? ReminderTemplateFormatter.DefaultTemplate
                : defaultTemplate;
        }

        /// <summary>
        /// 入队一条提醒。同一基础状态在同一冷却周期内返回 null（去重）。
        /// </summary>
        public ReminderPresentationItem Enqueue(
            ReminderMessage message,
            ReminderRenderContext context,
            DateTimeOffset now,
            double cooldownSeconds,
            ReminderVisualPreset preset = null,
            string template = null)
        {
            if (message == null) throw new ArgumentNullException(nameof(message));
            if (context == null) throw new ArgumentNullException(nameof(context));

            var activePreset = preset ?? _defaultPreset;
            var baseState = BaseOf(message.State);
            if (baseState.Length == 0) baseState = context.BaseId;

            if (_lastEnqueued.TryGetValue(baseState, out var last)
                && (now - last).TotalSeconds < Math.Max(0, cooldownSeconds))
            {
                return null;
            }

            var text = ReminderTemplateFormatter.Render(template ?? _defaultTemplate, context);
            var item = new ReminderPresentationItem(baseState, text, activePreset, now);
            _items.Add(item);
            _lastEnqueued[baseState] = now;
            return item;
        }

        /// <summary>
        /// 入队一条预览：不受正式提醒冷却限制，也不更新正式去重时间戳。
        /// 同一基础状态的旧预览会被替换，避免重复点击堆积。
        /// </summary>
        public ReminderPresentationItem Preview(
            ReminderRenderContext context,
            DateTimeOffset now,
            ReminderVisualPreset preset = null,
            string template = null)
        {
            if (context == null) throw new ArgumentNullException(nameof(context));

            var activePreset = preset ?? _defaultPreset;
            var baseState = context.BaseId;
            if (baseState.Length == 0) baseState = context.RuntimeId;

            var text = ReminderTemplateFormatter.Render(template ?? _defaultTemplate, context);
            _items.RemoveAll(i => string.Equals(i.BaseState, baseState, StringComparison.OrdinalIgnoreCase));
            var item = new ReminderPresentationItem(baseState, text, activePreset, now);
            _items.Add(item);
            return item;
        }

        /// <summary>
        /// 返回当前未过期的项（同时清理过期项）。
        /// </summary>
        public IReadOnlyList<ReminderPresentationItem> Active(DateTimeOffset now)
        {
            _items.RemoveAll(i => IsExpired(i, now));
            return _items.ToArray();
        }

        /// <summary>是否已过期（超过 淡入+停留+淡出 总时长）。</summary>
        public bool IsExpired(ReminderPresentationItem item, DateTimeOffset now)
        {
            if (item == null) throw new ArgumentNullException(nameof(item));
            var total = item.Preset.FadeInSeconds + item.Preset.DurationSeconds + item.Preset.FadeOutSeconds;
            return (now - item.QueuedAt).TotalSeconds >= total;
        }

        /// <summary>
        /// 计算指定时间的 alpha：淡入 0→Opacity，停留 Opacity，淡出 Opacity→0，过期后 0。
        /// </summary>
        public float Alpha(ReminderPresentationItem item, DateTimeOffset now)
        {
            if (item == null) throw new ArgumentNullException(nameof(item));
            var preset = item.Preset;
            var age = (now - item.QueuedAt).TotalSeconds;
            if (age < 0f) return 0f;

            if (preset.FadeInSeconds > 0f && age < preset.FadeInSeconds)
            {
                return preset.Opacity * (float)(age / preset.FadeInSeconds);
            }

            var holdEnd = preset.FadeInSeconds + preset.DurationSeconds;
            if (age < holdEnd)
            {
                return preset.Opacity;
            }

            if (preset.FadeOutSeconds > 0f)
            {
                var fadeEnd = holdEnd + preset.FadeOutSeconds;
                if (age < fadeEnd)
                {
                    return preset.Opacity * (float)((fadeEnd - age) / preset.FadeOutSeconds);
                }
            }

            return 0f;
        }

        private static string BaseOf(string state)
        {
            var s = state ?? string.Empty;
            if (s.EndsWith("*", StringComparison.Ordinal)) s = s.Substring(0, s.Length - 1);
            return MoodleIdentity.BaseId(s);
        }
    }
}