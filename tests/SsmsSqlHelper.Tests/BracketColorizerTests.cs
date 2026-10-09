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
    }
}
