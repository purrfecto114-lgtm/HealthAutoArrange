using System;
using HealthAutoArrange.Core;
using Xunit;

namespace HealthAutoArrange.Tests
{
    public sealed class UiSelectionModelTests
    {
        [Fact]
        public void SelectionEditorRoundTripsGeneratedPatternsWithoutDuplicatingStates()
        {
            var model = new UiConfigModel();
            model.GroupOrder.Add("Vital");
            model.Groups.Add(new UiGroupModel("Vital", "bleeding*, bleeding3"));

            var editor = model.CreateSelectionEditor();
            Assert.Single(editor.Groups[0].States);
            Assert.Equal("bleeding", editor.Groups[0].States[0]);

            editor.AddState("Infection", "poisoned");
            model.ApplySelectionEditor(editor);

            Assert.Equal(new[] { "Vital", "Infection" }, model.GroupOrder);
            // 旧 * 模式在无关保存时保真；新目录分配生成 #。
            Assert.Equal("bleeding*", model.Groups[0].StatesText);
            Assert.Equal("poisoned#", model.Groups[1].StatesText);
        }

        [Fact]
        public void SelectionEditorSave_PreservesLegacyWildcardPattern()
        {
            var model = new UiConfigModel();
            model.GroupOrder.Add("Vital");
            model.Groups.Add(new UiGroupModel("Vital", "pain*"));

            var editor = model.CreateSelectionEditor();
            editor.AddState("Infection", "poisoned");
            model.ApplySelectionEditor(editor);

            // 无关的修改/保存不得把旧 pain* 改写为 pain#。
            Assert.Equal("pain*", model.Groups[0].StatesText);
            Assert.Equal("poisoned#", model.Groups[1].StatesText);
        }

        [Fact]
        public void SelectionEditorSave_PreservesExactPatternWithSemanticDigits()
        {
            var model = new UiConfigModel();
            model.GroupOrder.Add("Vital");
            model.Groups.Add(new UiGroupModel("Vital", "bleeding3"));

            var editor = model.CreateSelectionEditor();
            editor.AddState("Infection", "poisoned");
            model.ApplySelectionEditor(editor);

            // 无关的修改/保存不得把 exact bleeding3 改写为 bleeding#。
            Assert.Equal("bleeding3", model.Groups[0].StatesText);
            Assert.Equal("poisoned#", model.Groups[1].StatesText);
        }

        [Fact]
        public void ReminderUiDefaultsAndVisualFieldsRoundTrip()
        {
            var model = new UiConfigModel();
            var reminder = new UiReminderModel("bleeding", true, ReminderMode.Log, 2);
            model.Reminders.Add(reminder);

            Assert.Equal(ReminderTemplateFormatter.DefaultTemplate, reminder.Template);
            reminder.PresetKind = ReminderVisualPresetKind.CriticalCenter;
            reminder.Opacity = 0.8f;
            reminder.DurationSeconds = 7f;
            reminder.Placement = ReminderPlacements.Custom(0.25f, 0.75f, 12f, -8f);

            var restored = UiConfigTextSerializer.Parse(UiConfigTextSerializer.Serialize(model));
            var restoredReminder = Assert.Single(restored.Reminders);
            Assert.Equal(ReminderVisualPresetKind.CriticalCenter, restoredReminder.PresetKind);
            Assert.Equal(0.8f, restoredReminder.Opacity);
            Assert.Equal(7f, restoredReminder.DurationSeconds);
            Assert.Equal(0.25f, restoredReminder.Placement.NormalizedX);
            Assert.Equal(12f, restoredReminder.Placement.PixelOffsetX);
        }

        [Fact]
        public void CreateSelectionEditor_PreservesEmptyConfiguredGroups()
        {
            var model = new UiConfigModel();
            model.GroupOrder.Add("Priority 1");
            model.Groups.Add(new UiGroupModel("Priority 1", string.Empty));
            var editor = model.CreateSelectionEditor();
            var group = Assert.Single(editor.Groups);
            Assert.Equal("Priority 1", group.Name);
            Assert.Empty(group.States);
        }
    }
}
