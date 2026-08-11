using System.Collections.Generic;
using HealthAutoArrange.Core;
using Xunit;

namespace HealthAutoArrange.Tests
{
    public sealed class UiConfigModelTests
    {
        [Fact]
        public void FromConfigAndToConfigRoundTripsDynamicGroupsAndReminders()
        {
            var parsed = ConfigTextParser.Parse(
                "GroupOrder = Front, Rear\n"
                + "Group.Front.States = bleeding*, poisoned\n"
                + "Group.Rear.States = stunned\n"
                + "UnknownStatePolicy = Keep\n"
                + "Reminder.bleeding.Enabled = true\n"
                + "Reminder.bleeding.Mode = BottomAlert\n"
                + "Reminder.bleeding.CooldownSeconds = 2.5");

            var model = UiConfigModel.FromConfig(parsed.Config, true);
            Assert.True(model.Enabled);
            Assert.Equal(new[] { "Front", "Rear" }, model.GroupOrder);
            Assert.Equal("bleeding*, poisoned", model.Groups[0].StatesText);
            Assert.Equal("stunned", model.Groups[1].StatesText);
            Assert.Equal("bleeding", model.Reminders[0].Name);

            var roundTrip = model.ToConfig();
            Assert.Equal(parsed.Config.GroupOrder, roundTrip.GroupOrder);
            Assert.Equal(parsed.Config.GroupStates["Front"], roundTrip.GroupStates["Front"]);
            Assert.Equal(UnknownStatePolicy.Keep, roundTrip.UnknownStatePolicy);
            Assert.Equal(ReminderMode.BottomAlert, roundTrip.Reminders[0].Mode);
            Assert.Equal(2.5, roundTrip.Reminders[0].CooldownSeconds);
        }

        [Fact]
        public void SerializeWritesAllDynamicFieldsUsingInvariantNumbers()
        {
            var model = new UiConfigModel
            {
                Enabled = false,
                GroupOrder = new List<string> { "医务", "后排" },
                UnknownStatePolicy = UnknownStatePolicy.Keep
            };
            model.Groups.Add(new UiGroupModel("医务", "bleeding*, poisoned"));
            model.Reminders.Add(new UiReminderModel("bleeding", true, ReminderMode.HealthPanelHint, 2.5));

            var text = UiConfigTextSerializer.Serialize(model);
            Assert.Contains("Enabled = false", text);
            Assert.Contains("GroupOrder = 医务, 后排", text);
            Assert.Contains("Reminder.bleeding.RepeatMode = WhilePresent", text);
            Assert.Contains("Reminder.bleeding.PeriodSeconds = 2.5", text);
            Assert.Contains("Reminder.bleeding.SendsPerPeriod = 1", text);

            var restored = UiConfigTextSerializer.Parse(text);
            Assert.False(restored.Enabled);
            Assert.Equal("bleeding*, poisoned", restored.Groups[0].StatesText);
        }

        [Fact]
        public void NormalizeRemovesEmptyNamesAndKeepsFirstDuplicate()
        {
            var model = new UiConfigModel
            {
                GroupOrder = new List<string> { "", " Front ", "front", "Rear" }
            };
            model.Groups.Add(new UiGroupModel("", "ignored"));
            model.Groups.Add(new UiGroupModel(" Front ", "first"));
            model.Groups.Add(new UiGroupModel("front", "second"));
            model.Groups.Add(new UiGroupModel("Rear", "third"));
            model.Reminders.Add(new UiReminderModel("", true, ReminderMode.Log, 1));
            model.Reminders.Add(new UiReminderModel(" Rule ", true, ReminderMode.Log, 2));
            model.Reminders.Add(new UiReminderModel("rule", false, ReminderMode.BottomAlert, 3));

            model.Normalize();

            Assert.Equal(new[] { "Front", "Rear" }, model.GroupOrder);
            Assert.Equal(new[] { "Front", "Rear" }, model.Groups.ConvertAll(x => x.Name));
            Assert.Equal("first", model.Groups[0].StatesText);
            Assert.Single(model.Reminders);
            Assert.Equal("Rule", model.Reminders[0].Name);
            Assert.True(model.Reminders[0].Enabled);
        }

        [Fact]
        public void SerializeNormalizesBeforeWriting()
        {
            var model = new UiConfigModel();
            model.Groups.Add(new UiGroupModel("", "ignored"));
            model.Groups.Add(new UiGroupModel("Group", "first"));
            model.Groups.Add(new UiGroupModel("group", "second"));

            var text = UiConfigTextSerializer.Serialize(model);

            Assert.DoesNotContain("Group..States", text);
            Assert.Equal(1, text.Split(new[] { "Group.Group.States" }, System.StringSplitOptions.None).Length - 1);
            Assert.Contains("Group.Group.States = first", text);
        }

        [Fact]
        public void CloneCreatesIndependentEditableSnapshot()
        {
            var model = new UiConfigModel { Enabled = true, GroupOrder = new List<string> { "A" } };
            model.Groups.Add(new UiGroupModel("A", "bleeding#"));
            var reminder = new UiReminderModel("bleeding#", true, ReminderMode.BottomAlert, ReminderRepeatMode.Once, 30d, 1);
            reminder.Template = "{name}";
            reminder.Placement = ReminderPlacements.Custom(0.25f, 0.75f, 10f, -20f);
            model.Reminders.Add(reminder);

            var clone = model.Clone();
            clone.Enabled = false;
            clone.GroupOrder[0] = "B";
            clone.Groups[0].Name = "B";
            clone.Reminders[0].Template = "changed";
            clone.Reminders[0].Placement = ReminderPlacements.Center();

            Assert.True(model.Enabled);
            Assert.Equal("A", model.GroupOrder[0]);
            Assert.Equal("A", model.Groups[0].Name);
            Assert.Equal("{name}", model.Reminders[0].Template);
            Assert.Equal(ReminderPlacementPreset.Custom, model.Reminders[0].Placement.Preset);
        }
    }
}
