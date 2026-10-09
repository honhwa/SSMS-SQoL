using Microsoft.VisualStudio.TestTools.UnitTesting;
using SsmsSqlHelper.Parsing;

namespace SsmsSqlHelper.Tests
{
    [TestClass]
    public class BracketColorizerTests
    {
        private static string Marked(string sql)
        {
            var chars = sql.ToCharArray();
            foreach (var span in BracketColorizer.Find(sql))
                chars[span.Start] = (char)('0' + span.Level);
            return new string(chars);
        }

        [TestMethod]
        public void NestedPairsUseMatchingLevels()
        {
            Assert.AreEqual("SELECT 0a, 1b2c2b1 FROM t0", Marked("SELECT (a, (b{c}b) FROM t)"));
        }

        [TestMethod]
        public void StringAndCommentsAreIgnored()
        {
            const string sql = "SELECT '(' /* ( */ FROM t -- )\nWHERE x = (1)";
            var spans = BracketColorizer.Find(sql);
            Assert.AreEqual(2, spans.Count);
            Assert.AreEqual(sql.IndexOf("(1)"), spans[0].Start);
            Assert.AreEqual(sql.IndexOf("(1)") + 2, spans[1].Start);
        }

        [TestMethod]
        public void BracketedIdentifierUsesCurrentDepth()
        {
            Assert.AreEqual("SELECT 01a]]b10", Marked("SELECT ([a]]b])"));
        }

        [TestMethod]
        public void UnmatchedDelimitersStayUncolored()
        {
            Assert.AreEqual(0, BracketColorizer.Find("SELECT (x").Count);
            Assert.AreEqual(0, BracketColorizer.Find("SELECT [x").Count);
            Assert.AreEqual(0, BracketColorizer.Find("SELECT [x]]").Count);
            Assert.AreEqual(0, BracketColorizer.Find("SELECT )").Count);
        }

        [TestMethod]
        public void CaretOnEitherSideOfNestedPairFindsTheSameBrackets()
        {
            const string sql = "SELECT COALESCE(ISNULL([Amount], 0), GETDATE())";
            var pairs = BracketColorizer.IndexPairs(sql);
            var open = sql.IndexOf("ISNULL(") + "ISNULL".Length;
            var close = sql.IndexOf(", 0)") + 3;

            Assert.IsTrue(BracketColorizer.TryGetPairAtCaret(pairs, open, out var onOpen));
            Assert.IsTrue(BracketColorizer.TryGetPairAtCaret(pairs, open + 1, out var afterOpen));
            Assert.IsTrue(BracketColorizer.TryGetPairAtCaret(pairs, close, out var onClose));
            Assert.IsTrue(BracketColorizer.TryGetPairAtCaret(pairs, close + 1, out var afterClose));
            Assert.AreEqual(open, onOpen.Open);
            Assert.AreEqual(close, onOpen.Close);
            Assert.AreEqual(open + 1, afterOpen.Open); // The '[' under the caret takes priority.
            Assert.AreEqual(onOpen.Open, onClose.Open);
            Assert.AreEqual(onOpen.Open, afterClose.Open);
        }

        [TestMethod]
        public void CaretIgnoresBracketsInStringsCommentsAndUnmatchedText()
        {
            const string sql = "SELECT '(' /* ) */ WHERE x = (1)";
            var pairs = BracketColorizer.IndexPairs(sql);
            Assert.IsFalse(BracketColorizer.TryGetPairAtCaret(pairs, sql.IndexOf("'('") + 1, out _));
            Assert.IsFalse(BracketColorizer.TryGetPairAtCaret(pairs, sql.IndexOf("/* )") + 3, out _));
            Assert.IsTrue(BracketColorizer.TryGetPairAtCaret(pairs, sql.IndexOf("(1)"), out _));
            Assert.IsFalse(BracketColorizer.TryGetPairAtCaret(BracketColorizer.IndexPairs("SELECT (x"), 7, out _));
        }

        [TestMethod]
        public void BracketedIdentifierMatchesOnlyItsOuterDelimiters()
        {
            const string sql = "SELECT [a]]b]";
            var pairs = BracketColorizer.IndexPairs(sql);
            Assert.IsTrue(BracketColorizer.TryGetPairAtCaret(pairs, 7, out var pair));
            Assert.AreEqual(7, pair.Open);
            Assert.AreEqual(sql.Length - 1, pair.Close);
            Assert.IsFalse(BracketColorizer.TryGetPairAtCaret(pairs, sql.IndexOf("]]"), out _));
        }
    }
}
