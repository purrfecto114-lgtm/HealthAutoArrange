using System;

namespace HealthAutoArrange.Core
{
    /// <summary>
    /// 提醒输出模式。引擎只产出消息对象，不直接调用游戏私有 API。
    /// </summary>
    public enum ReminderMode
    {
        /// <summary>仅写入日志。</summary>
        Log = 0,

        /// <summary>游戏底部弹出提醒。</summary>
        BottomAlert = 1,

        /// <summary>健康面板内的提示。</summary>
        HealthPanelHint = 2,
    }
}