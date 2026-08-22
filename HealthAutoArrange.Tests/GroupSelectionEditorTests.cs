using System.Linq;
using HealthAutoArrange.Core;
using Xunit;

namespace HealthAutoArrange.Tests
{
    /// <summary>
    /// 去重分组编辑模型：选择项只能属于一个分组；
    /// 重复加入时首次分组优先并返回冲突诊断；组内不重复；分组名不区分大小写去重；空值归一化。
    /// </summary>
    public sealed class GroupSelectionEditorTests
    {
        [Fact]
        public void AddState_CreatesGroupAndAddsState()
        {
            var editor = new GroupSelectionEditor();
            var result = editor.AddState("Vital", "bleeding");

            Assert.True(result.Added);
            var group = Assert.Single(editor.Groups);
            Assert.Equal("Vital", group.Name);
            Assert.Equal("bleeding", Assert.Single(group.States));
        }

        [Fact]
        public void AddState_ConflictWhenAlreadyInOtherGroup_FirstWins()
        {
            var editor = new GroupSelectionEditor();
            Assert.True(editor.AddState("Vital", "bleeding").Added);
            var conflict = editor.AddState("Other", "bleeding");

            Assert.False(conflict.Added);
            Assert.Equal("Vital", conflict.ConflictGroup);
            Assert.Single(editor.Groups);
            Assert.Contains(editor.Groups, g => g.Name == "Vital" && g.States.Contains("bleeding"));
        }

        [Fact]
        public void AddState_DuplicateInSameGroup_NoOp()
        {
            var editor = new GroupSelectionEditor();
            Assert.True(editor.AddState("Vital", "bleeding").Added);
            var dup = editor.AddState("Vital", "bleeding");

            Assert.False(dup.Added);
            Assert.Single(editor.Groups.Single(g => g.Name == "Vital").States);
        }

        [Fact]
        public void GroupNames_CaseInsensitiveDedup()
        {
            var editor = new GroupSelectionEditor();
            Assert.True(editor.AddState("Vital", "bleeding").Added);
            Assert.True(editor.AddState("vital", "infection").Added);

            var group = Assert.Single(editor.Groups);
            Assert.Equal("Vital", group.Name);
            Assert.Equal(2, group.States.Count);
        }

        [Fact]
        public void RemoveState_RemovesFromGroup()
        {
            var editor = new GroupSelectionEditor();
            editor.AddState("Vital", "bleeding");
            editor.AddState("Vital", "infection");

            Assert.True(editor.RemoveState("Vital", "bleeding"));
            Assert.Equal("infection", Assert.Single(editor.Groups.Single(g => g.Name == "Vital").States));
        }

        [Fact]
        public void Normalize_RemovesEmptyNamesAndStates()
        {
            var editor = new GroupSelectionEditor();
            editor.AddState("Vital", "bleeding");
            editor.AddState("", "infection");
            editor.AddState("Vital", "");

            editor.Normalize();

            var group = Assert.Single(editor.Groups);
            Assert.Equal("Vital", group.Name);
            Assert.Equal("bleeding", Assert.Single(group.States));
        }

        [Fact]
        public void EnsureGroup_PreservesEmptyGroupForFirstAssignment()
        {
            var editor = new GroupSelectionEditor();
            Assert.True(editor.EnsureGroup("Priority 1"));
            Assert.Single(editor.Groups);
            Assert.Empty(editor.Groups[0].States);
            Assert.True(editor.AddState("Priority 1", "bleeding").Added);
        }
    }
}