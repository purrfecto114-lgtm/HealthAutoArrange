using System;
using System.Collections.Generic;
using HealthAutoArrange.Core;
using Xunit;

namespace HealthAutoArrange.Tests
{
    public class SortPlanTests
    {
        private static SortPlan CreatePlan(
            IReadOnlyDictionary<string, (int Group, int Index)> priorities,
            UnknownStatePolicy policy = UnknownStatePolicy.End)
        {
            return new SortPlan(priorities, policy);
        }

        [Fact]
        public void EmptyInput_ReturnsEmpty()
        {
            var plan = CreatePlan(new Dictionary<string, (int, int)>());
            var result = plan.Apply(Array.Empty<string>());
            Assert.Empty(result);
        }

        [Fact]
        public void KnownStates_SortedByGroupThenInGroupOrder()
        {
            var priorities = new Dictionary<string, (int, int)>
            {
                ["Bleeding"] = (0, 0),
                ["Fracture"] = (0, 1),
                ["Infection"] = (1, 0),
            };
            var plan = CreatePlan(priorities);
            var states = new[] { "Infection", "Bleeding", "Fracture" };
            var result = plan.Apply(states);
            // Expected order: Bleeding(1), Fracture(2), Infection(0)
            Assert.Equal(new[] { 1, 2, 0 }, result);
        }

        [Fact]
        public void UnknownStates_GoToEnd_PreservingRelativeOrder()
        {
            var priorities = new Dictionary<string, (int, int)>
            {
                ["Bleeding"] = (0, 0),
            };
            var plan = CreatePlan(priorities);
            var states = new[] { "UnknownA", "Bleeding", "UnknownB" };
            var result = plan.Apply(states);
            // Expected: Bleeding(1), UnknownA(0), UnknownB(2)
            Assert.Equal(new[] { 1, 0, 2 }, result);
        }

        [Fact]
        public void SamePriority_KeepsOriginalOrder()
        {
            var priorities = new Dictionary<string, (int, int)>
            {
                ["Bleeding"] = (0, 0),
                ["Fracture"] = (0, 0),
            };
            var plan = CreatePlan(priorities);
            var states = new[] { "Fracture", "Bleeding" };
            var result = plan.Apply(states);
            // Same priority -> stable: Fracture(0), Bleeding(1)
            Assert.Equal(new[] { 0, 1 }, result);
        }

        [Fact]
        public void AllUnknown_KeepsOriginalOrder()
        {
            var plan = CreatePlan(new Dictionary<string, (int, int)>());
            var states = new[] { "C", "A", "B" };
            var result = plan.Apply(states);
            Assert.Equal(new[] { 0, 1, 2 }, result);
        }

        [Fact]
        public void KeepPolicy_UnknownStatesStayInPlace()
        {
            var priorities = new Dictionary<string, (int, int)>
            {
                ["Bleeding"] = (0, 0),
                ["Infection"] = (1, 0),
            };
            var plan = CreatePlan(priorities, UnknownStatePolicy.Keep);
            var states = new[] { "UnknownA", "Bleeding", "UnknownB", "Infection" };
            var result = plan.Apply(states);
            // Unknowns stay at positions 0 and 2; knowns fill remaining slots by priority.
            Assert.Equal(new[] { 0, 1, 2, 3 }, result);
        }

        [Fact]
        public void NullInput_Throws()
        {
            var plan = CreatePlan(new Dictionary<string, (int, int)>());
            Assert.Throws<ArgumentNullException>(() => plan.Apply(null));
        }

        // ---- v1.2.3: in-group intensity ordering ---------------------------------

        private static SortPlan CreateIntensityPlan(
            IReadOnlyDictionary<string, (int Group, int Index)> priorities,
            InGroupSortMode mode)
        {
            return new SortPlan(StateMatcher.FromExact(priorities), UnknownStatePolicy.End, mode);
        }

        private static IReadOnlyDictionary<string, (int, int)> IntensityGroup()
        {
            return new Dictionary<string, (int, int)>
            {
                ["bleeding"] = (0, 0),
                ["shock"] = (0, 1),
                ["pain"] = (0, 2),
            };
        }

        [Fact]
        public void IntensityDesc_OrdersWithinGroup_StrongestFirst()
        {
            var plan = CreateIntensityPlan(IntensityGroup(), InGroupSortMode.IntensityDesc);
            var states = new[] { "pain2", "bleeding1", "shock3" };
            var result = plan.Apply(states, new[] { 2, 1, 3 });
            // shock3 (3) > pain2 (2) > bleeding1 (1)
            Assert.Equal(new[] { 2, 0, 1 }, result);
        }

        [Fact]
        public void IntensityDesc_TieFallsBackToRuleOrder()
        {
            var plan = CreateIntensityPlan(IntensityGroup(), InGroupSortMode.IntensityDesc);
            var states = new[] { "pain2", "bleeding2", "shock2" };
            var result = plan.Apply(states, new[] { 2, 2, 2 });
            // Equal intensity: rule order bleeding(0), shock(1), pain(2)
            Assert.Equal(new[] { 1, 2, 0 }, result);
        }

        [Fact]
        public void IntensityAsc_OrdersWithinGroup_WeakestFirst()
        {
            var plan = CreateIntensityPlan(IntensityGroup(), InGroupSortMode.IntensityAsc);
            var states = new[] { "pain2", "bleeding1", "shock3" };
            var result = plan.Apply(states, new[] { 2, 1, 3 });
            // bleeding1 (1) < pain2 (2) < shock3 (3)
            Assert.Equal(new[] { 1, 0, 2 }, result);
        }

        [Fact]
        public void IntensityModes_UnknownIntensitySortsAfterKnown()
        {
            var desc = CreateIntensityPlan(IntensityGroup(), InGroupSortMode.IntensityDesc);
            // bleeding has no intensity data (-1); shock 0 and pain 1 are known.
            var states = new[] { "bleeding", "shock0", "pain1" };
            var resultDesc = desc.Apply(states, new[] { -1, 0, 1 });
            Assert.Equal(new[] { 2, 1, 0 }, resultDesc);

            var asc = CreateIntensityPlan(IntensityGroup(), InGroupSortMode.IntensityAsc);
            var resultAsc = asc.Apply(states, new[] { -1, 0, 1 });
            Assert.Equal(new[] { 1, 2, 0 }, resultAsc);
        }

        [Fact]
        public void IntensityDesc_NullAndShortIntensityLists_TreatedAsUnknown()
        {
            var plan = CreateIntensityPlan(IntensityGroup(), InGroupSortMode.IntensityDesc);
            var states = new[] { "pain2", "bleeding1", "shock3" };
            // No intensity list at all: falls back to rule order (group index).
            Assert.Equal(new[] { 1, 2, 0 }, plan.Apply(states));
            // Shorter list: the third entry (shock3) is unknown, sorts after knowns.
            Assert.Equal(new[] { 0, 1, 2 }, plan.Apply(states, new[] { 2, 1 }));
        }

        [Fact]
        public void RuleIndexMode_IgnoresIntensities_LegacyBehavior()
        {
            var plan = CreateIntensityPlan(IntensityGroup(), InGroupSortMode.RuleIndex);
            var states = new[] { "pain2", "bleeding1", "shock3" };
            var result = plan.Apply(states, new[] { 2, 1, 3 });
            // Rule order bleeding(0), shock(1), pain(2) regardless of intensity.
            Assert.Equal(new[] { 1, 2, 0 }, result);
        }

        [Fact]
        public void GroupOrder_DominatesIntensityMode()
        {
            var priorities = new Dictionary<string, (int, int)>
            {
                ["bleeding"] = (0, 0),
                ["infection"] = (1, 0),
            };
            var plan = CreateIntensityPlan(priorities, InGroupSortMode.IntensityDesc);
            var states = new[] { "infection0", "bleeding3" };
            var result = plan.Apply(states, new[] { 0, 3 });
            // Group 0 (bleeding) first even though infection has lower intensity.
            Assert.Equal(new[] { 1, 0 }, result);
        }
    }
}