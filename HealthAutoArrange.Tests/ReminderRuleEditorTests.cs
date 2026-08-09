using System.Linq;
using HealthAutoArrange.Core;
using Xunit;

namespace HealthAutoArrange.Tests
{
    /// <summary>
    /// 提醒规则基础模型去重：相同基础状态只能有一个规则；保留 cooldown。
    /// </summary>
    public sealed class ReminderRuleEditorTests
    {
        [Fact]
        public void AddRule_DedupsByBaseState()
        {
            var editor = new ReminderRuleEditor();
            Assert.True(editor.AddRule("bleeding1", true, ReminderMode.Log, 30).Added);
            var conflict = editor.AddRule("bleeding2", true, ReminderMode.Log, 60);

            Assert.False(conflict.Added);
            Assert.Single(editor.Rules);
            Assert.Equal("bleeding*", editor.Rules[0].State);
        }

        [Fact]
        public void AddRule_KeepsCooldown()
        {
            var editor = new ReminderRuleEditor();
            editor.AddRule("bleeding", true, ReminderMode.Log, 45);

            Assert.Equal(45, editor.Rules[0].CooldownSeconds);
        }

        [Fact]
        public void AddRule_EmptyState_Rejected()
        {
            var editor = new ReminderRuleEditor();
            var result = editor.AddRule("", true, ReminderMode.Log, 30);

            Assert.False(result.Added);
            Assert.Empty(editor.Rules);
        }

        [Fact]
        public void AddRule_GeneratesWildcardPattern()
        {
            var editor = new ReminderRuleEditor();
            editor.AddRule("bleeding", true, ReminderMode.Log, 30);

            Assert.Equal("bleeding*", editor.Rules[0].State);
            Assert.Equal("bleeding", editor.Rules[0].Name);
        }
    }
}