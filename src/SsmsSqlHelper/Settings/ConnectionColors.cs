using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.Serialization;
using System.Text;

namespace SsmsSqlHelper.Settings
{
    /// <summary>"If the server or database name contains <see cref="Match"/>, paint the connection banner <see cref="Color"/>."</summary>
    [DataContract]
    internal sealed class ConnectionColorRule
    {
        [DataMember(Name = "match")]
        public string Match { get; set; }

        /// <summary>A WPF colour: <c>#RRGGBB</c>, <c>#AARRGGBB</c> or a name such as <c>Red</c>.</summary>
        [DataMember(Name = "color")]
        public string Color { get; set; }
    }

    /// <summary>The rules behind the connection banner: matching a connection to a colour, and the one-rule-per-line text the settings window edits.</summary>
    internal static class ConnectionColors
    {
        /// <summary>What a fresh install starts with: anything that looks like production stands out.</summary>
        public static List<ConnectionColorRule> Defaults() => new List<ConnectionColorRule>
        {
            new ConnectionColorRule { Match = "*prod*", Color = "#FF6363" },
        };

        // Keep the previous fallback for a settings.json written before connectionColors existed.
        public static List<ConnectionColorRule> LegacyDefaults() => new List<ConnectionColorRule>
        {
            new ConnectionColorRule { Match = "prod", Color = "#B71C1C" },
        };

        /// <summary>The colour of the first rule that matches the connection, or null for the neutral banner.</summary>
        public static string Resolve(IEnumerable<ConnectionColorRule> rules, string server, string database)
        {
            if (rules == null)
                return null;

            return rules.FirstOrDefault(r => r != null && Matches(r.Match, server, database))?.Color;
        }

        /// <summary>
        /// Plain text matches if the server or database name contains it (any case). With <c>*</c> or <c>?</c> it is a wildcard
        /// pattern that must match the whole server name, the whole database name, or <c>server/database</c>.
        /// </summary>
        public static bool Matches(string pattern, string server, string database)
        {
            pattern = pattern?.Trim();
            if (string.IsNullOrEmpty(pattern))
                return false;

            server = server ?? "";
            database = database ?? "";

            if (pattern.IndexOfAny(new[] { '*', '?' }) < 0)
                return Contains(server, pattern) || Contains(database, pattern);

            return Wildcard(pattern, server) || Wildcard(pattern, database) || Wildcard(pattern, server + "/" + database);
        }

        /// <summary>One rule per line, <c>match = color</c>. Blank lines and lines starting with <c>//</c> are skipped.</summary>
        /// <param name="isValidColor">Whether a colour text can be used; null accepts anything.</param>
        public static List<ConnectionColorRule> Parse(string text, Func<string, bool> isValidColor, out List<string> problems)
        {
            var rules = new List<ConnectionColorRule>();
            problems = new List<string>();

            var lines = (text ?? "").Replace("\r\n", "\n").Replace('\r', '\n').Split('\n');
            for (var i = 0; i < lines.Length; i++)
            {
                var line = lines[i].Trim();
                if (line.Length == 0 || line.StartsWith("//", StringComparison.Ordinal))
                    continue;

                var eq = line.LastIndexOf('=');
                if (eq < 0)
                {
                    problems.Add($"Line {i + 1}: write it as  text = color  (for example  *prod* = #FF6363).");
                    continue;
                }

                var match = line.Substring(0, eq).Trim();
                var color = line.Substring(eq + 1).Trim();
                if (match.Length == 0)
                    problems.Add($"Line {i + 1}: nothing to look for before the = sign.");
                else if (color.Length == 0 || (isValidColor != null && !isValidColor(color)))
                    problems.Add($"Line {i + 1}: '{color}' is not a colour (use #RRGGBB or a name such as Red).");
                else
                    rules.Add(new ConnectionColorRule { Match = match, Color = color });
            }
            return rules;
        }

        public static string Format(IEnumerable<ConnectionColorRule> rules) =>
            string.Join(Environment.NewLine, (rules ?? Enumerable.Empty<ConnectionColorRule>()).Select(r => r.Match + " = " + r.Color));

        /// <summary>The rules as the JSON array stored in settings.json.</summary>
        public static string ToJson(IEnumerable<ConnectionColorRule> rules, string indent)
        {
            var list = (rules ?? Enumerable.Empty<ConnectionColorRule>()).Where(r => r != null).ToList();
            if (list.Count == 0)
                return "[]";

            var nl = Environment.NewLine;
            var sb = new StringBuilder("[").Append(nl);
            for (var i = 0; i < list.Count; i++)
            {
                sb.Append(indent).Append("  { \"match\": ").Append(JsonText.Quote(list[i].Match ?? ""))
                  .Append(", \"color\": ").Append(JsonText.Quote(list[i].Color ?? "")).Append(" }")
                  .Append(i < list.Count - 1 ? "," : "").Append(nl);
            }
            return sb.Append(indent).Append("]").ToString();
        }

        private static bool Contains(string text, string part) => text.IndexOf(part, StringComparison.OrdinalIgnoreCase) >= 0;

        // Case-insensitive match of the whole text; * is any run of characters, ? any single one
        private static bool Wildcard(string pattern, string text)
        {
            int p = 0, t = 0, star = -1, mark = 0;
            while (t < text.Length)
            {
                if (p < pattern.Length && (pattern[p] == '?' || char.ToUpperInvariant(pattern[p]) == char.ToUpperInvariant(text[t])))
                {
                    p++;
                    t++;
                }
                else if (p < pattern.Length && pattern[p] == '*')
                {
                    star = p++;
                    mark = t;
                }
                else if (star >= 0)
                {
                    p = star + 1;
                    t = ++mark;
                }
                else
                {
                    return false;
                }
            }
            while (p < pattern.Length && pattern[p] == '*')
                p++;
            return p == pattern.Length;
        }
    }
}
