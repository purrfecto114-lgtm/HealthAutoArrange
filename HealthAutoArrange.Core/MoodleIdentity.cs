using System;
using System.Text;

namespace HealthAutoArrange.Core
{
    /// <summary>
    /// Moodle 运行时标识工具（移植自 MoodleSorter_Source 的 MoodleIdentity）：
    /// runtime id 规范化、基础名提取、末尾强度解析、期望 runtime id 计算。
    /// 纯 C#，无 Unity 依赖，可单元测试。
    /// 游戏 Moodle.type 为 图标名+强度后缀（如 "bleeding1"、"braindamage3"）。
    /// </summary>
    public static class MoodleIdentity
    {
        /// <summary>
        /// 规范化 runtime id：小写化并仅保留字母/数字/./_/-。
        /// </summary>
        public static string NormalizeRuntimeId(string value)
        {
            if (string.IsNullOrWhiteSpace(value)) return string.Empty;
            var builder = new System.Text.StringBuilder(value.Length);
            foreach (var ch in value.Trim())
            {
                if (char.IsLetterOrDigit(ch) || ch == '.' || ch == '_' || ch == '-')
                    builder.Append(char.ToLowerInvariant(ch));
            }
            return builder.ToString();
        }

        /// <summary>
        /// 基础名：去除末尾数字（如 "bleeding1" → "bleeding"）。
        /// </summary>
        public static string BaseId(string runtimeId)
        {
            var normalized = NormalizeRuntimeId(runtimeId);
            if (normalized.Length == 0) return normalized;
            var end = normalized.Length;
            while (end > 0 && char.IsDigit(normalized[end - 1])) end--;
            return normalized.Substring(0, end);
        }

        /// <summary>
        /// 将配置模式还原为“观察到的基础标识”。由 UI 生成的通配模式
        /// （例如 mod123*）必须保留语义数字 123；旧的非通配配置仍按
        /// runtime id 兼容规则去除末尾强度数字。
        /// </summary>
        public static string PatternBaseId(string pattern)
        {
            if (string.IsNullOrWhiteSpace(pattern)) return string.Empty;
            var trimmed = pattern.Trim();
            if (trimmed.EndsWith("*", StringComparison.Ordinal))
                return NormalizeRuntimeId(trimmed.Substring(0, trimmed.Length - 1));
            return BaseId(trimmed);
        }

        /// <summary>
        /// 解析末尾强度数字；无末尾数字时返回 fallback。
        /// </summary>
        public static int ParseTrailingIntensity(string runtimeId, int fallback)
        {
            var normalized = NormalizeRuntimeId(runtimeId);
            if (normalized.Length == 0) return fallback;
            var start = normalized.Length;
            while (start > 0 && char.IsDigit(normalized[start - 1])) start--;
            if (start == normalized.Length) return fallback;
            int value;
            return int.TryParse(normalized.Substring(start), out value) ? value : fallback;
        }

        /// <summary>
        /// 由图标名与强度计算期望的 runtime id（与游戏 moodle.type = icon + intensity 一致）。
        /// </summary>
        public static string ExpectedRuntimeId(string iconId, int intensity)
        {
            return NormalizeRuntimeId((iconId ?? string.Empty) + intensity);
        }
    }
}