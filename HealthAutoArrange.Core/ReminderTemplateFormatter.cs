using System;
using System.Globalization;
using System.Text.RegularExpressions;

namespace HealthAutoArrange.Core
{
    /// <summary>
    /// 提醒渲染上下文：一次提醒对应的捕获元数据 + 分组信息。
    /// 纯 C#，无 Unity 依赖。
    /// </summary>
    public sealed class ReminderRenderContext
    {
        /// <summary>运行时 id（如 "bleeding3"）。</summary>
        public string RuntimeId { get; }

        /// <summary>基础状态名（如 "bleeding"）。</summary>
        public string BaseId { get; }

        /// <summary>游戏捕获的实际显示名（AddMoodle name，可能为空）。</summary>
        public string DisplayName { get; }

        /// <summary>所属分组名（可能为空）。</summary>
        public string GroupName { get; }

        /// <summary>强度。</summary>
        public int Intensity { get; }

        public ReminderRenderContext(string runtimeId, string displayName, string groupName, int intensity, string baseId = null)
        {
            RuntimeId = MoodleIdentity.NormalizeRuntimeId(runtimeId);
            BaseId = !string.IsNullOrWhiteSpace(baseId)
                ? MoodleIdentity.NormalizeRuntimeId(baseId)
                : MoodleIdentity.BaseId(RuntimeId);
            DisplayName = displayName ?? string.Empty;
            GroupName = groupName ?? string.Empty;
            Intensity = intensity;
        }
    }

    /// <summary>
    /// 提醒模板渲染：支持 {name} {id} {intensity} {group}（不区分大小写）。
    /// 未知占位符原样保留；空模板默认 "{name}"；
    /// 显示名为空时回退 BaseId，再回退 runtime id。
    /// 纯 C#，无 Unity 依赖，可单元测试。
    /// </summary>
    public static class ReminderTemplateFormatter
    {
        /// <summary>默认模板。</summary>
        public const string DefaultTemplate = "{name}";

        /// <summary>
        /// 渲染模板。空/空白模板使用默认 "{name}"。
        /// </summary>
        public static string Render(string template, ReminderRenderContext context)
        {
            if (context == null) throw new ArgumentNullException(nameof(context));
            var text = string.IsNullOrWhiteSpace(template) ? DefaultTemplate : template;

            var name = !string.IsNullOrWhiteSpace(context.DisplayName)
                ? context.DisplayName
                : (!string.IsNullOrWhiteSpace(context.BaseId) ? context.BaseId : context.RuntimeId);

            text = Replace(text, @"\{name\}", name);
            text = Replace(text, @"\{id\}", context.RuntimeId);
            text = Replace(text, @"\{intensity\}", context.Intensity >= 0 ? context.Intensity.ToString(CultureInfo.InvariantCulture) : "?");
            text = Replace(text, @"\{group\}", context.GroupName);
            return text;
        }

        private static string Replace(string text, string pattern, string value)
        {
            return Regex.Replace(text, pattern, _ => value, RegexOptions.IgnoreCase);
        }
    }
}