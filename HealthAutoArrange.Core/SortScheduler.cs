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
    /// 刷新调度器。游戏刷新路径可携带 frame token，从而保证不是
    /// “下一次 Update 调用”而是真正跨过至少一个 Unity frame 后再执行。
    /// 纯 C#，无 Unity 依赖。
    /// </summary>
    public sealed class SortScheduler
    {
        private bool _pending;
        private bool _hasFrameGate;
        private long _deferFromFrame;

        /// <summary>是否有挂起任务。</summary>
        public bool HasPending => _pending;

        /// <summary>
        /// 通用刷新完成事件。canRunNow=true 用于明确安全的手动/非 Unity 刷新路径；
        /// false 仅表示挂起，若需要严格帧边界应使用 OnGameRefreshCompleted(frame)。
        /// </summary>
        public SortDispatchDecision OnRefreshCompleted(bool canRunNow)
        {
            if (canRunNow)
            {
                ClearPending();
                return SortDispatchDecision.RunNow;
            }

            _pending = true;
            _hasFrameGate = false;
            return SortDispatchDecision.DeferToNextFrame;
        }

        /// <summary>
        /// 游戏 Moodle 刷新边界：记录触发刷新的 frame token，只有观察到不同 frame
        /// 才允许消费挂起任务。使用“不等于触发帧”而不是 currentFrame+1 比较，
        /// 这样即使 Unity 的有符号 frameCount 长时间运行后发生回绕，也不会永久卡住。
        /// 同一帧多次刷新会合并；更晚帧的新刷新会把门槛更新为那个更晚帧。
        /// </summary>
        public SortDispatchDecision OnGameRefreshCompleted(long currentFrame)
        {
            _pending = true;
            _hasFrameGate = true;
            _deferFromFrame = currentFrame;
            return SortDispatchDecision.DeferToNextFrame;
        }

        /// <summary>
        /// 兼容旧调用方：只挂起，不提供严格帧保证。Unity 适配层不得使用此重载。
        /// </summary>
        public SortDispatchDecision OnGameRefreshCompleted()
        {
            _pending = true;
            _hasFrameGate = false;
            return SortDispatchDecision.DeferToNextFrame;
        }

        /// <summary>
        /// 帧感知重试：严格拒绝在触发游戏刷新的同一 frame 消费。
        /// </summary>
        public bool TryDeferred(long currentFrame)
        {
            if (!_pending) return false;
            if (_hasFrameGate && currentFrame == _deferFromFrame) return false;
            ClearPending();
            return true;
        }

        /// <summary>
        /// 兼容旧调用方：消费挂起但不检查帧。Unity 适配层不得使用此重载。
        /// </summary>
        public bool TryDeferred()
        {
            if (!_pending) return false;
            ClearPending();
            return true;
        }

        /// <summary>明确安全的同帧强制执行入口；会清除任何挂起。</summary>
        public SortDispatchDecision TryRunNow()
        {
            ClearPending();
            return SortDispatchDecision.RunNow;
        }

        private void ClearPending()
        {
            _pending = false;
            _hasFrameGate = false;
            _deferFromFrame = 0L;
        }
    }
}
