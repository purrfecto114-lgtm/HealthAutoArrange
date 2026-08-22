using System;
using System.Linq;
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


        [Fact]
        public void ReliableCapture_MergesEarlierExactRuntimeObservation()
        {
            var registry = new StateObservationRegistry();
            var t0 = new DateTimeOffset(2026, 8, 9, 0, 0, 0, TimeSpan.Zero);
            registry.Observe("bleeding3", null, false, t0);
            registry.Observe("bleeding3", new MoodleCaptureMetadata
            {
                IconId = "bleeding", DisplayName = "Bleeding", Intensity = 3
            }, false, t0.AddSeconds(1));

            var entry = Assert.Single(registry.Snapshot());
            Assert.Equal("bleeding", entry.BaseId);
            Assert.Equal("bleeding#", entry.Pattern);
            Assert.Equal(new[] { 3 }, entry.Intensities);
        }

        [Fact]
        public void MissingCapture_DoesNotInventSeverityFromSemanticTrailingDigits()
        {
            var registry = new StateObservationRegistry();
            registry.Observe("modstate123", null, false, DateTimeOffset.UtcNow);
            var entry = Assert.Single(registry.Snapshot());
            Assert.Equal("modstate123", entry.BaseId);
            // 无 capture provisional 状态必须使用 exact pattern，而非 severity family "#"。
            Assert.Equal("modstate123", entry.Pattern);
            Assert.Empty(entry.Intensities);
        }

        [Fact]
        public void ProvisionalState_UsesExactPatternUntilReliableCapture()
        {
            var registry = new StateObservationRegistry();
            var t0 = new DateTimeOffset(2026, 8, 9, 0, 0, 0, TimeSpan.Zero);
            registry.Observe("drug2", null, false, t0);

            var provisional = Assert.Single(registry.Snapshot());
            Assert.Equal("drug2", provisional.BaseId);
            Assert.Equal("drug2", provisional.Pattern);
            Assert.Empty(provisional.Intensities);

            // 可靠 capture 确认 drug2 为图标基础名后，才使用 severity family。
            registry.Observe("drug21", new MoodleCaptureMetadata
            {
                IconId = "drug2", DisplayName = "Drug 2", Intensity = 1
            }, false, t0.AddSeconds(1));

            var entry = Assert.Single(registry.Snapshot());
            Assert.Equal("drug2", entry.BaseId);
            Assert.Equal("drug2#", entry.Pattern);
            Assert.Equal(new[] { 1 }, entry.Intensities);
            Assert.Equal("Drug 2", entry.DisplayName);
        }

        [Fact]
        public void ReliableCapture_MergesEarlierProvisionalSiblingIntensities()
        {
            var registry = new StateObservationRegistry();
            var t0 = new DateTimeOffset(2026, 8, 9, 0, 0, 0, TimeSpan.Zero);
            registry.Observe("bleeding1", null, false, t0);
            registry.Observe("bleeding2", null, false, t0.AddSeconds(1));
            registry.Observe("bleeding3", new MoodleCaptureMetadata
            {
                IconId = "bleeding", DisplayName = "Severe bleeding", Intensity = 3
            }, false, t0.AddSeconds(2));

            var entry = Assert.Single(registry.Snapshot());
            Assert.Equal("bleeding", entry.BaseId);
            Assert.Equal("bleeding#", entry.Pattern);
            // 不猜测 provisional 强度：仅 capture 提供的强度被记录。
            Assert.Equal(new[] { 3 }, entry.Intensities);
            Assert.Equal("Severe bleeding", entry.DisplayName);
            Assert.Equal("bleeding3", entry.LastRuntimeId);
        }

        [Fact]
        public void ReliableCapture_DoesNotMergeNonFamilyProvisionalRows()
        {
            var registry = new StateObservationRegistry();
            var t0 = new DateTimeOffset(2026, 8, 9, 0, 0, 0, TimeSpan.Zero);
            registry.Observe("bleedingshock", null, false, t0);
            registry.Observe("fracture2", null, false, t0.AddSeconds(1));
            registry.Observe("bleeding3", new MoodleCaptureMetadata
            {
                IconId = "bleeding", Intensity = 3
            }, false, t0.AddSeconds(2));

            var entries = registry.Snapshot();
            Assert.Equal(3, entries.Count);

            var bleeding = entries.Single(e => e.BaseId == "bleeding");
            Assert.Equal("bleeding#", bleeding.Pattern);
            Assert.Equal(new[] { 3 }, bleeding.Intensities);

            // 信息不足不可安全归属的 provisional 行保持独立并继续使用 exact pattern。
            Assert.Equal("bleedingshock", entries.Single(e => e.BaseId == "bleedingshock").Pattern);
            Assert.Equal("fracture2", entries.Single(e => e.BaseId == "fracture2").Pattern);
        }

        [Fact]
        public void ReliableBaseRuntimeWithoutNumericSuffix_MergesProvisionalSiblings()
        {
            var registry = new StateObservationRegistry();
            var t0 = new DateTimeOffset(2026, 8, 9, 0, 0, 0, TimeSpan.Zero);
            // 无 capture 的 provisional 强度观察（bleeding1 / bleeding2）
            registry.Observe("bleeding1", null, false, t0);
            registry.Observe("bleeding2", null, true, t0.AddSeconds(1));
            // 可靠 capture 的 runtime id 恰好等于图标基础名（无数字后缀），
            // 此时仍应合并 severity-family 内的 provisional 行，而不是跳过。
            registry.Observe("bleeding", new MoodleCaptureMetadata
            {
                IconId = "bleeding", DisplayName = "Bleeding", Intensity = 0
            }, false, t0.AddSeconds(2));

            var entry = Assert.Single(registry.Snapshot());
            Assert.Equal("bleeding", entry.BaseId);
            Assert.Equal("bleeding#", entry.Pattern);
            Assert.Equal(new[] { 0 }, entry.Intensities);
            Assert.True(entry.SeenInMainRow);
            Assert.True(entry.SeenInSideRow);
            Assert.Equal("Bleeding", entry.DisplayName);
            Assert.Equal("bleeding", entry.LastRuntimeId);
        }

        [Fact]
        public void ReliableCapture_DoesNotAbsorbAuthoritativeCapturedSibling()
        {
            var registry = new StateObservationRegistry();
            var t0 = new DateTimeOffset(2026, 8, 9, 0, 0, 0, TimeSpan.Zero);
            // "bleeding1" 是被可靠 capture 确认过的独立图标（语义数字），不是 "bleeding" 的强度变体。
            registry.Observe("bleeding11", new MoodleCaptureMetadata
            {
                IconId = "bleeding1", DisplayName = "Bleeding 1", Intensity = 1
            }, false, t0);
            // 之后出现另一个图标 "bleeding" 的 capture，不得把 "bleeding1" 吞并进 "bleeding"。
            registry.Observe("bleeding3", new MoodleCaptureMetadata
            {
                IconId = "bleeding", DisplayName = "Bleeding", Intensity = 3
            }, false, t0.AddSeconds(1));

            var entries = registry.Snapshot();
            Assert.Equal(2, entries.Count);
            Assert.Equal("bleeding1#", entries.Single(e => e.BaseId == "bleeding1").Pattern);
            Assert.Equal("Bleeding 1", entries.Single(e => e.BaseId == "bleeding1").DisplayName);
            Assert.Equal("bleeding#", entries.Single(e => e.BaseId == "bleeding").Pattern);
        }
    }
}
