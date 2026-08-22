using System;
using System.Collections.Generic;
using System.Linq;

namespace HealthAutoArrange.Core
{
    /// <summary>一个分组选择：名称 + 已选基础状态列表。</summary>
    public sealed class GroupSelection
    {
        public string Name { get; set; }
        public List<string> States { get; } = new List<string>();

        public GroupSelection(string name)
        {
            Name = name ?? string.Empty;
        }
    }

    /// <summary>加入状态的结果：是否加入、冲突所在分组、诊断消息。</summary>
    public sealed class StateAddResult
    {
        /// <summary>是否成功加入。</summary>
        public bool Added { get; }

        /// <summary>冲突时首次拥有该状态的分组名；无冲突为 null。</summary>
        public string ConflictGroup { get; }

        /// <summary>诊断消息。</summary>
        public string Message { get; }

        public StateAddResult(bool added, string conflictGroup, string message)
        {
            Added = added;
            ConflictGroup = conflictGroup;
            Message = message ?? string.Empty;
        }
    }

    /// <summary>
    /// 去重分组编辑模型：选择项只能属于一个分组；
    /// 重复加入时首次分组优先并返回冲突诊断；组内状态不重复；
    /// 分组名不区分大小写去重；空值归一化。纯 C#，无 Unity 依赖，可单元测试。
    /// </summary>
    public sealed class GroupSelectionEditor
    {
        private readonly List<GroupSelection> _groups = new List<GroupSelection>();

        /// <summary>当前分组（按加入顺序）。</summary>
        public IReadOnlyList<GroupSelection> Groups => _groups;

        /// <summary>Ensure an empty group exists so the UI can assign its first state.</summary>
        public bool EnsureGroup(string groupName)
        {
            var name = (groupName ?? string.Empty).Trim();
            if (name.Length == 0) return false;
            if (_groups.Any(g => string.Equals(g.Name, name, StringComparison.OrdinalIgnoreCase))) return false;
            _groups.Add(new GroupSelection(name));
            return true;
        }

        /// <summary>
        /// 将基础状态加入分组。分组名不区分大小写；状态已属于其它分组时首次分组优先并返回冲突。
        /// </summary>
        public StateAddResult AddState(string groupName, string state)
        {
            var name = (groupName ?? string.Empty).Trim();
            var normalized = MoodleIdentity.NormalizeRuntimeId(state);
            if (name.Length == 0) return new StateAddResult(false, null, "empty group name");
            if (normalized.Length == 0) return new StateAddResult(false, null, "empty state");

            var group = _groups.FirstOrDefault(g => string.Equals(g.Name, name, StringComparison.OrdinalIgnoreCase));

            if (group != null && group.States.Any(s => string.Equals(s, normalized, StringComparison.OrdinalIgnoreCase)))
                return new StateAddResult(false, null, "state already in group");

            // 冲突检查：状态已属于其他分组时首次分组优先，不创建新分组。
            foreach (var other in _groups)
            {
                if (other == group) continue;
                if (other.States.Any(s => string.Equals(s, normalized, StringComparison.OrdinalIgnoreCase)))
                    return new StateAddResult(false, other.Name, $"state '{normalized}' already in group '{other.Name}'");
            }

            if (group == null)
            {
                group = new GroupSelection(name);
                _groups.Add(group);
            }

            group.States.Add(normalized);
            return new StateAddResult(true, null, "added");
        }

        /// <summary>从分组移除状态；成功返回 true。</summary>
        public bool RemoveState(string groupName, string state)
        {
            var group = _groups.FirstOrDefault(g =>
                string.Equals(g.Name, (groupName ?? string.Empty).Trim(), StringComparison.OrdinalIgnoreCase));
            if (group == null) return false;
            var normalized = MoodleIdentity.NormalizeRuntimeId(state);
            return group.States.RemoveAll(s => string.Equals(s, normalized, StringComparison.OrdinalIgnoreCase)) > 0;
        }

        /// <summary>
        /// 空值归一化：移除空分组名/空状态，分组名与组内状态均不区分大小写去重。
        /// </summary>
        public void Normalize()
        {
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var result = new List<GroupSelection>();
            foreach (var group in _groups)
            {
                if (group == null) continue;
                var name = (group.Name ?? string.Empty).Trim();
                if (name.Length == 0 || !seen.Add(name)) continue;
                group.Name = name;

                var states = new List<string>();
                var stateSeen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                foreach (var s in group.States)
                {
                    var normalized = MoodleIdentity.NormalizeRuntimeId(s);
                    if (normalized.Length > 0 && stateSeen.Add(normalized)) states.Add(normalized);
                }
                group.States.Clear();
                group.States.AddRange(states);
                result.Add(group);
            }
            _groups.Clear();
            _groups.AddRange(result);
        }
    }
}