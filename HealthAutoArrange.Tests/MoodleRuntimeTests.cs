using System.Collections.Generic;
using System.Linq;
using HealthAutoArrange.Core;
using Xunit;

namespace HealthAutoArrange.Tests
{
    /// <summary>
    /// 从 MoodleSorter_Source 移植的运行时优势的可测试部分：
    /// identity（runtime id 规范化/基础名/强度）、AddMoodle 捕获解析、行隔离排序决策、渲染模式解析。
    /// 全部为纯 C#，无 Unity 依赖。
    /// </summary>
    public class MoodleIdentityTests
    {
        [Fact]
        public void NormalizeRuntimeId_LowercasesAndKeepsAlnum()
        {
            Assert.Equal("bleeding1", MoodleIdentity.NormalizeRuntimeId("Bleeding1"));
            Assert.Equal("internalbleed", MoodleIdentity.NormalizeRuntimeId("internalBleed"));
        }

        [Fact]
        public void NormalizeRuntimeId_StripsSymbolsAndWhitespace()
        {
            // 保留字母/数字/./_/-，去除空格与其他符号。
            Assert.Equal("brain-damage_2", MoodleIdentity.NormalizeRuntimeId(" brain-damage_2! "));
        }

        [Fact]
        public void NormalizeRuntimeId_NullOrWhitespace_ReturnsEmpty()
        {
            Assert.Equal(string.Empty, MoodleIdentity.NormalizeRuntimeId(null));
            Assert.Equal(string.Empty, MoodleIdentity.NormalizeRuntimeId("   "));
        }

        [Fact]
        public void BaseId_StripsTrailingDigits()
        {
            Assert.Equal("bleeding", MoodleIdentity.BaseId("bleeding1"));
            Assert.Equal("bleeding", MoodleIdentity.BaseId("Bleeding3"));
        }

        [Fact]
        public void BaseId_NoTrailingDigits_ReturnsSame()
        {
            Assert.Equal("bleeding", MoodleIdentity.BaseId("bleeding"));
        }

        [Fact]
        public void BaseId_KeepsInteriorDigits()
        {
            // 仅去除末尾数字；中间数字保留。
            Assert.Equal("internalbleed", MoodleIdentity.BaseId("internalBleed"));
        }

        [Fact]
        public void ParseTrailingIntensity_ReturnsTrailingNumber()
        {
            Assert.Equal(3, MoodleIdentity.ParseTrailingIntensity("bleeding3", 0));
            Assert.Equal(0, MoodleIdentity.ParseTrailingIntensity("bleeding0", -1));
        }

        [Fact]
        public void ParseTrailingIntensity_NoTrailingNumber_ReturnsFallback()
        {
            Assert.Equal(0, MoodleIdentity.ParseTrailingIntensity("bleeding", 0));
            Assert.Equal(7, MoodleIdentity.ParseTrailingIntensity("bleeding", 7));
        }

        [Fact]
        public void ParseTrailingIntensity_InteriorDigits_ReturnsFallback()
        {
            Assert.Equal(5, MoodleIdentity.ParseTrailingIntensity("internalBleed", 5));
        }

        [Fact]
        public void ExpectedRuntimeId_ConcatenatesIconAndIntensity()
        {
            Assert.Equal("bleeding2", MoodleIdentity.ExpectedRuntimeId("bleeding", 2));
            Assert.Equal("focused8", MoodleIdentity.ExpectedRuntimeId("focused", 8));
        }
    }

    public class MoodleCaptureRegistryTests
    {
        [Fact]
        public void Capture_RecordsMetadata()
        {
            var registry = new MoodleCaptureRegistry();
            registry.Capture("manager", 2, "bleeding", "Bleeding", "desc", true, false, false);

            var resolved = registry.Resolve("bleeding2");
            Assert.NotNull(resolved);
            Assert.Equal("bleeding", resolved.IconId);
            Assert.Equal("bleeding2", resolved.ExpectedRuntimeId);
            Assert.Equal("Bleeding", resolved.DisplayName);
            Assert.Equal(2, resolved.Intensity);
            Assert.True(resolved.Critical);
            Assert.False(resolved.IsSide);
            Assert.Equal(1, resolved.Sequence);
            Assert.Equal("manager", resolved.Manager);
        }

        [Fact]
        public void Resolve_ExactRuntimeIdMatch_LatestWins()
        {
            var registry = new MoodleCaptureRegistry();
            registry.Capture("m", 1, "bleeding", "Bleeding 1", "d", false, false, false);
            registry.Capture("m", 2, "bleeding", "Bleeding 2", "d", false, false, false);

            var resolved = registry.Resolve("bleeding2");
            Assert.Equal("Bleeding 2", resolved.DisplayName);
            Assert.Equal(2, resolved.Sequence);
        }

        [Fact]
        public void Resolve_FallsBackToBaseIconId()
        {
            var registry = new MoodleCaptureRegistry();
            registry.Capture("m", 3, "bleeding", "Bleeding", "d", false, false, false);

            // 扫描到的 runtime id 与捕获的期望 id 不同，但基础图标名一致 → 回退命中。
            var resolved = registry.Resolve("bleeding1");
            Assert.NotNull(resolved);
            Assert.Equal("Bleeding", resolved.DisplayName);
        }

        [Fact]
        public void Resolve_UnknownRuntimeId_ReturnsNull()
        {
            var registry = new MoodleCaptureRegistry();
            registry.Capture("m", 1, "bleeding", "Bleeding", "d", false, false, false);

            Assert.Null(registry.Resolve("unknownstate3"));
        }

        [Fact]
        public void Resolve_IsCaseInsensitive()
        {
            var registry = new MoodleCaptureRegistry();
            registry.Capture("m", 1, "bleeding", "Bleeding", "d", false, false, false);

            Assert.NotNull(registry.Resolve("Bleeding1"));
        }

        [Fact]
        public void Clear_EmptiesRegistry()
        {
            var registry = new MoodleCaptureRegistry();
            registry.Capture("m", 1, "bleeding", "Bleeding", "d", false, false, false);
            registry.Clear();

            Assert.Null(registry.Resolve("bleeding1"));
            Assert.Empty(registry.Snapshot());
        }

        [Fact]
        public void Resolve_FallbackPreservesSemanticDigitsInIconId()
        {
            var registry = new MoodleCaptureRegistry();
            registry.Capture("m", 1, "drug2", "Drug 2", "", false, false, false);
            registry.Capture("m", 1, "drug3", "Drug 3", "", false, false, false);

            Assert.Equal("Drug 2", registry.Resolve("drug21").DisplayName);
            Assert.Equal("Drug 3", registry.Resolve("drug31").DisplayName);
            Assert.Null(registry.Resolve("drug22extra"));
        }

        [Fact]
        public void Capture_RecordsSideRow()
        {
            var registry = new MoodleCaptureRegistry();
            registry.Capture("m", 5, "highimmunity", "High Immunity", "d", false, true, true);

            var resolved = registry.Resolve("highimmunity5");
            Assert.True(resolved.IsSide);
            Assert.True(resolved.ChippedOnly);
        }
        [Fact]
        public void Resolve_CanBeScopedToManager()
        {
            var registry = new MoodleCaptureRegistry();
            var managerA = new object();
            var managerB = new object();
            registry.Capture(managerA, 1, "bleeding", "A", "", false, false, false);
            registry.Capture(managerB, 1, "bleeding", "B", "", false, false, false);

            Assert.Equal("A", registry.Resolve("bleeding1", managerA).DisplayName);
            Assert.Equal("B", registry.Resolve("bleeding1", managerB).DisplayName);
        }


        [Fact]
        public void ResolveSince_RejectsMetadataFromPreviousRefreshWindow()
        {
            var registry = new MoodleCaptureRegistry();
            var manager = new object();
            registry.Capture(manager, 1, "bleeding", "Old bleeding", "", false, false, false);
            var boundary = registry.LatestSequence;

            Assert.Null(registry.Resolve("bleeding1", manager, boundary));

            registry.Capture(manager, 2, "bleeding", "Fresh bleeding", "", true, false, false);
            var fresh = registry.Resolve("bleeding2", manager, boundary);
            Assert.NotNull(fresh);
            Assert.Equal("Fresh bleeding", fresh.DisplayName);
            Assert.True(fresh.Critical);
        }

    }

    public class MoodleSortPlannerTests
    {
        private static SortPlan PlanFromConfig(string text)
        {
            return ConfigTextParser.Parse(text).Config.CreateSortPlan();
        }

        private static MoodleRowItem Item(string runtimeId, bool isSide, int originalIndex)
        {
            return new MoodleRowItem { RuntimeId = runtimeId, IsSide = isSide, OriginalIndex = originalIndex };
        }

        [Fact]
        public void PlanRows_IsolatesMainAndSideRows()
        {
            var plan = PlanFromConfig(@"
GroupOrder = Vital
Group.Vital.States = Bleeding, Fracture
");
            var items = new List<MoodleRowItem>
            {
                Item("fracture2", false, 0),   // main
                Item("bleeding1", false, 1),   // main
                Item("bleeding1", true, 2),    // side
                Item("fracture2", true, 3),    // side
            };

            var rows = MoodleSortPlanner.PlanRows(items, plan);

            // main 行：bleeding1(1) 在 fracture2(0) 前
            Assert.Equal(new[] { 1, 0 }, rows[false]);
            // side 行：bleeding1(0) 在 fracture2(1) 前
            Assert.Equal(new[] { 0, 1 }, rows[true]);
        }

        [Fact]
        public void PlanRows_UnknownsGoToEnd_WithinEachRow()
        {
            var plan = PlanFromConfig(@"
GroupOrder = Vital
UnknownStatePolicy = End
Group.Vital.States = bleeding
");
            var items = new List<MoodleRowItem>
            {
                Item("unknownA", false, 0),
                Item("bleeding1", false, 1),
                Item("unknownB", false, 2),
            };

            var orders = MoodleSortPlanner.PlanRows(items, plan);
            // bleeding1(1) 置前，未知保持相对顺序 → [1, 0, 2]
            Assert.Equal(new[] { 1, 0, 2 }, orders[false]);
        }

        [Fact]
        public void PlanRows_KeepPolicy_UnknownsStayInPlace()
        {
            var plan = PlanFromConfig(@"
GroupOrder = Vital
UnknownStatePolicy = Keep
Group.Vital.States = bleeding, fracture
");
            var items = new List<MoodleRowItem>
            {
                Item("unknownA", false, 0),
                Item("fracture2", false, 1),
                Item("bleeding1", false, 2),
            };

            var rows = MoodleSortPlanner.PlanRows(items, plan);
            // unknownA 原位；bleeding1 → pos1，fracture2 → pos2
            Assert.Equal(new[] { 0, 2, 1 }, rows[false]);
        }

        [Fact]
        public void PlanRows_EmptyInput_ReturnsEmptyRows()
        {
            var plan = PlanFromConfig("GroupOrder = Vital");
            var rows = MoodleSortPlanner.PlanRows(new List<MoodleRowItem>(), plan);

            Assert.Empty(rows[false]);
            Assert.Empty(rows[true]);
        }

        [Fact]
        public void PlanRows_NullInput_Throws()
        {
            var plan = PlanFromConfig("GroupOrder = Vital");
            Assert.Throws<System.ArgumentNullException>(() => MoodleSortPlanner.PlanRows(null, plan));
        }

        [Fact]
        public void PlanRows_NullPlan_Throws()
        {
            var items = new List<MoodleRowItem> { Item("bleeding1", false, 0) };
            Assert.Throws<System.ArgumentNullException>(() => MoodleSortPlanner.PlanRows(items, null));
        }
    }

    public class RenderModeResolverTests
    {
        [Fact]
        public void Auto_WithLayoutGroup_UsesSiblingOrder()
        {
            Assert.Equal(RenderMode.SiblingOrder,
                RenderModeResolver.Resolve(RenderMode.Auto, hasLayoutGroup: true, hasDistinctAnchoredPositions: true));
        }

        [Fact]
        public void Auto_NoLayoutGroup_DistinctAnchored_UsesAnchoredPosition()
        {
            Assert.Equal(RenderMode.AnchoredPosition,
                RenderModeResolver.Resolve(RenderMode.Auto, hasLayoutGroup: false, hasDistinctAnchoredPositions: true));
        }

        [Fact]
        public void Auto_NoLayoutGroup_NoDistinctAnchored_FallsBackToSiblingOrder()
        {
            Assert.Equal(RenderMode.SiblingOrder,
                RenderModeResolver.Resolve(RenderMode.Auto, hasLayoutGroup: false, hasDistinctAnchoredPositions: false));
        }

        [Fact]
        public void ExplicitMode_OverridesAutoDetection()
        {
            Assert.Equal(RenderMode.SiblingOrder,
                RenderModeResolver.Resolve(RenderMode.SiblingOrder, hasLayoutGroup: false, hasDistinctAnchoredPositions: true));
            Assert.Equal(RenderMode.AnchoredPosition,
                RenderModeResolver.Resolve(RenderMode.AnchoredPosition, hasLayoutGroup: true, hasDistinctAnchoredPositions: false));
        }

        [Fact]
        public void PatternBaseId_PreservesSemanticDigitsForGeneratedWildcard()
        {
            Assert.Equal("modstate123", MoodleIdentity.PatternBaseId("modstate123*"));
            Assert.Equal("modstate123", MoodleIdentity.PatternBaseId("modstate123#"));
            Assert.Equal("bleeding", MoodleIdentity.PatternBaseId("bleeding3"));
        }
    }
}