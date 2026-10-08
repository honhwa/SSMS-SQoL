using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.Serialization.Json;
using System.Text;

namespace SsmsSqlHelper.Snippets
{
    internal sealed class SnippetIssue
    {
        public SnippetIssue(int index, string message, bool isError)
        {
            Index = index;
            Message = message;
            IsError = isError;
        }

        /// <summary>Position of the snippet in the list.</summary>
        public int Index { get; }
        public string Message { get; }
        /// <summary>Errors block saving; warnings are only shown.</summary>
        public bool IsError { get; }
    }

    /// <summary>Reads, writes and checks the snippets.json format. No Visual Studio dependencies.</summary>
    internal static class SnippetFileFormat
    {
        /// <summary>Parses the file, keeping order and duplicates. Throws <see cref="System.Runtime.Serialization.SerializationException"/> on invalid JSON.</summary>
        public static List<Snippet> Parse(string json)
        {
            var serializer = new DataContractJsonSerializer(typeof(SnippetFile));
            using (var stream = new MemoryStream(Encoding.UTF8.GetBytes(json)))
            {
                var file = (SnippetFile)serializer.ReadObject(stream);
                return (file?.Snippets ?? new List<Snippet>())
                    .Where(s => s != null)
                    .Select(s => new Snippet { Shortcut = s.Shortcut?.Trim(), Description = s.Description, Body = s.Body })
                    .ToList();
            }
        }

        /// <summary>One snippet per line, so the file stays easy to read and diff by hand.</summary>
        public static string Write(IEnumerable<Snippet> snippets)
        {
            var nl = Environment.NewLine;
            var sb = new StringBuilder();
            sb.Append("{").Append(nl).Append("  \"snippets\": [").Append(nl);

            var list = snippets.ToList();
            for (var i = 0; i < list.Count; i++)
            {
                var s = list[i];
                sb.Append("    { \"shortcut\": ").Append(Quote(s.Shortcut ?? ""))
                  .Append(", \"description\": ").Append(Quote(s.Description ?? ""))
                  .Append(", \"body\": ").Append(Quote((s.Body ?? "").Replace("\r\n", "\n").Replace('\r', '\n')))
                  .Append(" }").Append(i < list.Count - 1 ? "," : "").Append(nl);
            }

            sb.Append("  ]").Append(nl).Append("}").Append(nl);
            return sb.ToString();
        }

        public static List<SnippetIssue> Validate(IReadOnlyList<Snippet> snippets)
        {
            var issues = new List<SnippetIssue>();
            var firstWithShortcut = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

            for (var i = 0; i < snippets.Count; i++)
            {
                var s = snippets[i];
                var shortcut = s.Shortcut?.Trim() ?? "";

                if (shortcut.Length == 0)
                    issues.Add(new SnippetIssue(i, "Shortcut is required.", true));
                else if (!shortcut.All(c => char.IsLetterOrDigit(c) || c == '_'))
                    issues.Add(new SnippetIssue(i, "Shortcut can only contain letters, digits and underscores (it is matched as a single word).", true));
                else if (firstWithShortcut.TryGetValue(shortcut, out var first))
                    issues.Add(new SnippetIssue(i, $"Shortcut '{shortcut}' is already used by snippet {first + 1}.", true));
                else
                    firstWithShortcut[shortcut] = i;

                if (string.IsNullOrWhiteSpace(s.Body))
                    issues.Add(new SnippetIssue(i, "Body is required.", true));
                else if (SnippetTemplate.CountFinalMarkers(s.Body) > 1)
                    issues.Add(new SnippetIssue(i, $"Only the first {SnippetExpander.CursorMarker} / $0 is used as the final caret position.", false));
            }
            return issues;
        }

        private static int CountOccurrences(string text, string value)
        {
            var count = 0;
            for (var i = text.IndexOf(value, StringComparison.Ordinal); i >= 0; i = text.IndexOf(value, i + value.Length, StringComparison.Ordinal))
                count++;
            return count;
        }

        private static string Quote(string value) => JsonText.Quote(value);
    }
}
