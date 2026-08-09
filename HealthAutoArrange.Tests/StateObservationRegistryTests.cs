using System;
using HealthAutoArrange.Core;
using Xunit;

namespace HealthAutoArrange.Tests
{
    public sealed class StateObservationRegistryTests
    {
        [Fact]
        public void SnapshotContainsOnlyObservedNodesAndMergesRowsAndIntensity()
        {
            var registry = new StateObservationRegistry();
            var t0 = new DateTimeOffset(2026, 8, 9, 0, 0, 0, TimeSpan.Zero);
            registry.Observe("bleeding1", new MoodleCaptureMetadata
            {
                IconId = "bleeding", DisplayName = "Bleeding", Intensity = 1, Critical = false
            }, false, t0);
            registry.Observe("bleeding3", new MoodleCaptureMetadata
            {
                IconId = "bleeding", DisplayName = "Severe bleeding", Intensity = 3, Critical = true, ChippedOnly = true
            }, true, t0.AddSeconds(1));

            var entry = Assert.Single(registry.Snapshot());
            Assert.Equal("bleeding", entry.BaseId);
            Assert.Equal(new[] { 1, 3 }, entry.Intensities);
            Assert.True(entry.SeenInMainRow);
            Assert.True(entry.SeenInSideRow);
            Assert.True(entry.EverCritical);
            Assert.True(entry.UsesChippedOnly);
            Assert.Equal("bleeding3", entry.LastRuntimeId);
        }

        [Fact]
        public void CaptureAloneDoesNotPopulateObservationRegistry()
        {
            var captureRegistry = new MoodleCaptureRegistry();
            captureRegistry.Capture(null, 2, "infection", "Infection", "", false, false, false);
            var observed = new StateObservationRegistry();
            Assert.Empty(observed.Snapshot());
        }
    }
}
