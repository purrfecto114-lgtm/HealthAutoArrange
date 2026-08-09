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
        /// UIUtil.IsPointerOverUIElement() 前缀补丁：F8 设置窗口打开时直接返回 true，
        /// 跳过 EventSystem 射线检测（IMGUI 窗口不在 EventSystem 中，原检测永远为 false，
        /// 导致按住左键拖动滚动条时游戏持续攻击）。
        /// 窗口关闭时返回 false，走原逻辑，不影响游戏自身 UI 的输入拦截。
        /// </summary>
        public static bool IsPointerOverUIElementPrefix(ref bool __result)
        {
            if (Plugin.SettingsWindowOpen)
            {
                __result = true;
                return false;
            }
            return true;
        }
        /// <summary>
        /// MoodleManager 刷新边界后置补丁：刷新完成后安排下一帧扫描/排序/提醒。
        /// 所有异常捕获并记录，不阻塞游戏。
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
        /// 所有异常捕获并记录，不阻塞游戏。
        /// </summary>
        public static void AddMoodlePrefix(
            MoodleManager __instance,
            int intensity,
            string icon,
            string name,
            string desc,
            bool critical,
            bool chippedOnly)
        {
            try
            {
                Plugin.Adapter?.OnMoodleAdded(__instance, intensity, icon, name, desc, critical, chippedOnly);
            }
            catch (Exception ex)
            {
                Plugin.PluginLog?.LogWarning($"HealthAutoArrange: AddMoodle prefix error: {ex}");
            }
        }
    }
}
