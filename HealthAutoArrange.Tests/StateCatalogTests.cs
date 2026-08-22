using System;
using System.Linq;
using HealthAutoArrange.Core;
using Xunit;

namespace HealthAutoArrange.Tests
{
    /// <summary>
    /// 状态目录模型：从运行时捕获元数据构建可选项；
    /// 相同基础状态不同强度默认合并，保留强度集合与最近出现信息。
    /// </summary>
    public sealed class StateCatalogTests
    {
        private static readonly DateTimeOffset T0 =
            new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);

        private static MoodleCaptureMetadata Capture(
            string iconId, int intensity, string displayName = null, DateTimeOffset? at = null)
        {
            return new MoodleCaptureMetadata
            {
                IconId = iconId,
                ExpectedRuntimeId = MoodleIdentity.ExpectedRuntimeId(iconId, intensity),
                DisplayName = displayName ?? string.Empty,
                Intensity = intensity,
                CapturedAt = at ?? T0,
            };
        }

        [Fact]
        public void FromCaptures_MergesSameBaseIntensities()
        {
            var catalog = StateCatalog.FromCaptures(new[]
            {
                Capture("bleeding", 1),
                Capture("bleeding", 2),
                Capture("bleeding", 3),
            });

            var entry = Assert.Single(catalog.Entries);
            Assert.Equal("bleeding", entry.BaseId);
            Assert.Equal(new[] { 1, 2, 3 }, entry.Intensities);
        }

        [Fact]
        public void FromCaptures_DisplayNamePrefersAddName()
        {
            var catalog = StateCatalog.FromCaptures(new[]
            {
                Capture("bleeding", 1, displayName: "Bleeding"),
            });

            Assert.Equal("Bleeding", catalog.Entries[0].DisplayName);
        }

        [Fact]
        public void FromCaptures_DisplayNameFallsBackToBaseId()
        {
            var catalog = StateCatalog.FromCaptures(new[]
            {
                Capture("bleeding", 1, displayName: null),
            });

            Assert.Equal("bleeding", catalog.Entries[0].DisplayName);
        }

        [Fact]
        public void FromCaptures_PatternIsBaseIdSeverityFamily()
        {
            var catalog = StateCatalog.FromCaptures(new[]
            {
                Capture("bleeding", 1),
            });

            Assert.Equal("bleeding#", catalog.Entries[0].Pattern);
        }

        [Fact]
        public void FromCaptures_LastSeenTracksLatest()
        {
            var catalog = StateCatalog.FromCaptures(new[]
            {
                Capture("bleeding", 1, at: T0),
                Capture("bleeding", 2, at: T0.AddSeconds(5)),
            });

            Assert.Equal(T0.AddSeconds(5), catalog.Entries[0].LastSeenAt);
            Assert.Equal("bleeding2", catalog.Entries[0].LastRuntimeId);
        }

        [Fact]
        public void FromCaptures_EmptyInput_ReturnsEmpty()
        {
            var catalog = StateCatalog.FromCaptures(Array.Empty<MoodleCaptureMetadata>());
            Assert.Empty(catalog.Entries);
        }

        [Fact]
        public void FromCaptures_EmptyIconId_PreservesFullRuntimeIdInExactPattern()
        {
            // 无可靠 IconId 时不得把 modstate123 剥成 modstate，也不得猜测 severity family。
            var catalog = StateCatalog.FromCaptures(new[]
            {
                new MoodleCaptureMetadata
                {
                    IconId = string.Empty,
                    ExpectedRuntimeId = "modstate123",
                    DisplayName = string.Empty,
                    Intensity = 0,
                    CapturedAt = T0,
                },
            });

            var entry = Assert.Single(catalog.Entries);
            Assert.Equal("modstate123", entry.BaseId);
            Assert.Equal("modstate123", entry.Pattern);
        }
    }
}