using System;
using System.Collections.Generic;

namespace HealthAutoArrange.Core
{
    /// <summary>
    /// 组内排序模式（v1.2.3）：
    /// - <see cref="RuleIndex"/>：组内按规则文件声明顺序（历史行为）；
    /// - <see cref="IntensityDesc"/>：组内按当前效果强度从高到低（默认；同级回退规则顺序）；
    /// - <see cref="IntensityAsc"/>：组内按当前效果强度从低到高（同级回退规则顺序）。
    /// 强度来源：AddMoodle 捕获的 intensity，缺失时回退 runtime id 末尾数字
    /// （游戏 Moodle.type = 图标名 + 强度，如 "pain2" → 2）。
    /// </summary>
    public enum InGroupSortMode
    {
        RuleIndex = 0,
        IntensityDesc = 1,
        IntensityAsc = 2,
    }

    /// <summary>
    /// 渲染/重排模式（移植自 MoodleSorter_Source 的 RenderMode）。
    /// </summary>
    public enum RenderMode
    {
        /// <summary>自动检测：LayoutGroup → anchoredPosition slots → sibling index。</summary>
        Auto = 0,

        /// <summary>通过 SetSiblingIndex 重排层级。</summary>
        SiblingOrder = 1,

        /// <summary>通过交换 anchoredPosition 槽位重排。</summary>
        AnchoredPosition = 2,
    }

    /// <summary>
    /// 扫描到的一个 Moodle UI 条目（纯数据，供排序决策使用）。
    /// </summary>
    public sealed class MoodleRowItem
    {
        /// <summary>runtime id（Moodle.type）。</summary>
        public string RuntimeId { get; set; }

        /// <summary>所在行：true = side 行，false = main 行。</summary>
        public bool IsSide { get; set; }

        /// <summary>当前效果强度（0-8）；未知为 -1（排序时按最低处理）。</summary>
        public int Intensity { get; set; } = -1;

        /// <summary>在输入列表中的原始索引。</summary>
        public int OriginalIndex { get; set; }
    }

    /// <summary>
    /// 行隔离排序决策核心（移植自 MoodleSorter_Source 的按行分组排序思路）：
    /// 对 main/side 两行分别应用现有 SortPlan，未知状态沿用配置的 End/Keep 策略。
    /// 纯 C#，无 Unity 依赖，可单元测试。
    /// </summary>
    public static class MoodleSortPlanner
    {
        /// <summary>
        /// 对条目按行分组（保持行内原始顺序），对每行应用排序计划。
        /// 返回 key=IsSide，value=该行排序后的原始索引列表（索引指向该行输入列表）。
        /// 两行键始终存在（可能为空列表）。
        /// </summary>
        public static IReadOnlyDictionary<bool, IReadOnlyList<int>> PlanRows(
            IReadOnlyList<MoodleRowItem> items, SortPlan plan)
        {
            if (items == null) throw new ArgumentNullException(nameof(items));
            if (plan == null) throw new ArgumentNullException(nameof(plan));

            var main = new List<MoodleRowItem>();
            var side = new List<MoodleRowItem>();
            foreach (var item in items)
            {
                if (item == null) continue;
                if (item.IsSide) side.Add(item);
                else main.Add(item);
            }

            var result = new Dictionary<bool, IReadOnlyList<int>>
            {
                [false] = plan.Apply(ToRuntimeIds(main), ToIntensities(main)),
                [true] = plan.Apply(ToRuntimeIds(side), ToIntensities(side)),
            };
            return result;
        }

        private static IReadOnlyList<string> ToRuntimeIds(List<MoodleRowItem> items)
        {
            var ids = new string[items.Count];
            for (var i = 0; i < items.Count; i++) ids[i] = items[i].RuntimeId;
            return ids;
        }

        private static IReadOnlyList<int> ToIntensities(List<MoodleRowItem> items)
        {
            // v1.2.3: per-member effect strength for the in-group intensity ordering.
            // Unknown stays -1 (SortPlan treats it as the lowest strength).
            var intensities = new int[items.Count];
            for (var i = 0; i < items.Count; i++) intensities[i] = items[i].Intensity;
            return intensities;
        }
    }

    /// <summary>
    /// 渲染模式解析（移植自 MoodleSorter_Source 的 UiReorderer.ResolveMode）：
    /// Auto 模式下优先检测 Horizontal/Vertical/GridLayoutGroup → sibling order；
    /// 否则若全部条目有 anchoredPosition 且位置互不相同 → anchoredPosition slots；
    /// 最后回退 sibling index。显式模式直接生效。
    /// 纯 C#，无 Unity 依赖，可单元测试。
    /// </summary>
    public static class RenderModeResolver
    {
        public static RenderMode Resolve(
            RenderMode requested,
            bool hasLayoutGroup,
            bool hasDistinctAnchoredPositions)
        {
            if (requested != RenderMode.Auto) return requested;
            if (hasLayoutGroup) return RenderMode.SiblingOrder;
            if (hasDistinctAnchoredPositions) return RenderMode.AnchoredPosition;
            return RenderMode.SiblingOrder;
        }
    }
}