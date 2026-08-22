using System;
using HarmonyLib;

namespace HealthAutoArrange.Plugin
{
    /// <summary>
    /// 仅包含经反编译确认的 Harmony 补丁目标。
    /// 当前目标：
    /// - MoodleManager.UpdateMoodles（优先）或 AddAllMoodles（降级）→ 刷新完成边界；
    /// - MoodleManager.AddMoodle(int,string,string,string,bool,bool) → 捕获元数据；
    /// - UIUtil.IsPointerOverUIElement()（无参）→ F8 设置窗口打开时视为指针在 UI 上，
    ///   拦截游戏原生输入（攻击/交互等），与游戏自身 UI 打开时的行为一致。
    /// 运行时只选择一个刷新边界，避免重复触发。
    /// </summary>
    public static class GamePatches
    {
        /// <summary>
        /// UIUtil.IsPointerOverUIElement() 后置补丁：保留游戏和其他 Mod 的原始判断，
        /// 只在 F8 设置窗口打开时把最终结果提升为 true。
        /// Harmony 官方建议仅做小幅结果修正时优先使用 postfix，避免 prefix=false
        /// 跳过原方法及其他有副作用前缀。
        /// </summary>
        public static void IsPointerOverUIElementPostfix(ref bool __result)
        {
            if (Plugin.SettingsWindowOpen) __result = true;
        }

        /// <summary>
        /// MoodleManager 刷新边界后置补丁：刷新完成后安排下一帧扫描/排序/提醒。
        /// 捕获并记录可恢复的托管异常；不声称能处理 Unity 原生层或进程级故障。
        /// </summary>
        public static void MoodleRefreshPostfix(MoodleManager __instance)
        {
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
    }
}
