using System;
using System.Collections.Generic;
using System.Linq;

namespace HealthAutoArrange.Core
{
    /// <summary>
    /// 解析结果：配置模型与解析过程中产生的警告。
    /// </summary>
    public sealed class ConfigParseResult
    {
        public ArrangeConfig Config { get; }
        public IReadOnlyList<string> Warnings { get; }

        public ConfigParseResult(ArrangeConfig config, IReadOnlyList<string> warnings)
        {
            Config = config ?? throw new ArgumentNullException(nameof(config));
            Warnings = warnings ?? throw new ArgumentNullException(nameof(warnings));
        }
    }

    /// <summary>
    /// 从键值对文本解析配置。支持 BepInEx 风格配置键：
    /// GroupOrder、Group.&lt;name&gt;.States、UnknownStatePolicy、Reminder.&lt;rule&gt;.{Enabled|Mode|CooldownSeconds}。
    /// 空白项忽略、重复项首次生效、非法值回退到默认并记录警告。
    /// </summary>
    public static class ConfigTextParser
    {
        private const string ReminderPrefix = "Reminder.";
        private const string GroupPrefix = "Group.";

        public static ConfigParseResult Parse(string text)
        {
            if (text == null) throw new ArgumentNullException(nameof(text));

            var warnings = new List<string>();
            var groupOrder = new List<string>();
            var groupStates = new Dictionary<string, List<string>>();
            var policy = UnknownStatePolicy.Keep;
            var rules = new Dictionary<string, RuleBuilder>(StringComparer.OrdinalIgnoreCase);

            foreach (var rawLine in text.Split(new[] { "\r\n", "\n", "\r" }, StringSplitOptions.None))
            {
                var line = rawLine.Trim();
                if (line.Length == 0 || line[0] == '#' || line[0] == ';') continue;

                int eq = line.IndexOf('=');
                if (eq < 0) continue;

                var key = line.Substring(0, eq).Trim();
                var value = line.Substring(eq + 1).Trim();
                if (key.Length == 0) continue;

                if (string.Equals(key, "GroupOrder", StringComparison.OrdinalIgnoreCase))
                {
                    foreach (var item in SplitList(value))
                    {
                        if (!groupOrder.Contains(item, StringComparer.OrdinalIgnoreCase))
                            groupOrder.Add(item);
                    }
                }
                else if (key.StartsWith(GroupPrefix, StringComparison.OrdinalIgnoreCase)
                         && key.EndsWith(".States", StringComparison.OrdinalIgnoreCase))
                {
                    var groupName = key.Substring(GroupPrefix.Length,
                        key.Length - GroupPrefix.Length - ".States".Length);
                    if (!groupStates.TryGetValue(groupName, out var list))
                    {
                        list = new List<string>();
                        groupStates[groupName] = list;
                    }
                    foreach (var item in SplitList(value))
                    {
                        if (!list.Contains(item, StringComparer.OrdinalIgnoreCase))
                            list.Add(item);
                    }
                }
                else if (string.Equals(key, "UnknownStatePolicy", StringComparison.OrdinalIgnoreCase))
                {
                    if (Enum.TryParse(value, true, out UnknownStatePolicy parsed))
                        policy = parsed;
                    else
                        warnings.Add($"UnknownStatePolicy: invalid value '{value}', using Keep.");
                }
                else if (key.StartsWith(ReminderPrefix, StringComparison.OrdinalIgnoreCase))
                {
                    ParseReminderKey(key, value, rules, warnings);
                }
                // 其他未知键：忽略
            }

            var reminders = new List<ReminderRule>(rules.Count);
            foreach (var rb in rules.Values)
            {
                if (!rb.ModeValid)
                {
                    rb.Enabled = false;
                    warnings.Add($"Reminder.{rb.Name}: invalid Mode '{rb.InvalidModeValue}', rule disabled.");
                }
                reminders.Add(new ReminderRule(rb.Name, rb.Name, rb.Enabled, rb.Mode, rb.CooldownSeconds));
            }

            var config = new ArrangeConfig(
                groupOrder,
                groupStates.ToDictionary(
                    kv => kv.Key,
                    kv => (IReadOnlyList<string>)kv.Value,
                    StringComparer.OrdinalIgnoreCase),
                policy,
                reminders);

            return new ConfigParseResult(config, warnings);
        }

        private static IEnumerable<string> SplitList(string value)
        {
            foreach (var item in value.Split(','))
            {
                var trimmed = item.Trim();
                if (trimmed.Length > 0) yield return trimmed;
            }
        }

        private static void ParseReminderKey(
            string key, string value, Dictionary<string, RuleBuilder> rules, List<string> warnings)
        {
            const string enabledSuffix = ".Enabled";
            const string modeSuffix = ".Mode";
            const string cooldownSuffix = ".CooldownSeconds";

            if (key.EndsWith(enabledSuffix, StringComparison.OrdinalIgnoreCase))
            {
                var name = RuleNameOf(key, enabledSuffix);
                var rb = GetOrAdd(rules, name);
                if (bool.TryParse(value, out var b))
                    rb.Enabled = b;
                else
                {
                    rb.Enabled = false;
                    warnings.Add($"Reminder.{name}.Enabled: invalid value '{value}', rule disabled.");
                }
            }
            else if (key.EndsWith(modeSuffix, StringComparison.OrdinalIgnoreCase))
            {
                var name = RuleNameOf(key, modeSuffix);
                var rb = GetOrAdd(rules, name);
                if (Enum.TryParse(value, true, out ReminderMode mode))
                    rb.Mode = mode;
                else
                {
                    rb.ModeValid = false;
                    rb.InvalidModeValue = value;
                }
            }
            else if (key.EndsWith(cooldownSuffix, StringComparison.OrdinalIgnoreCase))
            {
                var name = RuleNameOf(key, cooldownSuffix);
                var rb = GetOrAdd(rules, name);
                if (double.TryParse(value, out var d))
                {
                    if (d < 0)
                    {
                        rb.CooldownSeconds = 0;
                        warnings.Add($"Reminder.{name}.CooldownSeconds: negative value '{value}' clamped to 0.");
                    }
                    else
                    {
                        rb.CooldownSeconds = d;
                    }
                }
                else
                {
                    rb.CooldownSeconds = 0;
                    warnings.Add($"Reminder.{name}.CooldownSeconds: invalid value '{value}', using 0.");
                }
            }
        }

        private static string RuleNameOf(string key, string suffix)
        {
            return key.Substring(ReminderPrefix.Length, key.Length - ReminderPrefix.Length - suffix.Length);
        }

        private static RuleBuilder GetOrAdd(Dictionary<string, RuleBuilder> rules, string name)
        {
            if (!rules.TryGetValue(name, out var rb))
            {
                rb = new RuleBuilder { Name = name };
                rules[name] = rb;
            }
            return rb;
        }

        private sealed class RuleBuilder
        {
            public string Name;
            public bool Enabled;
            public ReminderMode Mode = ReminderMode.Log;
            public bool ModeValid = true;
            public string InvalidModeValue;
            public double CooldownSeconds;
        }
    }
}