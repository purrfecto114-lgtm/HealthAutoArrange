using System.Collections.Generic;

namespace HealthAutoArrange.Core
{
    /// <summary>
    /// 完整配置模型：分组顺序、各组状态、未知状态策略与提醒规则。
    /// 状态和分组名称全部来自配置文本，不在代码中固化。
    /// </summary>
    public sealed class ArrangeConfig
    {
        public IReadOnlyList<string> GroupOrder { get; }
        public IReadOnlyDictionary<string, IReadOnlyList<string>> GroupStates { get; }
        public UnknownStatePolicy UnknownStatePolicy { get; }
        public IReadOnlyList<ReminderRule> Reminders { get; }

        public ArrangeConfig(
            IReadOnlyList<string> groupOrder,
            IReadOnlyDictionary<string, IReadOnlyList<string>> groupStates,
            UnknownStatePolicy unknownStatePolicy,
            IReadOnlyList<ReminderRule> reminders)
        {
            GroupOrder = groupOrder ?? throw new System.ArgumentNullException(nameof(groupOrder));
            GroupStates = groupStates ?? throw new System.ArgumentNullException(nameof(groupStates));
            UnknownStatePolicy = unknownStatePolicy;
            Reminders = reminders ?? throw new System.ArgumentNullException(nameof(reminders));
        }

        /// <summary>
        /// 由配置生成排序计划。状态条目即匹配模式（支持 exact、prefix 通配符如 "bleeding*"、
        /// 以及去除末尾数字后与基础名匹配）；重复模式采用首次声明（按分组顺序与组内顺序遍历）。
        /// </summary>
        public SortPlan CreateSortPlan()
        {
            var patterns = new List<StatePattern>();
            var seen = new HashSet<string>(System.StringComparer.OrdinalIgnoreCase);
            for (int g = 0; g < GroupOrder.Count; g++)
            {
                if (!GroupStates.TryGetValue(GroupOrder[g], out var states)) continue;
                for (int i = 0; i < states.Count; i++)
                {
                    if (seen.Add(states[i]))
                    {
                        patterns.Add(new StatePattern(states[i], g, i));
                    }
                }
            }
            return new SortPlan(new StateMatcher(patterns), UnknownStatePolicy);
        }

        /// <summary>
        /// 解析状态所属的分组名：按分组顺序遍历，返回首个包含匹配模式的分组；
        /// 未命中返回空字符串。供提醒渲染上下文使用。
        /// </summary>
        public string ResolveGroupName(string state)
        {
            if (string.IsNullOrEmpty(state)) return string.Empty;
            foreach (var group in GroupOrder)
            {
                if (!GroupStates.TryGetValue(group, out var states)) continue;
                foreach (var pattern in states)
                {
                    if (StateMatcher.MatchesPattern(pattern, state)) return group;
                }
            }
            return string.Empty;
        }
    }
}