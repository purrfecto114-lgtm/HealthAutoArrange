using System;
using System.Collections.Generic;

namespace HealthAutoArrange.Core
{
    /// <summary>
    /// 选择结果生成：由选中的基础状态生成 Group.&lt;name&gt;.States 需要的模式。
    /// 模式由系统生成（baseId + "#"），非玩家手写；未知状态策略保持 End/Keep。
    /// 纯 C#，无 Unity 依赖，可单元测试。
    /// </summary>
    public static class SelectionResultGenerator
    {
        /// <summary>
        /// 生成分组状态字典：分组名 → 通配模式列表（如 "bleeding*"）。
        /// </summary>
        public static IReadOnlyDictionary<string, IReadOnlyList<string>> GenerateGroupStates(
            GroupSelectionEditor editor)
        {
            if (editor == null) throw new ArgumentNullException(nameof(editor));

            var result = new Dictionary<string, IReadOnlyList<string>>(StringComparer.OrdinalIgnoreCase);
            foreach (var group in editor.Groups)
            {
                var name = (group.Name ?? string.Empty).Trim();
                if (name.Length == 0) continue;

                var patterns = new List<string>();
                foreach (var state in group.States)
                {
                    var baseId = MoodleIdentity.NormalizeRuntimeId(state);
                    if (baseId.Length > 0) patterns.Add(baseId + "#");
                }
                result[name] = patterns;
            }
            return result;
        }

        /// <summary>
        /// 生成完整配置：分组顺序 = 分组编辑顺序，组状态 = 通配模式，
        /// 未知状态策略与提醒规则原样保留。
        /// </summary>
        public static ArrangeConfig GenerateConfig(
            GroupSelectionEditor editor,
            UnknownStatePolicy policy,
            System.Collections.Generic.IEnumerable<ReminderRule> reminders)
        {
            if (editor == null) throw new ArgumentNullException(nameof(editor));

            var groupStates = GenerateGroupStates(editor);
            var order = new List<string>();
            foreach (var group in editor.Groups)
            {
                var name = (group.Name ?? string.Empty).Trim();
                if (name.Length > 0 && groupStates.ContainsKey(name)) order.Add(name);
            }

            return new ArrangeConfig(
                order,
                groupStates,
                policy,
                reminders == null
                    ? new List<ReminderRule>()
                    : new List<ReminderRule>(reminders));
        }
    }
}