using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace SsmsSqlHelper.Snippets
{
    /// <summary>One place the caret can be sent to with Tab: a <c>$1</c> / <c>${1:default}</c> in the body.</summary>
    internal sealed class SnippetStop
    {
        public SnippetStop(int index, int start, int length)
        {
            Index = index;
            Start = start;
            Length = length;
        }

        /// <summary>The number in the body; stops with the same number mirror each other.</summary>
        public int Index { get; }

        /// <summary>Offset in the expanded text.</summary>
        public int Start { get; }
        public int Length { get; }
    }

    /// <summary>A snippet body turned into text, with the positions the editor needs afterwards.</summary>
    internal sealed class RenderedSnippet
    {
        public RenderedSnippet(string text, IReadOnlyList<SnippetStop> stops, int finalOffset)
        {
            Text = text;
            Stops = stops;
            FinalOffset = finalOffset;
        }

        public string Text { get; }

        /// <summary>In the order they are visited: ascending number, equal numbers in text order.</summary>
        public IReadOnlyList<SnippetStop> Stops { get; }

        /// <summary>Where the caret ends up after the last stop: at <c>$CURSOR$</c> / <c>$0</c>, or the end of the text.</summary>
        public int FinalOffset { get; }

        /// <summary>Where the caret goes right after expansion: the first stop, else the final position.</summary>
        public int InitialCaretOffset => Stops.Count > 0 ? Stops[0].Start : FinalOffset;
    }

    /// <summary>
    /// The snippet body language. <c>$CURSOR$</c> or <c>$0</c>: final caret position. <c>$1</c>, <c>${1:text}</c>: tab stops
    /// (same number = mirrors). <c>$DATE$</c>, <c>$USER$</c>, <c>$SELECTED$</c>, ...: variables. <c>$$</c>: a literal dollar sign.
    /// No Visual Studio dependencies.
    /// </summary>
    internal static class SnippetTemplate
    {
        public const string CursorMarker = "$CURSOR$";

        public const string Selected = "SELECTED";

        /// <summary>Variable names a body may use; anything else between dollar signs stays as written.</summary>
        public static readonly IReadOnlyList<string> VariableNames = new[]
        {
            Selected, "DATE", "TIME", "DATETIME", "USER", "MACHINE", "SERVER", "DATABASE", "CLIPBOARD",
        };

        private enum PartKind { Text, Stop, Final, Variable }

        private sealed class Part
        {
            public PartKind Kind;
            public string Text;     // literal text, default text of a stop, or variable name
            public int Index;       // stop number
        }

        /// <summary>
        /// Expands <paramref name="body"/>. Every line after the first gets <paramref name="lineIndent"/> and the document's
        /// <paramref name="newLine"/>; empty lines stay empty. <paramref name="variable"/> supplies variable values (null = empty).
        /// </summary>
        public static RenderedSnippet Render(string body, string lineIndent, string newLine, Func<string, string> variable = null)
        {
            var sb = new StringBuilder();
            var stops = new List<SnippetStop>();
            var final = -1;
            var pendingIndent = false;

            void AppendText(string text)
            {
                var lines = text.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n');
                for (var i = 0; i < lines.Length; i++)
                {
                    if (i > 0)
                    {
                        sb.Append(newLine);
                        pendingIndent = true;
                    }
                    if (lines[i].Length == 0)
                        continue;
                    if (pendingIndent)
                    {
                        sb.Append(lineIndent);
                        pendingIndent = false;
                    }
                    sb.Append(lines[i]);
                }
            }

            // A position on a fresh line is after its indentation, even if nothing follows on that line
            void FlushIndent()
            {
                if (pendingIndent)
                {
                    sb.Append(lineIndent);
                    pendingIndent = false;
                }
            }

            foreach (var part in Parse(body ?? ""))
            {
                switch (part.Kind)
                {
                    case PartKind.Text:
                        AppendText(part.Text);
                        break;

                    case PartKind.Final:
                        FlushIndent();
                        if (final < 0)
                            final = sb.Length;
                        break;

                    case PartKind.Stop:
                    {
                        FlushIndent();
                        var start = sb.Length;
                        AppendText(part.Text);
                        stops.Add(new SnippetStop(part.Index, start, sb.Length - start));
                        break;
                    }

                    case PartKind.Variable:
                    {
                        var value = variable?.Invoke(part.Text) ?? "";
                        if (part.Text == Selected)
                        {
                            // Selected lines already carry their own indentation. Only a first line that was selected from
                            // the middle of the line (so it lost its indentation) needs the line's indent put back.
                            var lines = value.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n');
                            if (value.Length > 0 && pendingIndent)
                            {
                                if (!value.StartsWith(lineIndent, StringComparison.Ordinal))
                                    sb.Append(lineIndent);
                                pendingIndent = false;
                            }
                            sb.Append(string.Join(newLine, lines));
                        }
                        else
                        {
                            AppendText(value);
                        }
                        break;
                    }
                }
            }

            var ordered = stops
                .Select((s, i) => (Stop: s, Order: i))
                .OrderBy(x => x.Stop.Index == 0 ? int.MaxValue : x.Stop.Index)
                .ThenBy(x => x.Order)
                .Select(x => x.Stop)
                .ToList();
            return new RenderedSnippet(sb.ToString(), ordered, final >= 0 ? final : sb.Length);
        }

        /// <summary>Counts the places that mark the final caret position, to warn about more than one.</summary>
        public static int CountFinalMarkers(string body) => Parse(body ?? "").Count(p => p.Kind == PartKind.Final);

        private static List<Part> Parse(string body)
        {
            var parts = new List<Part>();
            var text = new StringBuilder();

            void FlushText()
            {
                if (text.Length > 0)
                {
                    parts.Add(new Part { Kind = PartKind.Text, Text = text.ToString() });
                    text.Clear();
                }
            }

            var i = 0;
            while (i < body.Length)
            {
                var c = body[i];
                if (c != '$')
                {
                    text.Append(c);
                    i++;
                    continue;
                }

                // $$ -> $
                if (i + 1 < body.Length && body[i + 1] == '$')
                {
                    text.Append('$');
                    i += 2;
                    continue;
                }

                // ${1:default}
                if (i + 1 < body.Length && body[i + 1] == '{')
                {
                    var j = i + 2;
                    while (j < body.Length && char.IsDigit(body[j])) j++;
                    var close = body.IndexOf('}', j);
                    if (j > i + 2 && close >= 0 && (body[j] == ':' || body[j] == '}'))
                    {
                        FlushText();
                        var number = int.Parse(body.Substring(i + 2, j - (i + 2)));
                        var def = body[j] == ':' ? body.Substring(j + 1, close - j - 1) : "";
                        parts.Add(number == 0 && def.Length == 0
                            ? new Part { Kind = PartKind.Final }
                            : new Part { Kind = PartKind.Stop, Index = number, Text = def });
                        i = close + 1;
                        continue;
                    }
                }

                // $1 / $0
                if (i + 1 < body.Length && char.IsDigit(body[i + 1]))
                {
                    var j = i + 1;
                    while (j < body.Length && char.IsDigit(body[j])) j++;
                    FlushText();
                    var number = int.Parse(body.Substring(i + 1, j - (i + 1)));
                    parts.Add(number == 0 ? new Part { Kind = PartKind.Final } : new Part { Kind = PartKind.Stop, Index = number, Text = "" });
                    i = j;
                    continue;
                }

                // $CURSOR$ / $NAME$
                var end = body.IndexOf('$', i + 1);
                if (end > i + 1)
                {
                    var name = body.Substring(i + 1, end - i - 1);
                    if (name == "CURSOR")
                    {
                        FlushText();
                        parts.Add(new Part { Kind = PartKind.Final });
                        i = end + 1;
                        continue;
                    }
                    if (VariableNames.Contains(name))
                    {
                        FlushText();
                        parts.Add(new Part { Kind = PartKind.Variable, Text = name });
                        i = end + 1;
                        continue;
                    }
                }

                // Not ours (money literal, $action, ...): keep as written
                text.Append('$');
                i++;
            }

            FlushText();
            return parts;
        }
    }
}
