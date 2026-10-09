using Microsoft.VisualStudio.TestTools.UnitTesting;
using SsmsSqlHelper.Parsing;

namespace SsmsSqlHelper.Tests
{
    [TestClass]
    public class SqlFunctionCallTests
    {
        private static SqlFunctionCall Active(string marked)
        {
            var caret = marked.IndexOf('|');
            var sql = marked.Remove(caret, 1);
            Assert.IsTrue(SqlFunctionCallParser.TryFindActive(sql, caret, out var call), marked);
            return call;
        }

        [TestMethod]
        public void CastAsMovesToTypeArgument()
        {
            Assert.AreEqual("CAST", Active("SELECT CAST(|x AS int)").Signature.Name);
            Assert.AreEqual(0, Active("SELECT CAST(x| AS int)").ArgumentIndex);
            Assert.AreEqual(1, Active("SELECT CAST(x AS |int)").ArgumentIndex);
        }

        [TestMethod]
        public void NestedCallsUseInnermostFunction()
        {
            var inner = Active("SELECT ISNULL(a, IIF(a > 0, |1, 0))");
            Assert.AreEqual("IIF", inner.Signature.Name);
            Assert.AreEqual(1, inner.ArgumentIndex);
            var outer = Active("SELECT ISNULL(a, IIF(a > 0, 1, 0), |2)");
            Assert.AreEqual("ISNULL", outer.Signature.Name);
        }

        [TestMethod]
        public void CommasInsideNestedCallDoNotAdvanceOuterArgument()
        {
            Assert.AreEqual(1, Active("SELECT ISNULL(a, COALESCE(b, c)|)").ArgumentIndex);
        }

        [TestMethod]
        public void NoSignatureInsideStringOrComment()
        {
            const string stringSql = "SELECT CAST('hello(|' AS varchar(10))";
            Assert.IsFalse(SqlFunctionCallParser.TryFindActive(stringSql.Replace("|", ""), stringSql.IndexOf('|'), out _));
            const string commentSql = "SELECT ISNULL(a, /* , ( | */ b)";
            Assert.IsFalse(SqlFunctionCallParser.TryFindActive(commentSql.Replace("|", ""), commentSql.IndexOf('|'), out _));
        }
    }
}
