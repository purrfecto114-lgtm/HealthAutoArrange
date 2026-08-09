using System;
using System.Linq;
using HealthAutoArrange.Core;
using Xunit;

namespace HealthAutoArrange.Tests
{
    /// <summary>
    /// 选择结果生成：由选中的基础状态生成 Group.&lt;name&gt;.States 模式，
    /// 模式由系统生成（非玩家手写）；未知状态策略保持 End/Keep。
    /// </summary>
    public sealed class SelectionResultGeneratorTests
    {
        [Fact]
        public void GenerateGroupStates_ProducesSeverityFamilyPatterns()
        {
            var editor = new GroupSelectionEditor();
            editor.AddState("Vital", "bleeding");
            editor.AddState("Vital", "fracture");
            editor.AddState("Infection", "infection");

            var states = SelectionResultGenerator.GenerateGroupStates(editor);

            Assert.Equal(new[] { "bleeding#", "fracture#" }, states["Vital"]);
            Assert.Equal(new[] { "infection#" }, states["Infection"]);
        }

        [Fact]
        public void GenerateConfig_PreservesUnknownPolicy()
        {
            var editor = new GroupSelectionEditor();
            editor.AddState("Vital", "bleeding");

            var config = SelectionResultGenerator.GenerateConfig(
                editor, UnknownStatePolicy.Keep, Array.Empty<ReminderRule>());

            Assert.Equal(UnknownStatePolicy.Keep, config.UnknownStatePolicy);
            Assert.Equal(new[] { "bleeding#" }, config.GroupStates["Vital"]);
        }

        [Fact]
        public void GenerateConfig_IncludesReminders()
        {
            var editor = new GroupSelectionEditor();
            editor.AddState("Vital", "bleeding");
            var rule = new ReminderRule("bleeding", "bleeding*", true, ReminderMode.Log, 30);

            var config = SelectionResultGenerator.GenerateConfig(
                editor, UnknownStatePolicy.End, new[] { rule });

            Assert.Single(config.Reminders);
            Assert.Equal("bleeding*", config.Reminders[0].State);
        }

        [Fact]
        public void GenerateGroupStates_PreservesSemanticTrailingDigits()
        {
            var editor = new GroupSelectionEditor();
            editor.AddState("Mods", "drug2");

            var states = SelectionResultGenerator.GenerateGroupStates(editor);

            Assert.Contains("drug2#", states["Mods"]);
        }
    }
}