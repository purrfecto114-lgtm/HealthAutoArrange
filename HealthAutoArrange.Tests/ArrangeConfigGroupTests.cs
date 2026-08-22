using System.Collections.Generic;
using HealthAutoArrange.Core;
using Xunit;

namespace HealthAutoArrange.Tests
{
    /// <summary>
    /// ArrangeConfig 的组名解析：运行时提醒上下文需要知道状态属于哪个分组。
    /// </summary>
    public sealed class ArrangeConfigGroupTests
    {
        private static ArrangeConfig Config()
        {
            var groups = new Dictionary<string, IReadOnlyList<string>>
            {
                { "Vital", new[] { "bleeding*", "fracture" } },
                { "Infection", new[] { "infection*" } }
            };
            return new ArrangeConfig(
                new List<string> { "Vital", "Infection" },
                groups,
                UnknownStatePolicy.End,
                new List<ReminderRule>());
        }

        [Fact]
        public void ResolveGroupName_PrefixMatch()
        {
            Assert.Equal("Vital", Config().ResolveGroupName("bleeding3"));
        }

        [Fact]
        public void ResolveGroupName_BaseNameMatch()
        {
            Assert.Equal("Vital", Config().ResolveGroupName("Fracture2"));
        }

        [Fact]
        public void ResolveGroupName_NoMatch_ReturnsEmpty()
        {
            Assert.Equal(string.Empty, Config().ResolveGroupName("unknown1"));
        }
    }
}