using System.Linq;
using HealthAutoArrange.Core;
using Xunit;

namespace HealthAutoArrange.Tests
{
    public class ConfigParserTests
    {
        [Fact]
        public void Parse_ReadsGroupOrderAndStates()
        {
            var text = @"
GroupOrder = Vital, Infection
Group.Vital.States = Bleeding, Fracture
Group.Infection.States = Infection
";
            var result = ConfigTextParser.Parse(text);
            Assert.Empty(result.Warnings);
            Assert.Equal(new[] { "Vital", "Infection" }, result.Config.GroupOrder);
            Assert.Equal(new[] { "Bleeding", "Fracture" }, result.Config.GroupStates["Vital"]);
            Assert.Equal(new[] { "Infection" }, result.Config.GroupStates["Infection"]);
        }

        [Fact]
        public void Parse_UnknownStatePolicy_DefaultsToKeep()
        {
            var result = ConfigTextParser.Parse("");
            Assert.Equal(UnknownStatePolicy.Keep, result.Config.UnknownStatePolicy);
        }

        [Fact]
        public void Parse_UnknownStatePolicy_ReadsKeep()
        {
            var result = ConfigTextParser.Parse("UnknownStatePolicy = Keep");
            Assert.Equal(UnknownStatePolicy.Keep, result.Config.UnknownStatePolicy);
        }

        [Fact]
        public void Parse_InvalidUnknownStatePolicy_WarnsAndDefaults()
        {
            var result = ConfigTextParser.Parse("UnknownStatePolicy = Bogus");
            Assert.Equal(UnknownStatePolicy.Keep, result.Config.UnknownStatePolicy);
            Assert.Contains(result.Warnings, w => w.Contains("UnknownStatePolicy"));
        }

        [Fact]
        public void Parse_BlankItems_AreIgnored()
        {
            var text = "GroupOrder = Vital, , Infection, ";
            var result = ConfigTextParser.Parse(text);
            Assert.Equal(new[] { "Vital", "Infection" }, result.Config.GroupOrder);
        }

        [Fact]
        public void Parse_DuplicateGroupOrder_FirstWins()
        {
            var text = "GroupOrder = Vital, Vital, Infection";
            var result = ConfigTextParser.Parse(text);
            Assert.Equal(new[] { "Vital", "Infection" }, result.Config.GroupOrder);
        }

        [Fact]
        public void Parse_DuplicateStates_FirstDeclarationWins()
        {
            var text = @"
GroupOrder = Vital, Infection
Group.Vital.States = Bleeding, Infection
Group.Infection.States = Infection, Fracture
";
            var result = ConfigTextParser.Parse(text);
            // Infection 首次声明于 Vital（组 0，索引 1），Fracture 在 Infection 组（组 1，索引 1）
            var plan = result.Config.CreateSortPlan();
            var states = new[] { "Fracture", "Infection", "Bleeding" };
            // 期望顺序：Bleeding(2), Infection(1), Fracture(0)
            Assert.Equal(new[] { 2, 1, 0 }, plan.Apply(states));
        }

        [Fact]
        public void Parse_ReminderRules_ReadsEnabledModeCooldown()
        {
            var text = @"
Reminder.Bleeding.Enabled = true
Reminder.Bleeding.Mode = BottomAlert
Reminder.Bleeding.CooldownSeconds = 60
";
            var result = ConfigTextParser.Parse(text);
            var rule = result.Config.Reminders.Single(r => r.State == "Bleeding");
            Assert.True(rule.Enabled);
            Assert.Equal(ReminderMode.BottomAlert, rule.Mode);
            Assert.Equal(60, rule.CooldownSeconds);
        }

        [Fact]
        public void Parse_InvalidMode_DisablesRuleWithWarning()
        {
            var text = @"
Reminder.Bleeding.Enabled = true
Reminder.Bleeding.Mode = Bogus
";
            var result = ConfigTextParser.Parse(text);
            var rule = result.Config.Reminders.Single(r => r.State == "Bleeding");
            Assert.False(rule.Enabled);
            Assert.Contains(result.Warnings, w => w.Contains("Bleeding"));
        }

        [Fact]
        public void Parse_InvalidEnabled_WarnsAndDefaultsToDisabled()
        {
            var text = "Reminder.Bleeding.Enabled = maybe";
            var result = ConfigTextParser.Parse(text);
            var rule = result.Config.Reminders.Single(r => r.State == "Bleeding");
            Assert.False(rule.Enabled);
            Assert.Contains(result.Warnings, w => w.Contains("Enabled"));
        }

        [Fact]
        public void Parse_InvalidCooldown_WarnsAndDefaultsToZero()
        {
            var text = "Reminder.Bleeding.CooldownSeconds = abc";
            var result = ConfigTextParser.Parse(text);
            var rule = result.Config.Reminders.Single(r => r.State == "Bleeding");
            Assert.Equal(0, rule.CooldownSeconds);
            Assert.Contains(result.Warnings, w => w.Contains("CooldownSeconds"));
        }

        [Fact]
        public void Parse_NegativeCooldown_ClampedToZero()
        {
            var text = "Reminder.Bleeding.CooldownSeconds = -5";
            var result = ConfigTextParser.Parse(text);
            var rule = result.Config.Reminders.Single(r => r.State == "Bleeding");
            Assert.Equal(0, rule.CooldownSeconds);
        }

        [Fact]
        public void Parse_CommentsAndBlankLines_AreIgnored()
        {
            var text = @"
# comment
; another comment

GroupOrder = Vital
";
            var result = ConfigTextParser.Parse(text);
            Assert.Equal(new[] { "Vital" }, result.Config.GroupOrder);
        }

        [Fact]
        public void Parse_UnknownKeys_AreIgnored()
        {
            var text = @"
SomeRandomKey = value
GroupOrder = Vital
";
            var result = ConfigTextParser.Parse(text);
            Assert.Equal(new[] { "Vital" }, result.Config.GroupOrder);
        }

        [Fact]
        public void Parse_EmptyConfig_ProducesEmptyPlan()
        {
            var result = ConfigTextParser.Parse("");
            var plan = result.Config.CreateSortPlan();
            Assert.Equal(new[] { 0, 1 }, plan.Apply(new[] { "A", "B" }));
        }

        [Fact]
        public void Parse_EmptyConfig_ProducesEmptyGroupsAndReminders()
        {
            // 空默认配置：无分组、无提醒，逻辑不依赖任何示例模板值。
            var result = ConfigTextParser.Parse("");
            Assert.Empty(result.Config.GroupOrder);
            Assert.Empty(result.Config.GroupStates);
            Assert.Empty(result.Config.Reminders);
            Assert.Equal(UnknownStatePolicy.Keep, result.Config.UnknownStatePolicy);
        }

        [Fact]
        public void Parse_EmptyConfig_PlanKeepsOriginalOrder()
        {
            // 空默认配置下排序计划为空：所有状态视为未知，保持原始顺序。
            var result = ConfigTextParser.Parse("");
            var plan = result.Config.CreateSortPlan();
            var states = new[] { "AnyState", "AnotherState", "ThirdState" };
            Assert.Equal(new[] { 0, 1, 2 }, plan.Apply(states));
        }

        [Fact]
        public void Parse_CustomGroupNames_AreRead()
        {
            // 自定义分组：任意 Group.<name>.States 键均可被读取。
            var text = @"
GroupOrder = CustomGroupA, CustomGroupB
Group.CustomGroupA.States = StateX, StateY
Group.CustomGroupB.States = StateZ
";
            var result = ConfigTextParser.Parse(text);
            Assert.Empty(result.Warnings);
            Assert.Equal(new[] { "CustomGroupA", "CustomGroupB" }, result.Config.GroupOrder);
            Assert.Equal(new[] { "StateX", "StateY" }, result.Config.GroupStates["CustomGroupA"]);
            Assert.Equal(new[] { "StateZ" }, result.Config.GroupStates["CustomGroupB"]);
        }

        [Fact]
        public void Parse_CustomReminderRules_AreRead()
        {
            // 自定义提醒规则：任意 Reminder.<rule>.* 键均可被读取。
            var text = @"
Reminder.CustomState.Enabled = true
Reminder.CustomState.Mode = HealthPanelHint
Reminder.CustomState.CooldownSeconds = 120
Reminder.AnotherState.Enabled = false
";
            var result = ConfigTextParser.Parse(text);
            var custom = result.Config.Reminders.Single(r => r.State == "CustomState");
            Assert.True(custom.Enabled);
            Assert.Equal(ReminderMode.HealthPanelHint, custom.Mode);
            Assert.Equal(120, custom.CooldownSeconds);
            var another = result.Config.Reminders.Single(r => r.State == "AnotherState");
            Assert.False(another.Enabled);
        }

        [Fact]
        public void Parse_CustomConfigWithoutTemplateKeys_Works()
        {
            // 模拟用户删除全部默认模板键、只保留自定义键的场景。
            var text = @"
Group.MyGroup.States = MyState1, MyState2
Reminder.MyState1.Enabled = true
Reminder.MyState1.Mode = Log
";
            var result = ConfigTextParser.Parse(text);
            Assert.Equal(new[] { "MyState1", "MyState2" }, result.Config.GroupStates["MyGroup"]);
            var rule = result.Config.Reminders.Single(r => r.State == "MyState1");
            Assert.True(rule.Enabled);
            // 未声明 GroupOrder 时无排序优先级，保持原顺序。
            var plan = result.Config.CreateSortPlan();
            Assert.Equal(new[] { 0, 1 }, plan.Apply(new[] { "MyState2", "MyState1" }));
        }

        [Fact]
        public void Parse_BepInExDumpWithTemplateAndCustomKeys_ReadsAll()
        {
            // 模拟 Plugin.LoadConfig 对 BepInEx ConfigFile 的序列化输出：
            // 默认模板键（含 BepInEx 布尔值大小写）+ 用户新增的自定义键。
            var text = @"
GroupOrder = Vital, Infection, CustomGroup
UnknownStatePolicy = End
Group.Vital.States = Bleeding, Fracture
Group.Infection.States = Infection
Group.CustomGroup.States = CustomState
Reminder.Bleeding.Enabled = False
Reminder.Bleeding.Mode = Log
Reminder.Bleeding.CooldownSeconds = 60
Reminder.CustomState.Enabled = True
Reminder.CustomState.Mode = BottomAlert
Reminder.CustomState.CooldownSeconds = 30
";
            var result = ConfigTextParser.Parse(text);
            Assert.Equal(new[] { "Vital", "Infection", "CustomGroup" }, result.Config.GroupOrder);
            Assert.Equal(new[] { "Bleeding", "Fracture" }, result.Config.GroupStates["Vital"]);
            Assert.Equal(new[] { "CustomState" }, result.Config.GroupStates["CustomGroup"]);
            var custom = result.Config.Reminders.Single(r => r.State == "CustomState");
            Assert.True(custom.Enabled);
            Assert.Equal(ReminderMode.BottomAlert, custom.Mode);
            Assert.Equal(30, custom.CooldownSeconds);
            var bleeding = result.Config.Reminders.Single(r => r.State == "Bleeding");
            Assert.False(bleeding.Enabled);
        }
    }
}