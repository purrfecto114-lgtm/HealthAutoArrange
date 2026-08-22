using System;
using BepInEx.Logging;
using HealthAutoArrange.Core;

namespace HealthAutoArrange.Plugin
{
    /// <summary>
    /// Non-visual reminder dispatcher.
    /// - Log: writes to BepInEx log only.
    /// - BottomAlert: visual delivery is handled exclusively by TransparentReminderOverlay.
    ///   We intentionally do NOT also call PlayerCamera.DoAlert; v1.1.1 did both and could show
    ///   duplicate messages for one engine event.
    /// - HealthPanelHint: legacy/unimplemented, log only.
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
            if (message == null) return;

            switch (message.Mode)
            {
                case ReminderMode.Log:
                    _log.LogInfo($"[Reminder:{message.RuleName}] state '{message.State}' present");
                    break;

                case ReminderMode.BottomAlert:
                    // Visual-only mode. Plugin.OnReminderMessage owns the transparent overlay.
                    break;

                case ReminderMode.HealthPanelHint:
                    _log.LogInfo($"[Reminder:{message.RuleName}] HealthPanelHint legacy/unimplemented: '{message.State}'");
                    break;

                default:
                    _log.LogInfo($"[Reminder:{message.RuleName}] unknown mode {message.Mode}: '{message.State}'");
                    break;
            }
        }
    }
}
