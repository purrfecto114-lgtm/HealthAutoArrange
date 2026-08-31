using System;
using HarmonyLib;

namespace HealthAutoArrange.Plugin
{
    /// <summary>
    /// 仅包含经反编译确认的 Harmony 补丁目标。
    /// 当前目标：
    /// - MoodleManager.UpdateMoodles（优先）或 AddAllMoodles（降级）→ 刷新完成边界；
    /// - MoodleManager.AddMoodle(int,string,string,string,bool,bool)
    ///   - 前缀：捕获元数据；
    ///   - 后缀（v1.2.0 新增）：记录新创建 moodle 的 Transform instance id 到 fresh-set，
    ///     供下一次 Scan 过滤 ClearMoodles 遗留的 Destroy-pending 节点；
    /// - UIUtil.IsPointerOverUIElement()（无参）→ F8 设置窗口打开时视为指针在 UI 上，
    ///   拦截游戏原生输入（攻击/交互等），与游戏自身 UI 打开时的行为一致。
    /// 运行时只选择一个刷新边界，避免重复触发。
    /// </summary>
    public static class GamePatches
    {
        // Diagnostics flags: set when each patch is actually invoked at runtime.
        // The F8 "Version & Updates" panel can read these to confirm the patches are
        // not only applied (logged once in Plugin.InitializePlugin) but actually firing.
        internal static bool PointerOverUiPostfixInvoked;
        internal static bool MoodleRefreshPostfixInvoked;
        internal static bool AddMoodlePrefixInvoked;
        internal static bool AddMoodlePostfixInvoked;  // v1.2.0
        internal static long MoodleRefreshInvokeCount;
        internal static long AddMoodleInvokeCount;
        internal static long AddMoodlePostfixInvokeCount;  // v1.2.0
        internal static bool AddAllMoodlesPostfixInvoked;  // v1.2.2
        internal static bool ClearMoodlesPostfixInvoked;  // v1.2.2
        internal static long AddAllMoodlesInvokeCount;  // v1.2.2
        internal static long ClearMoodlesInvokeCount;  // v1.2.2

        /// <summary>
        /// UIUtil.IsPointerOverUIElement() 后置补丁：保留游戏和其他 Mod 的原始判断，
        /// 只在 F8 设置窗口打开时把最终结果提升为 true。
        /// Harmony 官方建议仅做小幅结果修正时优先使用 postfix，避免 prefix=false
        /// 跳过原方法及其他有副作用前缀。
        /// </summary>
        public static void IsPointerOverUIElementPostfix(ref bool __result)
        {
            PointerOverUiPostfixInvoked = true;
            if (Plugin.SettingsWindowOpen) __result = true;
        }

        /// <summary>
        /// MoodleManager 刷新边界后置补丁：刷新完成后安排下一帧扫描/排序/提醒。
        /// 捕获并记录可恢复的托管异常；不声称能处理 Unity 原生层或进程级故障。
        /// </summary>
        public static void MoodleRefreshPostfix(MoodleManager __instance)
        {
            MoodleRefreshPostfixInvoked = true;
            MoodleRefreshInvokeCount++;
            try
            {
                Plugin.Adapter?.OnMoodlesUpdated(__instance);
            }
            catch (Exception ex)
            {
                Plugin.PluginLog?.LogWarning($"HealthAutoArrange: Moodle refresh postfix error: {ex}");
            }
        }

        /// <summary>
        /// MoodleManager.AddMoodle 前缀补丁：捕获 runtime id、图标、强度、显示名、critical、创建顺序、manager 与行。
        /// 捕获并记录可恢复的托管异常；不声称能处理 Unity 原生层或进程级故障。
        /// </summary>
        public static void AddMoodlePrefix(
            MoodleManager __instance,
            int __0,
            string __1,
            string __2,
            string __3,
            bool __4,
            bool __5)
        {
            AddMoodlePrefixInvoked = true;
            AddMoodleInvokeCount++;
            try
            {
                // Harmony supports __n positional argument injection. Using indexes here avoids
                // depending on game-assembly parameter names, which can change between builds even
                // when the reverse-engineered method signature remains ABI-compatible.
                Plugin.Adapter?.OnMoodleAdded(__instance, __0, __1, __2, __3, __4, __5);
            }
            catch (Exception ex)
            {
                Plugin.PluginLog?.LogWarning($"HealthAutoArrange: AddMoodle prefix error: {ex}");
            }
        }

        /// <summary>
        /// v1.2.0: MoodleManager.AddMoodle 后置补丁。原方法刚执行完毕：新 moodle 已被
        /// SetParent 到 manager.moodles 末尾。此时通过 Adapter.OnMoodleCreated 把它的
        /// Transform instance id 记入 fresh-set，让下一次 Scan 能过滤掉 ClearMoodles
        /// 遗留的 Destroy-pending 节点（Object.Destroy 在本帧末才真正执行；postfix
        /// 时旧 moodle 的 C# wrapper 仍可能 == null 检查失败，需要 fresh-set 精确过滤）。
        /// 不在此处触发排序：排序仍由 UpdateMoodles postfix 与 Plugin.Update 周期性
        /// safety-net 共同驱动，避免与同帧 postfix 重入冲突。
        /// </summary>
        public static void AddMoodlePostfix(MoodleManager __instance)
        {
            AddMoodlePostfixInvoked = true;
            AddMoodlePostfixInvokeCount++;
            try
            {
                Plugin.Adapter?.OnMoodleCreated(__instance);
            }
            catch (Exception ex)
            {
                Plugin.PluginLog?.LogWarning($"HealthAutoArrange: AddMoodle postfix error: {ex}");
            }
        }

        /// <summary>
        /// v1.2.2: AddAllMoodles 后置补丁。AddAllMoodles 是所有重建路径的最低边界——
        /// 无论调用者是 UpdateMoodles 的 0.5s 定时器还是其他任何（未知的）路径，
        /// 本轮重建的全部新节点在 postfix 时刻都已创建完毕。postfix 直接调用
        /// Adapter.OnMoodlesUpdated 在同一帧内完成最终排序（渲染发生在帧末，用户
        /// 永远看不到游戏的原始创建顺序）。
        /// v1.2.1 只挂了 UpdateMoodles 一条路径：任何绕过它的重建（直呼 AddAllMoodles
        /// 或逐个 AddMoodle）都要等 4Hz 周期兜底才被纠正，期间用户看到游戏顺序与
        /// mod 顺序“互相替代”。
        /// </summary>
        public static void AddAllMoodlesPostfix(MoodleManager __instance)
        {
            AddAllMoodlesPostfixInvoked = true;
            AddAllMoodlesInvokeCount++;
            try
            {
                Plugin.Adapter?.OnMoodlesUpdated(__instance);
            }
            catch (Exception ex)
            {
                Plugin.PluginLog?.LogWarning($"HealthAutoArrange: AddAllMoodles postfix error: {ex}");
            }
        }

        /// <summary>
        /// v1.2.2: ClearMoodles 后置补丁。ClearMoodles 在每轮重建最前执行（销毁旧
        /// 节点，Object.Destroy 延迟到帧末才真正移除）。postfix 时通知 Adapter 重置
        /// 本轮累积状态（创建序列、fresh-set、watchdog 计划），让随后的 AddMoodle
        /// 后缀从零开始累积/定位。
        /// </summary>
        public static void ClearMoodlesPostfix(MoodleManager __instance)
        {
            ClearMoodlesPostfixInvoked = true;
            ClearMoodlesInvokeCount++;
            try
            {
                Plugin.Adapter?.OnMoodlesCleared(__instance);
            }
            catch (Exception ex)
            {
                Plugin.PluginLog?.LogWarning($"HealthAutoArrange: ClearMoodles postfix error: {ex}");
            }
        }
    }
}
