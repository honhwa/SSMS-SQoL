using System.Collections.Generic;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using SsmsSqlHelper.Snippets;

namespace SsmsSqlHelper.Tests
{
    [TestClass]
    public class SnippetExpanderTests
    {
        private static readonly Dictionary<string, Snippet> Snippets = new Dictionary<string, Snippet>(System.StringComparer.OrdinalIgnoreCase)
        {
            ["ssf"] = new Snippet { Shortcut = "ssf", Body = "SELECT * FROM $CURSOR$" },
            ["uu"] = new Snippet { Shortcut = "uu", Body = "UPDATE $CURSOR$\nSET \nWHERE " },
            ["ob"] = new Snippet { Shortcut = "ob", Body = "ORDER BY" },
            ["bt"] = new Snippet { Shortcut = "bt", Body = "BEGIN TRAN\n\n$CURSOR$" },
        };

        private static SnippetExpansion Expand(string beforeCaret, string indent = "") =>
            SnippetExpander.TryExpand(beforeCaret, indent, "\r\n", s => Snippets.TryGetValue(s, out var x) ? x : null);

        [TestMethod]
        public void ExpandsShortcutAtLineStart()
        {
            var e = Expand("ssf");
            Assert.AreEqual(0, e.ReplaceStart);
            Assert.AreEqual(3, e.ReplaceLength);
            Assert.AreEqual("SELECT * FROM ", e.Text);
            Assert.AreEqual(14, e.CaretOffset);
        }

        [TestMethod]
        public void ShortcutIsCaseInsensitive() => Assert.IsNotNull(Expand("SSF"));

        [TestMethod]
        public void ExpandsAfterWhitespaceAndParen()
        {
            Assert.AreEqual(4, Expand("    ssf", "    ").ReplaceStart);
            Assert.AreEqual(9, Expand("WHERE x (ssf").ReplaceStart);
        }

        [TestMethod]
        public void CaretGoesToEndWithoutMarker()
        {
            var e = Expand("ob");
            Assert.AreEqual(e.Text.Length, e.CaretOffset);
        }

        [TestMethod]
        public void MultiLineBodyGetsIndentAndDocumentNewLine()
        {
            var e = Expand("  uu", "  ");
            Assert.AreEqual("UPDATE \r\n  SET \r\n  WHERE ", e.Text);
            Assert.AreEqual(7, e.CaretOffset);
        }

        [TestMethod]
        public void EmptyBodyLinesAreNotIndented()
        {
            var e = Expand("  bt", "  ");
            Assert.AreEqual("BEGIN TRAN\r\n\r\n  ", e.Text);
            Assert.AreEqual(e.Text.Length, e.CaretOffset);
        }

        [DataTestMethod]
        [DataRow("")]                 // nothing typed
        [DataRow("SELECT ")]          // caret after whitespace
        [DataRow("xyz")]              // unknown shortcut
        [DataRow("dbo.ssf")]          // part of a qualified name
        [DataRow("@ssf")]             // variable
        [DataRow("#ssf")]             // temp table
        [DataRow("[ssf")]             // bracketed identifier
        [DataRow("myssf")]            // longer word that merely ends with the shortcut
        [DataRow("SELECT 'ssf")]      // inside a string literal
        [DataRow("-- ssf")]           // inside a line comment
        public void DoesNotExpand(string beforeCaret) => Assert.IsNull(Expand(beforeCaret));

        [TestMethod]
        public void ExpandsAfterClosedString() => Assert.IsNotNull(Expand("SELECT 'a'; ssf"));
    }
}
