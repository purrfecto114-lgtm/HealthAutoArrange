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
        private readonly InGroupSortMode _inGroupSortMode;

        /// <summary>历史行为构造（组内按规则声明顺序）：保持既有调用方与测试兼容。</summary>
        public SortPlan(StateMatcher matcher, UnknownStatePolicy policy)
            : this(matcher, policy, InGroupSortMode.RuleIndex)
        {
        }

        /// <summary>v1.2.3 构造：可指定组内排序模式（强度降/升序或规则顺序）。</summary>
        public SortPlan(StateMatcher matcher, UnknownStatePolicy policy, InGroupSortMode inGroupSortMode)
        {
            _matcher = matcher ?? throw new ArgumentNullException(nameof(matcher));
            _policy = policy;
            _inGroupSortMode = inGroupSortMode;
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
            return Apply(states, null);
        }

        /// <summary>
        /// v1.2.3：带每个状态当前效果强度的排序。<paramref name="intensities"/>
        /// 可为 null 或较短（视为未知 -1）；组内排序模式为 <see cref="InGroupSortMode.RuleIndex"/>
        /// 时强度被忽略，行为与旧版完全一致。
        /// </summary>
        public IReadOnlyList<int> Apply(IReadOnlyList<string> states, IReadOnlyList<int> intensities)
        {
            if (states == null) throw new ArgumentNullException(nameof(states));

            int n = states.Count;
            if (n == 0) return Array.Empty<int>();

            var result = new int[n];
            var occupied = new bool[n];
            var known = new List<(int StateIndex, int Group, int Index, int Intensity)>();

            for (int i = 0; i < n; i++)
            {
                if (_matcher.TryGetPriority(states[i], out var p))
                {
                    int intensity = (intensities != null && i < intensities.Count) ? intensities[i] : -1;
                    known.Add((i, p.Group, p.Index, intensity));
                }
                else if (_policy == UnknownStatePolicy.Keep)
                {
                    // 未知状态保持原位
                    result[i] = i;
                    occupied[i] = true;
                }
            }

            // 已知状态按 (Group, 组内键, 声明顺序) 稳定排序。
            // 组内键 v1.2.3：RuleIndex → 规则声明顺序；IntensityDesc/Asc → 当前效果强度
            //（未知 -1 在两种方向下都排在已知强度之后；同级回退规则声明顺序）。
            known.Sort((a, b) =>
            {
                int c = a.Group.CompareTo(b.Group);
                if (c != 0) return c;
                c = CompareInGroup(a, b);
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

        /// <summary>组内比较键：强度模式把未知 (-1) 视作两端最低优先级。</summary>
        private int CompareInGroup(
            (int StateIndex, int Group, int Index, int Intensity) a,
            (int StateIndex, int Group, int Index, int Intensity) b)
        {
            switch (_inGroupSortMode)
            {
                case InGroupSortMode.IntensityDesc:
                {
                    // 未知 (-1) 视作最低强度：排到已知强度之后。
                    int av = a.Intensity < 0 ? int.MinValue : a.Intensity;
                    int bv = b.Intensity < 0 ? int.MinValue : b.Intensity;
                    return bv.CompareTo(av);
                }
                case InGroupSortMode.IntensityAsc:
                {
                    // 升序时未知同样排最后：映射为 int.MaxValue。
                    int av = a.Intensity < 0 ? int.MaxValue : a.Intensity;
                    int bv = b.Intensity < 0 ? int.MaxValue : b.Intensity;
                    return av.CompareTo(bv);
                }
                default:
                    return a.Index.CompareTo(b.Index);
            }
        }
    }
}