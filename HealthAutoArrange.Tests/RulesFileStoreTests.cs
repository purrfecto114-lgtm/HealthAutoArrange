using System;
using System.Collections.Generic;
using System.IO;
using HealthAutoArrange.Core;
using Xunit;

namespace HealthAutoArrange.Tests
{
    /// <summary>
    /// rules 文件（独立于 BepInEx cfg 的动态配置）读写与“应用模型”逻辑测试。
    /// 应用逻辑 = UiConfigModel → ArrangeConfig → SortPlan / ReminderEngine。
    /// </summary>
    public sealed class RulesFileStoreTests : IDisposable
    {
        private readonly string _dir;

        public RulesFileStoreTests()
        {
            _dir = Path.Combine(Path.GetTempPath(), "haa_rules_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_dir);
        }

        public void Dispose()
        {
            try { Directory.Delete(_dir, true); } catch { /* ignore */ }
        }

        private static UiConfigModel SampleModel()
        {
            var model = new UiConfigModel
            {
                Enabled = false,
                GroupOrder = new List<string> { "Vital", "Infection" },
                UnknownStatePolicy = UnknownStatePolicy.Keep
            };
            model.Groups.Add(new UiGroupModel("Vital", "Bleeding, Fracture"));
            model.Groups.Add(new UiGroupModel("Infection", "Infection"));
            model.Reminders.Add(new UiReminderModel("Bleeding", true, ReminderMode.BottomAlert, 2.5));
            return model;
        }

        [Fact]
        public void WriteThenRead_RoundTripsModel()
        {
            var path = Path.Combine(_dir, "rules.cfg");
            RulesFileStore.Write(path, SampleModel());

            var loaded = RulesFileStore.Read(path);

            Assert.NotNull(loaded);
            Assert.False(loaded.Enabled);
            Assert.Equal(new[] { "Vital", "Infection" }, loaded.GroupOrder);
            Assert.Equal(UnknownStatePolicy.Keep, loaded.UnknownStatePolicy);
            Assert.Equal("Bleeding, Fracture", loaded.Groups[0].StatesText);
            Assert.Equal("Infection", loaded.Groups[1].StatesText);
            Assert.Equal("Bleeding", loaded.Reminders[0].Name);
            Assert.Equal(ReminderMode.BottomAlert, loaded.Reminders[0].Mode);
            Assert.Equal(2.5, loaded.Reminders[0].CooldownSeconds);
        }

        [Fact]
        public void Write_ReplacesExistingFile()
        {
            var path = Path.Combine(_dir, "rules.cfg");
            RulesFileStore.Write(path, SampleModel());

            var updated = new UiConfigModel { GroupOrder = new List<string> { "Rear" } };
            updated.Groups.Add(new UiGroupModel("Rear", "stunned"));
            RulesFileStore.Write(path, updated);

            var loaded = RulesFileStore.Read(path);
            Assert.Equal(new[] { "Rear" }, loaded.GroupOrder);
            Assert.Equal("stunned", loaded.Groups[0].StatesText);
        }

        [Fact]
        public void Write_CreatesFileWhenMissing()
        {
            var path = Path.Combine(_dir, "new.cfg");
            RulesFileStore.Write(path, SampleModel());
            Assert.True(File.Exists(path));
        }

        [Fact]
        public void Read_MissingFile_ReturnsNull()
        {
            var path = Path.Combine(_dir, "missing.cfg");
            Assert.Null(RulesFileStore.Read(path));
        }

        [Fact]
        public void Read_GarbageContent_DoesNotThrow_ReturnsEmptyModel()
        {
            // 解析器对未知/损坏内容宽容：不抛异常，返回空模型。
            var path = Path.Combine(_dir, "bad.cfg");
            File.WriteAllText(path, "\u0000\u0001\u0002 not a config \uFFFF");
            var loaded = RulesFileStore.Read(path);
            Assert.NotNull(loaded);
            Assert.Empty(loaded.GroupOrder);
            Assert.Empty(loaded.Groups);
        }
    }

    /// <summary>
    /// 应用逻辑：UiConfigModel → ArrangeConfig → SortPlan / ReminderEngine。
    /// 与 Plugin.ApplyModel 使用同一路径。
    /// </summary>
    public sealed class ApplyModelTests
    {
        private static UiConfigModel ModelFromText(string text)
        {
            return UiConfigTextSerializer.Parse(text);
        }

        [Fact]
        public void ApplyModel_ProducesSortPlan_WithDynamicGroups()
        {
            var model = ModelFromText(@"
Enabled = true
GroupOrder = Vital, Infection
Group.Vital.States = Bleeding, Fracture
Group.Infection.States = Infection
UnknownStatePolicy = End
");

            var config = model.ToConfig();
            var plan = config.CreateSortPlan();
            var states = new[] { "infection1", "fracture2", "bleeding1" };
            // bleeding1 → (0,0)，fracture2 → (0,1)，infection1 → (1,0)
            Assert.Equal(new[] { 2, 1, 0 }, plan.Apply(states));
        }

        [Fact]
        public void ApplyModel_ProducesReminderEngine_WithRules()
        {
            var model = ModelFromText(@"
Reminder.Bleeding.Enabled = true
Reminder.Bleeding.Mode = BottomAlert
Reminder.Bleeding.CooldownSeconds = 60
");

            var config = model.ToConfig();
            var engine = new ReminderEngine(config.Reminders);
            var messages = engine.Update(new[] { "Bleeding" }, new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero));

            Assert.Single(messages);
            Assert.Equal("Bleeding", messages[0].State);
            Assert.Equal(ReminderMode.BottomAlert, messages[0].Mode);
        }

        [Fact]
        public void ApplyModel_EmptyModel_ProducesNoOpPlan()
        {
            var model = new UiConfigModel();
            var config = model.ToConfig();
            var plan = config.CreateSortPlan();

            Assert.Equal(new[] { 0, 1 }, plan.Apply(new[] { "A", "B" }));
            Assert.Empty(config.Reminders);
        }
    }
}