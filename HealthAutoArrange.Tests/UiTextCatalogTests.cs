using HealthAutoArrange.Core;
using Xunit;

namespace HealthAutoArrange.Tests
{
    public sealed class UiTextCatalogTests
    {
        [Fact]
        public void ChineseCatalogUsesCompactSafetyFocusedLabels()
        {
            var text = UiTextCatalog.ForLanguage(true);
            Assert.Equal("状态图标自动整理", text.WindowTitle);
            Assert.Equal("基础设置", text.Basic);
            Assert.Equal("自动整理状态图标", text.Enabled);
            Assert.Equal("排序分组", text.Groups);
            Assert.Equal("状态提醒（实验性）", text.ReminderRules);
            Assert.Equal("保持原位（推荐）", text.UnknownPolicy(UnknownStatePolicy.Keep));
            Assert.Contains("不会改变伤病判定", text.EnabledHelp);
            Assert.Contains("实际", text.CatalogHelp);
        }

        [Fact]
        public void EnglishCatalogExplainsUnsupportedLegacyReminderMode()
        {
            var text = UiTextCatalog.ForLanguage(false);
            Assert.Equal("Moodle Auto Arrange", text.WindowTitle);
            Assert.Equal("Keep position (recommended)", text.UnknownPolicy(UnknownStatePolicy.Keep));
            Assert.Contains("legacy/unimplemented", text.ReminderMode(ReminderMode.HealthPanelHint));
        }

        [Fact]
        public void PreviewFallbacksRemainLocalized()
        {
            Assert.Equal("状态提醒预览", UiTextCatalog.ForLanguage(true).PreviewFallback);
            Assert.Equal("State reminder preview", UiTextCatalog.ForLanguage(false).PreviewFallback);
        }


        [Fact]
        public void LanguageButtonShowsTargetLanguageAndHelpIsLocalized()
        {
            var zh = UiTextCatalog.ForLanguage(true);
            var en = UiTextCatalog.ForLanguage(false);
            Assert.Equal("EN", zh.LanguageButton);
            Assert.Equal("中文", en.LanguageButton);
            Assert.Contains("不会修改游戏语言", zh.LanguageHelp);
            Assert.Contains("does not change the game language", en.LanguageHelp);
            Assert.Equal("选择状态", zh.SelectState);
            Assert.Equal("Select state", en.SelectState);
        }

        [Fact]
        public void UpdateLabelsExposeSafetyAndAvailabilityWithoutOpeningAdvancedSection()
        {
            var zh = UiTextCatalog.ForLanguage(true);
            var en = UiTextCatalog.ForLanguage(false);
            Assert.Contains("1.1.7", zh.UpdateBadge("1.1.7"));
            Assert.Contains("1.1.7", en.UpdateBadge("1.1.7"));
            Assert.Contains("RSA", zh.UpdateSecurityHelp);
            Assert.Contains("SHA-256", en.UpdateSecurityHelp);
            Assert.Contains("不会", zh.UpdateInstallNote);
        }
    }
}
