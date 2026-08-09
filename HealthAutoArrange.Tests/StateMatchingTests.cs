using HealthAutoArrange.Core;
using Xunit;

namespace HealthAutoArrange.Tests
{
    /// <summary>
    /// 配置驱动的状态匹配策略测试。
    /// 游戏 Moodle.type 为 图标名+强度后缀（如 bleeding1、braindamage3），
    /// 配置中的状态名需支持 exact、prefix 通配符（如 bleeding*）、
    /// 以及去除末尾数字后与基础名匹配三种方式。
    /// </summary>
    public class StateMatchingTests
    {
        private static SortPlan PlanFromConfig(string text)
        {
            return ConfigTextParser.Parse(text).Config.CreateSortPlan();
        }

        [Fact]
        public void BaseNameMatch_IntensitySuffix_MatchesConfigState()
        {
            var plan = PlanFromConfig(@"
GroupOrder = Vital
Group.Vital.States = Bleeding, Fracture
");
            var states = new[] { "fracture2", "bleeding1" };
            // bleeding1 → (0,0)，fracture2 → (0,1)
            Assert.Equal(new[] { 1, 0 }, plan.Apply(states));
        }

        [Fact]
        public void PrefixWildcard_MatchesAllIntensityVariants()
        {
            var plan = PlanFromConfig(@"
GroupOrder = Vital, Infection
Group.Vital.States = bleeding*
Group.Infection.States = infection*
");
            var states = new[] { "infection1", "bleeding3" };
            Assert.Equal(new[] { 1, 0 }, plan.Apply(states));
        }

        [Fact]
        public void ExactMatch_StillWorks()
        {
            var plan = PlanFromConfig(@"
GroupOrder = Vital
UnknownStatePolicy = End
Group.Vital.States = Bleeding
");
            Assert.Equal(new[] { 0 }, plan.Apply(new[] { "Bleeding" }));
        }

        [Fact]
        public void BaseNameMatch_DoesNotMatchNonNumericSuffix()
        {
            var plan = PlanFromConfig(@"
GroupOrder = Vital
UnknownStatePolicy = End
Group.Vital.States = Bleeding
");
            var states = new[] { "BleedingX", "Bleeding" };
            // "BleedingX" 末尾非数字，基础名不匹配 → 未知 → 末尾
            Assert.Equal(new[] { 1, 0 }, plan.Apply(states));
        }

        [Fact]
        public void FirstDeclarationWins_AcrossPatterns()
        {
            var plan = PlanFromConfig(@"
GroupOrder = Vital, Infection
Group.Vital.States = bleeding*, wound
Group.Infection.States = bleeding1, infection
");
            var states = new[] { "infection", "bleeding1", "wound" };
            // bleeding1 命中 Vital 的 bleeding*（组0）而非 Infection 的 bleeding1（组1）
            Assert.Equal(new[] { 1, 2, 0 }, plan.Apply(states));
        }

        [Fact]
        public void UnknownStates_StillGoToEnd_WithPatternMatching()
        {
            var plan = PlanFromConfig(@"
GroupOrder = Vital
UnknownStatePolicy = End
Group.Vital.States = Bleeding
");
            var states = new[] { "Unknown", "bleeding1" };
            Assert.Equal(new[] { 1, 0 }, plan.Apply(states));
        }

        [Fact]
        public void KeepPolicy_UnknownsStayInPlace_WithPatternMatching()
        {
            var plan = PlanFromConfig(@"
GroupOrder = Vital
UnknownStatePolicy = Keep
Group.Vital.States = Bleeding, Fracture
");
            var states = new[] { "UnknownA", "fracture2", "bleeding1" };
            // UnknownA 原位；bleeding1 → (0,0) 填 pos1，fracture2 → (0,1) 填 pos2
            Assert.Equal(new[] { 0, 2, 1 }, plan.Apply(states));
        }

        [Fact]
        public void StableSort_WithPatternMatching()
        {
            var plan = PlanFromConfig(@"
GroupOrder = Vital, Infection
Group.Vital.States = bleeding*
Group.Infection.States = infection*
");
            var states = new[] { "infection1", "bleeding2", "bleeding1" };
            // bleeding2、bleeding1 同优先级 (0,0)，稳定排序保持原始相对顺序
            Assert.Equal(new[] { 1, 2, 0 }, plan.Apply(states));
        }

        [Fact]
        public void Matching_IsCaseInsensitive()
        {
            var plan = PlanFromConfig(@"
GroupOrder = Vital
Group.Vital.States = Bleeding, Fracture
");
            var states = new[] { "fracture", "bleeding" };
            Assert.Equal(new[] { 1, 0 }, plan.Apply(states));
        }

        [Fact]
        public void PrefixWildcard_DoesNotMatchShorterState()
        {
            var plan = PlanFromConfig(@"
GroupOrder = Vital
UnknownStatePolicy = End
Group.Vital.States = bleeding*
");
            var states = new[] { "bleed", "bleeding1" };
            // "bleed" 不以 "bleeding" 开头 → 未知 → 末尾
            Assert.Equal(new[] { 1, 0 }, plan.Apply(states));
        }
    }
}
