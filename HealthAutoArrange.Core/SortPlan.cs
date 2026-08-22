using System;
using System.Collections.Generic;

namespace HealthAutoArrange.Core
{
    /// <summary>
    /// 纯 C# 排序核心：输入当前状态列表（按原始顺序），输出排序后的原始索引。
    /// 已知状态按 (分组顺序, 组内顺序) 排序；未知状态保持置于末尾或原位（由策略决定）。
    /// 相同优先级保持原始顺序（稳定排序）。
    /// 状态匹配由 <see cref="StateMatcher"/> 驱动：支持 exact、prefix 通配符（如 "bleeding*"）、
    /// 以及去除末尾数字后与基础名匹配（游戏 Moodle.type 为 图标名+强度后缀，如 "bleeding1"）。
    /// </summary>
    public sealed class SortPlan
    {
        private readonly StateMatcher _matcher;
        private readonly UnknownStatePolicy _policy;

        public SortPlan(StateMatcher matcher, UnknownStatePolicy policy)
        {
            _matcher = matcher ?? throw new ArgumentNullException(nameof(matcher));
            _policy = policy;
        }

        /// <summary>
        /// 兼容旧 API：以精确优先级字典构造排序计划（每个键作为一条匹配模式）。
        /// </summary>
        public SortPlan(
            IReadOnlyDictionary<string, (int Group, int Index)> priorities,
            UnknownStatePolicy policy)
            : this(StateMatcher.FromExact(priorities), policy)
        {
        }

        /// <summary>
        /// 输入当前状态列表（索引即原始位置），返回按排序规则排列后的原始索引列表。
        /// </summary>
        public IReadOnlyList<int> Apply(IReadOnlyList<string> states)
        {
            if (states == null) throw new ArgumentNullException(nameof(states));

            int n = states.Count;
            if (n == 0) return Array.Empty<int>();

            var result = new int[n];
            var occupied = new bool[n];
            var known = new List<(int StateIndex, int Group, int Index)>();

            for (int i = 0; i < n; i++)
            {
                if (_matcher.TryGetPriority(states[i], out var p))
                {
                    known.Add((i, p.Group, p.Index));
                }
                else if (_policy == UnknownStatePolicy.Keep)
                {
                    // 未知状态保持原位
                    result[i] = i;
                    occupied[i] = true;
                }
            }

            // 已知状态按 (Group, Index) 稳定排序
            known.Sort((a, b) =>
            {
                int c = a.Group.CompareTo(b.Group);
                if (c != 0) return c;
                c = a.Index.CompareTo(b.Index);
                return c != 0 ? c : a.StateIndex.CompareTo(b.StateIndex);
            });

            int pos = 0;
            foreach (var k in known)
            {
                while (pos < n && occupied[pos]) pos++;
                result[pos] = k.StateIndex;
                occupied[pos] = true;
            }

            if (_policy == UnknownStatePolicy.End)
            {
                // 未知状态置于末尾，保持原始相对顺序
                for (int i = 0; i < n; i++)
                {
                    if (!_matcher.TryGetPriority(states[i], out _))
                    {
                        while (pos < n && occupied[pos]) pos++;
                        result[pos] = i;
                        occupied[pos] = true;
                    }
                }
            }

            return result;
        }
    }
}