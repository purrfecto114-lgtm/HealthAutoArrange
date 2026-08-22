using System;
using System.Collections.Generic;

namespace HealthAutoArrange.Core
{
    /// <summary>
    /// 配置中的一条状态匹配模式：模式文本 + 排序优先级（分组顺序、组内顺序）。
    /// 模式文本支持三种匹配方式（由文本自身决定）：
    /// 1. 以 '#' 结尾 → 严重度族，只匹配基础 ID 本身或其后的纯数字强度后缀（如 "bleeding#"）；
    /// 2. 以 '*' 结尾 → 传统 prefix 通配符，匹配所有以该前缀开头的状态（如 "bleeding*"）；
    /// 3. 其余模式 → 先精确匹配（忽略大小写），再去除状态末尾数字后与模式比较；
    ///    但若模式自身以数字结尾（如第三方 id "drug2"），末尾数字视为语义，只允许精确匹配。
    /// '#' 是 GUI 新生成规则的安全默认；'*' 保留用于旧配置和手工广义前缀匹配。
    /// </summary>
    public sealed class StatePattern
    {
        public string Pattern { get; }
        public int Group { get; }
        public int Index { get; }

        public StatePattern(string pattern, int group, int index)
        {
            if (pattern == null) throw new ArgumentNullException(nameof(pattern));
            Pattern = pattern;
            Group = group;
            Index = index;
        }
    }

    /// <summary>
    /// 配置驱动的状态匹配器：按声明顺序（分组顺序、组内顺序）保存模式，
    /// 对游戏状态执行 exact / prefix 通配符 / 去末尾数字基础名 匹配。
    /// 首个匹配的模式生效（首次声明优先），未命中返回 false。
    /// </summary>
    public sealed class StateMatcher
    {
        private readonly IReadOnlyList<StatePattern> _patterns;

        public StateMatcher(IEnumerable<StatePattern> patterns)
        {
            if (patterns == null) throw new ArgumentNullException(nameof(patterns));
            _patterns = new List<StatePattern>(patterns);
        }

        /// <summary>
        /// 由精确优先级字典构造匹配器（兼容旧 API：每个键作为一条模式）。
        /// </summary>
        public static StateMatcher FromExact(
            IReadOnlyDictionary<string, (int Group, int Index)> priorities)
        {
            if (priorities == null) throw new ArgumentNullException(nameof(priorities));

            var patterns = new List<StatePattern>(priorities.Count);
            foreach (var kv in priorities)
            {
                patterns.Add(new StatePattern(kv.Key, kv.Value.Group, kv.Value.Index));
            }
            // 字典迭代顺序不确定，按优先级排序保证确定性。
            patterns.Sort((a, b) =>
            {
                int c = a.Group.CompareTo(b.Group);
                return c != 0 ? c : a.Index.CompareTo(b.Index);
            });
            return new StateMatcher(patterns);
        }

        /// <summary>
        /// 尝试为状态查找优先级。命中返回 true 并输出 (Group, Index)。
        /// </summary>
        public bool TryGetPriority(string state, out (int Group, int Index) priority)
        {
            if (state == null)
            {
                priority = default;
                return false;
            }

            foreach (var p in _patterns)
            {
                if (Matches(p.Pattern, state))
                {
                    priority = (p.Group, p.Index);
                    return true;
                }
            }

            priority = default;
            return false;
        }

        private static bool Matches(string pattern, string state)
        {
            return MatchesPattern(pattern, state);
        }

        /// <summary>
        /// 公开的匹配判定：exact / 严重度族（"bleeding#"）/ prefix 通配符（"bleeding*"）/ 去末尾数字基础名。
        /// 供配置层（如组名解析）复用同一套匹配规则。
        /// </summary>
        public static bool MatchesPattern(string pattern, string state)
        {
            if (pattern == null) throw new ArgumentNullException(nameof(pattern));
            if (string.IsNullOrEmpty(state)) return false;

            if (pattern.EndsWith("#", StringComparison.Ordinal))
            {
                var prefix = pattern.Substring(0, pattern.Length - 1);
                if (prefix.Length == 0) return false;
                if (string.Equals(prefix, state, StringComparison.OrdinalIgnoreCase)) return true;
                if (!state.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) return false;

                var suffix = state.Substring(prefix.Length);
                if (suffix.Length == 0) return true;
                for (int i = 0; i < suffix.Length; i++)
                {
                    if (!char.IsDigit(suffix[i])) return false;
                }
                return true;
            }

            if (pattern.EndsWith("*", StringComparison.Ordinal))
            {
                var prefix = pattern.Substring(0, pattern.Length - 1);
                return state.StartsWith(prefix, StringComparison.OrdinalIgnoreCase);
            }

            if (string.Equals(pattern, state, StringComparison.OrdinalIgnoreCase))
                return true;

            // 其余模式默认先精确匹配，再去掉状态末尾数字与模式基础名比较。
            // 但当模式自身以数字结尾时，末尾数字视为语义（如第三方 id "drug2"），
            // 只允许精确匹配，避免 "drug2" 误匹配 "drug21"。
            if (pattern.Length > 0 && char.IsDigit(pattern[pattern.Length - 1]))
                return false;

            // 去除状态末尾数字后与模式基础名比较（如 "bleeding1" → "bleeding"）
            var baseName = StripTrailingDigits(state);
            return baseName.Length > 0
                && string.Equals(pattern, baseName, StringComparison.OrdinalIgnoreCase);
        }

        private static string StripTrailingDigits(string s)
        {
            int end = s.Length;
            while (end > 0 && s[end - 1] >= '0' && s[end - 1] <= '9') end--;
            return s.Substring(0, end);
        }
    }
}