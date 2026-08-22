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
    }
}