namespace HealthAutoArrange.Core
{
    /// <summary>
    /// 刷新完成后的调度决策。
    /// </summary>
    public enum SortDispatchDecision
    {
        /// <summary>同帧立即执行扫描/排序/提醒。</summary>
        RunNow,

        /// <summary>无法立即处理，推迟到下一帧重试。</summary>
        DeferToNextFrame,
    }

    /// <summary>
    /// 刷新调度器。游戏刷新完成后必须跨帧重试，以避开 Unity 延迟销毁的旧节点；
    /// 手动重排仍可选择同帧执行。纯 C#，无 Unity 依赖，可单元测试。
    /// </summary>
    public sealed class SortScheduler
    {
        private bool _pending;

        /// <summary>是否有推迟到下一帧的挂起任务。</summary>
        public bool HasPending => _pending;

        /// <summary>
        /// 刷新完成事件：优先调度同帧立即执行；仅当无法立即处理（canRunNow=false）时推迟到下一帧。
        /// </summary>
        public SortDispatchDecision OnRefreshCompleted(bool canRunNow)
        {
            if (canRunNow)
            {
                _pending = false;
                return SortDispatchDecision.RunNow;
            }
            _pending = true;
            return SortDispatchDecision.DeferToNextFrame;
        }

        /// <summary>
        /// 游戏 Moodle 刷新边界：延迟到下一帧，等待旧 Moodle 节点真正销毁。
        /// </summary>
        public SortDispatchDecision OnGameRefreshCompleted()
        {
            _pending = true;
            return SortDispatchDecision.DeferToNextFrame;
        }

        /// <summary>
        /// 下一帧重试：有挂起任务时返回 true 并清除挂起；无挂起返回 false。
        /// </summary>
        public bool TryDeferred()
        {
            if (!_pending) return false;
            _pending = false;
            return true;
        }
    }
}
