using HealthAutoArrange.Core;
using Xunit;

namespace HealthAutoArrange.Tests
{
    /// <summary>
    /// 刷新调度决策：游戏刷新跨帧处理，手动重排可同帧执行。
    /// 仅当无法立即处理时推迟到下一帧重试。
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
        public void RefreshCompleted_CannotRunNow_DefersToNextFrame()
        {
            var scheduler = new SortScheduler();
            var decision = scheduler.OnRefreshCompleted(canRunNow: false);
            Assert.Equal(SortDispatchDecision.DeferToNextFrame, decision);
            Assert.True(scheduler.HasPending);
        }

        [Fact]
        public void GameRefreshCompleted_AlwaysDefersToNextFrame()
        {
            var scheduler = new SortScheduler();

            var decision = scheduler.OnGameRefreshCompleted();

            Assert.Equal(SortDispatchDecision.DeferToNextFrame, decision);
            Assert.True(scheduler.HasPending);
        }

        [Fact]
        public void Deferred_Retry_ReturnsTrueOnce()
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
            Assert.False(scheduler.TryDeferred());
        }

        [Fact]
        public void RunNow_ClearsPriorPending()
        {
            var scheduler = new SortScheduler();
            scheduler.OnRefreshCompleted(canRunNow: false);
            var decision = scheduler.OnRefreshCompleted(canRunNow: true);
            Assert.Equal(SortDispatchDecision.RunNow, decision);
            Assert.False(scheduler.HasPending);
        }
    }
}
