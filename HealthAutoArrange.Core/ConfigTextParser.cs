using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace HealthAutoArrange.Core
{
    /// <summary>Parse result: config plus non-fatal diagnostics.</summary>
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
    /// Parses BepInEx-style key/value text.
    /// Reminder v1.1.2 keys:
    /// Reminder.&lt;rule&gt;.{Enabled|Mode|RepeatMode|PeriodSeconds|SendsPerPeriod}.
    /// Legacy Reminder.&lt;rule&gt;.CooldownSeconds remains readable for v1.1.1 migration.
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
            var groupStates = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
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
            }

            var reminders = new List<ReminderRule>(rules.Count);
            foreach (var rb in rules.Values)
            {
                if (string.IsNullOrWhiteSpace(rb.Name))
                {
                    warnings.Add("Reminder with empty name ignored.");
                    continue;
                }

                if (!rb.ModeValid)
                {
                    rb.Enabled = false;
                    warnings.Add($"Reminder.{rb.Name}: invalid Mode '{rb.InvalidModeValue}', rule disabled.");
                }

                var repeatMode = rb.RepeatMode;
                var period = rb.PeriodSeconds;
                var sends = rb.SendsPerPeriod;

                // v1.1.1 migration. Treat each new frequency field independently so a hand-edited
                // partial 1.1.2 config does not accidentally discard a valid legacy cooldown.
                if (rb.LegacyCooldownSeconds.HasValue)
                {
                    var legacy = rb.LegacyCooldownSeconds.Value;
                    if (!rb.HasRepeatModeField)
                        repeatMode = legacy > 0d ? ReminderRepeatMode.WhilePresent : ReminderRepeatMode.Once;
                    if (!rb.HasPeriodField && legacy > 0d)
                        period = legacy;
                    if (!rb.HasSendsField)
                        sends = ReminderRule.DefaultSendsPerPeriod;

                    if (legacy <= 0d && !rb.HasRepeatModeField)
                        warnings.Add($"Reminder.{rb.Name}.CooldownSeconds=0 migrated to RepeatMode=Once to prevent every-refresh spam.");
                }

                var normalizedPeriod = ReminderRule.NormalizePeriod(period);
                var normalizedSends = ReminderRule.NormalizeSends(normalizedPeriod, sends);
                if (normalizedPeriod != period)
                    warnings.Add($"Reminder.{rb.Name}.PeriodSeconds normalized to {normalizedPeriod.ToString(CultureInfo.InvariantCulture)}.");
                if (normalizedSends != sends)
                    warnings.Add($"Reminder.{rb.Name}.SendsPerPeriod normalized to {normalizedSends} so the effective interval is at least {ReminderRule.MinimumEffectiveIntervalSeconds.ToString(CultureInfo.InvariantCulture)} second.");

                reminders.Add(new ReminderRule(
                    rb.Name,
                    rb.Name,
                    rb.Enabled,
                    rb.Mode,
                    repeatMode,
                    normalizedPeriod,
                    normalizedSends));
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
            const string repeatModeSuffix = ".RepeatMode";
            const string periodSuffix = ".PeriodSeconds";
            const string sendsSuffix = ".SendsPerPeriod";
            const string legacyCooldownSuffix = ".CooldownSeconds";

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
            else if (key.EndsWith(repeatModeSuffix, StringComparison.OrdinalIgnoreCase))
            {
                var name = RuleNameOf(key, repeatModeSuffix);
                var rb = GetOrAdd(rules, name);
                rb.HasRepeatModeField = true;
                if (Enum.TryParse(value, true, out ReminderRepeatMode repeatMode))
                    rb.RepeatMode = repeatMode;
                else
                {
                    rb.RepeatMode = ReminderRepeatMode.Once;
                    warnings.Add($"Reminder.{name}.RepeatMode: invalid value '{value}', using Once.");
                }
            }
            else if (key.EndsWith(periodSuffix, StringComparison.OrdinalIgnoreCase))
            {
                var name = RuleNameOf(key, periodSuffix);
                var rb = GetOrAdd(rules, name);
                rb.HasPeriodField = true;
                if (TryParseInvariantDouble(value, out var d))
                {
                    var normalized = ReminderRule.NormalizePeriod(d);
                    if (normalized != d)
                        warnings.Add($"Reminder.{name}.PeriodSeconds: value '{value}' normalized to {normalized.ToString(CultureInfo.InvariantCulture)}.");
                    rb.PeriodSeconds = normalized;
                }
                else
                {
                    rb.PeriodSeconds = ReminderRule.DefaultPeriodSeconds;
                    warnings.Add($"Reminder.{name}.PeriodSeconds: invalid value '{value}', using {ReminderRule.DefaultPeriodSeconds.ToString(CultureInfo.InvariantCulture)}.");
                }
            }
            else if (key.EndsWith(sendsSuffix, StringComparison.OrdinalIgnoreCase))
            {
                var name = RuleNameOf(key, sendsSuffix);
                var rb = GetOrAdd(rules, name);
                rb.HasSendsField = true;
                if (int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var n))
                {
                    var normalized = ReminderRule.NormalizeSends(n);
                    if (normalized != n)
                        warnings.Add($"Reminder.{name}.SendsPerPeriod: value '{value}' normalized to {normalized}.");
                    rb.SendsPerPeriod = normalized;
                }
                else
                {
                    rb.SendsPerPeriod = ReminderRule.DefaultSendsPerPeriod;
                    warnings.Add($"Reminder.{name}.SendsPerPeriod: invalid value '{value}', using {ReminderRule.DefaultSendsPerPeriod}.");
                }
            }
            else if (key.EndsWith(legacyCooldownSuffix, StringComparison.OrdinalIgnoreCase))
            {
                var name = RuleNameOf(key, legacyCooldownSuffix);
                var rb = GetOrAdd(rules, name);
                if (TryParseInvariantDouble(value, out var d))
                {
                    rb.LegacyCooldownSeconds = Math.Max(0d, d);
                    if (d < 0d)
                        warnings.Add($"Reminder.{name}.CooldownSeconds: negative value '{value}' clamped to 0 before migration.");
                }
                else
                {
                    rb.LegacyCooldownSeconds = 0d;
                    warnings.Add($"Reminder.{name}.CooldownSeconds: invalid value '{value}', treating as 0 and migrating to Once.");
                }
            }
        }

        private static bool TryParseInvariantDouble(string value, out double result)
        {
            return double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out result)
                && NumericSafety.IsFinite(result);
        }

        private static string RuleNameOf(string key, string suffix)
        {
            return key.Substring(ReminderPrefix.Length, key.Length - ReminderPrefix.Length - suffix.Length).Trim();
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
            public bool HasRepeatModeField;
            public bool HasPeriodField;
            public bool HasSendsField;
            public ReminderRepeatMode RepeatMode = ReminderRepeatMode.Once;
            public double PeriodSeconds = ReminderRule.DefaultPeriodSeconds;
            public int SendsPerPeriod = ReminderRule.DefaultSendsPerPeriod;
            public double? LegacyCooldownSeconds;
        }
    }
}
