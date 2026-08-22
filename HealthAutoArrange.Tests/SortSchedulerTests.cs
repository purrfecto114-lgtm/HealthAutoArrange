using HealthAutoArrange.Core;
using Xunit;

namespace HealthAutoArrange.Tests
{
    /// <summary>
    /// 刷新调度决策：游戏刷新必须真正跨过 frame token；明确安全的手动路径可同帧执行。
    /// </summary>
    public sealed class SortSchedulerTests
    {
        [Fact]
        public void RefreshCompleted_CanRunNow_RunsImmediately()
        {
            var scheduler = new SortScheduler();
            var decision = scheduler.OnRefreshCompleted(canRunNow: true);
            Assert.Equal(SortDispatchDecision.RunNow, decision);
            Assert.False(scheduler.HasPending);
        }

        [Fact]
        public void RefreshCompleted_CannotRunNow_Defers()
        {
            var scheduler = new SortScheduler();
            var decision = scheduler.OnRefreshCompleted(canRunNow: false);
            Assert.Equal(SortDispatchDecision.DeferToNextFrame, decision);
            Assert.True(scheduler.HasPending);
        }

        [Fact]
        public void GameRefreshCompleted_DoesNotRunInSameFrame()
        {
            var scheduler = new SortScheduler();
            var decision = scheduler.OnGameRefreshCompleted(100);

            Assert.Equal(SortDispatchDecision.DeferToNextFrame, decision);
            Assert.True(scheduler.HasPending);
            Assert.False(scheduler.TryDeferred(100));
            Assert.True(scheduler.HasPending);
        }

        [Fact]
        public void GameRefreshCompleted_RunsAtNextFrame()
        {
            var scheduler = new SortScheduler();
            scheduler.OnGameRefreshCompleted(100);

            Assert.True(scheduler.TryDeferred(101));
            Assert.False(scheduler.TryDeferred(101));
            Assert.False(scheduler.HasPending);
        }

        [Fact]
        public void RepeatedGameRefresh_PushesGateForwardAndCoalesces()
        {
            var scheduler = new SortScheduler();
            scheduler.OnGameRefreshCompleted(100);
            scheduler.OnGameRefreshCompleted(101);

            Assert.False(scheduler.TryDeferred(101));
            Assert.True(scheduler.TryDeferred(102));
            Assert.False(scheduler.HasPending);
        }

        [Fact]
        public void FrameTokenWrap_DoesNotPermanentlyBlockPendingWork()
        {
            var scheduler = new SortScheduler();
            scheduler.OnGameRefreshCompleted(int.MaxValue);

            Assert.False(scheduler.TryDeferred(int.MaxValue));
            Assert.True(scheduler.TryDeferred(int.MinValue));
            Assert.False(scheduler.HasPending);
        }

        [Fact]
        public void LegacyDeferred_Retry_ReturnsTrueOnce()
        {
            var scheduler = new SortScheduler();
            scheduler.OnRefreshCompleted(canRunNow: false);
            Assert.True(scheduler.TryDeferred());
            Assert.False(scheduler.TryDeferred());
            Assert.False(scheduler.HasPending);
        }

        [Fact]
        public void NoPending_Retry_ReturnsFalse()
        {
            var scheduler = new SortScheduler();
            Assert.False(scheduler.TryDeferred(1));
        }

        [Fact]
        public void RunNow_ClearsPriorPendingAndFrameGate()
        {
            var scheduler = new SortScheduler();
            scheduler.OnGameRefreshCompleted(100);
            var decision = scheduler.TryRunNow();
            Assert.Equal(SortDispatchDecision.RunNow, decision);
            Assert.False(scheduler.HasPending);
            Assert.False(scheduler.TryDeferred(101));
        }
    }
}
