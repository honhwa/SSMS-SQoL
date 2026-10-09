using Microsoft.VisualStudio.TestTools.UnitTesting;
using SsmsSqlHelper.Metadata;
using SsmsSqlHelper.Parsing;

namespace SsmsSqlHelper.Tests
{
    [TestClass]
    public class DatabaseCompletionContextTests
    {
        private static void AssertSpan(string markedSql, string expected)
        {
            var caret = markedSql.IndexOf('|');
            var sql = markedSql.Remove(caret, 1);
            Assert.IsTrue(SqlContext.TryGetDatabaseNameSpan(sql, caret, out var start, out var end), markedSql);
            Assert.AreEqual(expected, sql.Substring(start, end - start), markedSql);
        }

        [TestMethod]
        public void AfterUseAnEmptyOrPartialDatabaseNameCanBeCompleted()
        {
            AssertSpan("USE |", "");
            AssertSpan("use Pro|", "Pro");
            AssertSpan("USE [Prototype|", "[Prototype");
            AssertSpan("USE [Prototype-Dev]|", "[Prototype-Dev]");
            AssertSpan("SELECT 1; USE Db|", "Db");
        }

        [DataTestMethod]
        [DataRow("USE|")]
        [DataRow("SELECT 'USE Db|'")]
        [DataRow("-- USE Db|")]
        [DataRow("SELECT * FROM dbo.Budgets|")]
        [DataRow("USE Db;|")]
        public void DatabaseNamesAreNotOfferedElsewhere(string markedSql)
        {
            var caret = markedSql.IndexOf('|');
            var sql = markedSql.Remove(caret, 1);
            Assert.IsFalse(SqlContext.TryGetDatabaseNameSpan(sql, caret, out _, out _), markedSql);
        }

        [TestMethod]
        public void DatabaseNamesWithSpacesOrReservedWordsAreQuoted()
        {
            Assert.AreEqual("[Prototype Dev]", SqlIdentifier.Quote("Prototype Dev"));
            Assert.AreEqual("[Order]", SqlIdentifier.Quote("Order"));
            Assert.AreEqual("Budgets", SqlIdentifier.Quote("Budgets"));
        }
    }
}
