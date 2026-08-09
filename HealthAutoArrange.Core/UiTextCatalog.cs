using System;

namespace HealthAutoArrange.Core
{
    /// <summary>F8 fallback settings window text. Keeps persistent keys/enums unchanged.</summary>
    public sealed class UiTextCatalog
    {
        private readonly bool _chinese;

        private UiTextCatalog(bool chinese) { _chinese = chinese; }

        public static UiTextCatalog ForLanguage(bool chinese) => new UiTextCatalog(chinese);

        public bool IsChinese => _chinese;
        public string WindowTitle => _chinese ? "状态图标自动整理" : "Moodle Auto Arrange";
        public string Basic => _chinese ? "基础设置" : "Basic";
        public string Enabled => _chinese ? "自动整理状态图标" : "Auto arrange moodles";
        public string UnknownStatePolicy => _chinese ? "未识别状态" : "Unknown moodles";
        public string Groups => _chinese ? "排序分组" : "Sort groups";
        public string StateSelection => _chinese ? "状态目录" : "State catalog";
        public string Advanced => _chinese ? "高级设置" : "Advanced";
        public string ReminderRules => _chinese ? "状态提醒（实验性）" : "State reminders (experimental)";
        public string TechnicalEditing => _chinese ? "手动编辑状态 ID" : "Edit state IDs manually";
        public string Name => _chinese ? "名称" : "Name";
        public string States => _chinese ? "状态 ID / 模式" : "State IDs / patterns";
        public string Mode => _chinese ? "输出方式" : "Output";
        public string Cooldown => _chinese ? "冷却" : "Cooldown";
        public string Seconds => _chinese ? "秒" : "sec";
        public string AddGroup => _chinese ? "添加分组" : "Add group";
        public string AddReminder => _chinese ? "添加提醒" : "Add reminder";
        public string Delete => _chinese ? "删除" : "Delete";
        public string Save => _chinese ? "保存并应用" : "Save & apply";
        public string Reload => _chinese ? "放弃修改并重载" : "Discard & reload";
        public string ForceResort => _chinese ? "立即重排" : "Resort now";
        public string Diagnostics => _chinese ? "输出诊断" : "Dump diagnostics";
        public string Close => _chinese ? "关闭" : "Close";
        public string Search => _chinese ? "搜索" : "Search";
        public string All => _chinese ? "全部" : "All";
        public string Unassigned => _chinese ? "未分组" : "Unassigned";
        public string Current => _chinese ? "最近出现（30 秒）" : "Seen recently (30s)";
        public string RefreshStates => _chinese ? "刷新" : "Refresh";
        public string TargetGroup => _chinese ? "加入到" : "Assign to";
        public string Join => _chinese ? "加入" : "Assign";
        public string Move => _chinese ? "移动" : "Move";
        public string Remove => _chinese ? "移除" : "Remove";
        public string MoveUp => _chinese ? "上移" : "Up";
        public string MoveDown => _chinese ? "下移" : "Down";
        public string NoStates => _chinese ? "还没有观察到可配置的状态。进入游戏并产生状态后再刷新。" : "No configurable moodles observed yet. Enter gameplay, trigger a status, then refresh.";
        public string NoGroup => _chinese ? "先添加至少一个分组。" : "Add at least one group first.";
        public string Conflict => _chinese ? "该状态已属于：" : "State already belongs to: ";
        public string Unsaved => _chinese ? "有未保存修改" : "Unsaved changes";
        public string Preview => _chinese ? "预览" : "Preview";
        public string Template => _chinese ? "文本模板" : "Text template";
        public string Opacity => _chinese ? "透明度" : "Opacity";
        public string Duration => _chinese ? "持续" : "Duration";
        public string Placement => _chinese ? "位置" : "Placement";
        public string VisualPreset => _chinese ? "视觉预设" : "Visual preset";
        public string NormalizedX => _chinese ? "相对 X" : "Normalized X";
        public string NormalizedY => _chinese ? "相对 Y" : "Normalized Y";
        public string PixelOffsetX => _chinese ? "像素偏移 X" : "Pixel offset X";
        public string PixelOffsetY => _chinese ? "像素偏移 Y" : "Pixel offset Y";
        public string PreviewFallback => _chinese ? "状态提醒预览" : "State reminder preview";
        public string LegacyUnsupported => _chinese ? "旧设置：当前未实现" : "Legacy setting: not implemented";

        public string EnabledHelp => _chinese
            ? "只调整游戏本轮已经创建的 Moodle 图标顺序；不改变伤病判定、严重度、脑芯片可见性或主/侧状态栏规则。"
            : "Only reorders Moodle UI nodes already created by the game. It does not change medical simulation, severity, chip visibility, or main/side row rules.";

        public string UnknownPolicyHelp => _chinese
            ? "“保持原位”最保守：新版本或第三方 Mod 的未知状态不会被擅自降到末尾。“移到末尾”只适合你已覆盖绝大多数状态的配置。"
            : "Keep position is safest: new-game or third-party moodles are not silently demoted. Move to end is best only when your rules cover almost every status.";

        public string GroupHelp => _chinese
            ? "分组从上到下代表从前到后；同组按状态列表顺序。主 Moodle 与 side Moodle 始终分别排序，不会混到同一行。"
            : "Groups run from highest/earliest to lowest/latest. States within a group keep their listed order. Main and side moodles are always sorted separately.";

        public string CatalogHelp => _chinese
            ? "这里列出的不是 Wiki 推测值，而是本 Mod 实际在 UI 中观察到的 Moodle。不同强度通常归并为同一基础状态；游戏条件、脑芯片、健康面板/悬停会影响哪些状态能出现。"
            : "This catalog is built from Moodle UI nodes actually observed at runtime, not guessed wiki variables. Severity variants are normally merged; game conditions, brainchip state, health-panel state and hovering affect what can appear.";

        public string AdvancedHelp => _chinese
            ? "高级区主要用于诊断、兼容和提醒。排序本身通常不需要改这里；越多自定义坐标/提醒规则，越容易受分辨率和游戏更新影响。"
            : "Advanced options are mostly for diagnostics, compatibility and reminders. Sorting normally needs none of these; custom positions/reminders are more sensitive to resolution and game updates.";

        public string ReminderHelp => _chinese
            ? "提醒是额外功能，不参与排序。优先使用日志或底部透明提示；“健康面板提示”旧模式当前没有可靠实现，因此不再作为可选模式展示。"
            : "Reminders are independent from sorting. Prefer log or transparent bottom alerts. The legacy Health Panel Hint mode has no reliable implementation and is no longer offered as a selectable mode.";

        public string TargetGroupHelp => _chinese
            ? "点右侧按钮循环选择目标分组，再用“加入/移动”分配状态。修改只在“保存并应用”后写入规则文件。"
            : "Cycle the target group with the button, then assign or move states. Changes are written only after Save & apply.";

        public string StateTechnicalHelp(StateCatalogEntry entry)
        {
            if (entry == null) return string.Empty;
            var intensities = entry.Intensities == null ? string.Empty : string.Join(", ", entry.Intensities);
            var rows = entry.SeenInMainRow && entry.SeenInSideRow
                ? (_chinese ? "主排 + side 排" : "main + side")
                : entry.SeenInSideRow ? "side" : (_chinese ? "主排" : "main");
            return (_chinese ? "基础 ID: " : "Base ID: ") + entry.BaseId
                + "\n" + (_chinese ? "最近 runtime ID: " : "Last runtime ID: ") + entry.LastRuntimeId
                + "\n" + (_chinese ? "观察到的强度: " : "Observed intensities: ") + intensities
                + "\n" + (_chinese ? "观察到的行: " : "Observed rows: ") + rows
                + "\n" + (_chinese ? "曾标记 critical: " : "Ever critical: ") + entry.EverCritical
                + "\n" + (_chinese ? "曾使用 chippedOnly: " : "Ever used chippedOnly: ") + entry.UsesChippedOnly
                + "\n" + (_chinese ? "最近观察: " : "Last observed: ") + entry.LastSeenAt.ToLocalTime().ToString("HH:mm:ss");
        }

        public string UnknownPolicy(UnknownStatePolicy policy)
        {
            switch (policy)
            {
                case global::HealthAutoArrange.Core.UnknownStatePolicy.End: return _chinese ? "移到末尾" : "Move to end";
                case global::HealthAutoArrange.Core.UnknownStatePolicy.Keep: return _chinese ? "保持原位（推荐）" : "Keep position (recommended)";
                default: return policy.ToString();
            }
        }

        public string ReminderMode(ReminderMode mode)
        {
            switch (mode)
            {
                case global::HealthAutoArrange.Core.ReminderMode.Log: return _chinese ? "仅日志" : "Log only";
                case global::HealthAutoArrange.Core.ReminderMode.BottomAlert: return _chinese ? "透明提示" : "Transparent alert";
                case global::HealthAutoArrange.Core.ReminderMode.HealthPanelHint: return _chinese ? "健康面板提示（旧/未实现）" : "Health panel hint (legacy/unimplemented)";
                default: return mode.ToString();
            }
        }

        public string ReminderPreset(ReminderVisualPresetKind kind)
        {
            switch (kind)
            {
                case ReminderVisualPresetKind.SubtleTop: return _chinese ? "顶部轻提示" : "Subtle top";
                case ReminderVisualPresetKind.CriticalCenter: return _chinese ? "中央紧急提示" : "Critical center";
                case ReminderVisualPresetKind.CompactBottomLeft: return _chinese ? "左下紧凑提示" : "Compact bottom-left";
                default: return _chinese ? "底部轻提示" : "Subtle bottom";
            }
        }

        public string PlacementPreset(ReminderPlacementPreset preset)
        {
            switch (preset)
            {
                case ReminderPlacementPreset.Top: return _chinese ? "顶部" : "Top";
                case ReminderPlacementPreset.Center: return _chinese ? "中央" : "Center";
                case ReminderPlacementPreset.BottomLeft: return _chinese ? "左下" : "Bottom-left";
                case ReminderPlacementPreset.Custom: return _chinese ? "自定义" : "Custom";
                default: return _chinese ? "底部" : "Bottom";
            }
        }
    }
}
