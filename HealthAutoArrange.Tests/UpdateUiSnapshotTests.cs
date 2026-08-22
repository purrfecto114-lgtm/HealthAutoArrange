using HealthAutoArrange.Core;
using Xunit;

namespace HealthAutoArrange.Tests
{
    public sealed class UpdateUiSnapshotTests
    {
        [Fact]
        public void DownloadProgressIsClamped()
        {
            Assert.Equal(0f, new UpdateUiSnapshot(UpdateUiState.Downloading, "1", "2", "", "", -1f).Progress01);
            Assert.Equal(1f, new UpdateUiSnapshot(UpdateUiState.Downloading, "1", "2", "", "", 2f).Progress01);
        }

        [Fact]
        public void AvailableAndDownloadedStatesRemainActionableButOnlyAvailableCanDownload()
        {
            var available = new UpdateUiSnapshot(UpdateUiState.Available, "1.1.6", "1.1.7", "", "");
            var downloaded = new UpdateUiSnapshot(UpdateUiState.Downloaded, "1.1.6", "1.1.7", "", "path", 1f);

            Assert.True(available.HasUpdate);
            Assert.True(available.CanDownload);
            Assert.True(downloaded.HasUpdate);
            Assert.False(downloaded.CanDownload);
        }
    }
}
