using System;
using BepInEx.Logging;
using HealthAutoArrange.Core;

namespace HealthAutoArrange.Plugin
{
    /// <summary>
    /// 提醒消息分发器：
    /// - Log：直接写日志；
    /// - BottomAlert：仅在 PlayerCamera.main 可用时调用 DoAlert；
    /// - HealthPanelHint：占位，仅写日志，不猜测游戏 UI。
    /// </summary>
    public sealed class ReminderDispatcher
    {
        private readonly ManualLogSource _log;

        public ReminderDispatcher(ManualLogSource log)
        {
            _log = log ?? throw new ArgumentNullException(nameof(log));
        }

        public void Dispatch(ReminderMessage message)
        {
            switch (message.Mode)
            {
                case ReminderMode.Log:
                    _log.LogInfo($"[Reminder:{message.RuleName}] state '{message.State}' present");
                    break;

                case ReminderMode.BottomAlert:
                    TryBottomAlert(message);
                    break;

                case ReminderMode.HealthPanelHint:
                    // 占位：健康面板提示未实现，不猜测 UI，仅记录。
                    _log.LogInfo($"[Reminder:{message.RuleName}] HealthPanelHint placeholder (not implemented): '{message.State}'");
                    break;

                default:
                    _log.LogInfo($"[Reminder:{message.RuleName}] unknown mode {message.Mode}: '{message.State}'");
                    break;
            }
        }

        private void TryBottomAlert(ReminderMessage message)
        {
            try
            {
                var camera = PlayerCamera.main;
                if (camera == null)
                {
                    _log.LogWarning($"[Reminder:{message.RuleName}] PlayerCamera.main unavailable, BottomAlert skipped.");
                    return;
                }
                camera.DoAlert($"[HealthAutoArrange] {message.State}", true);
            }
            catch (Exception ex)
            {
                _log.LogWarning($"[Reminder:{message.RuleName}] BottomAlert failed: {ex.Message}");
            }
        }
    }
}