using System;
using System.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using SsmsSqlHelper.Editor;
using SsmsSqlHelper.Parsing;

namespace SsmsSqlHelper.Tests
{
    [TestClass]
    public class CompletionSortOrderTests
    {
        [TestMethod]
        public void ExactObjectNamePrecedesPrefixAndSubstringMatches()
        {
            var names = new[] { "AssetWorkers", "ItemCategoryGroupWorkers", "Workers", "WorkersArchive", "WorkerSignatures" };
            var ranked = names.OrderBy(n => CompletionSortOrder.ObjectMatch(n, "workers")).ToArray();
            CollectionAssert.AreEqual(new[] { "Workers", "WorkersArchive", "WorkerSignatures", "AssetWorkers", "ItemCategoryGroupWorkers" }, ranked);
        }

        [DataTestMethod]
        [DataRow("dbo.workers", "workers")]
        [DataRow("[dbo].[Workers]", "Workers")]
        [DataRow("[dbo].[Work", "Work")]
        [DataRow("dbo.", "")]
        public void ObjectRankingUsesTheNameAfterTheSchema(string typedName, string expected)
        {
            Assert.AreEqual(expected, CompletionSortOrder.ObjectNameWord(typedName));
        }

        [TestMethod]
        public void RankingChangesAsTheUserTypesAndKeepsTiesStable()
        {
            var names = new[] { "AssetWorkers", "WorkersArchive", "Workers" };
            CollectionAssert.AreEqual(names, names.OrderBy(n => CompletionSortOrder.ObjectMatch(n, "")).ToArray());
            CollectionAssert.AreEqual(new[] { "WorkersArchive", "Workers", "AssetWorkers" },
                names.OrderBy(n => CompletionSortOrder.ObjectMatch(n, "work")).ToArray());
            CollectionAssert.AreEqual(new[] { "Workers", "WorkersArchive", "AssetWorkers" },
                names.OrderBy(n => CompletionSortOrder.ObjectMatch(n, "workers")).ToArray());
        }

        [TestMethod]
        public void EmptyWordPutsEveryColumnBeforeKeywordsAndFunctions()
        {
            var lastColumn = CompletionSortOrder.Column(3, 42);
            Assert.IsTrue(StringComparer.Ordinal.Compare(lastColumn, CompletionSortOrder.Keyword("FROM", "", 0)) < 0);
            Assert.IsTrue(StringComparer.Ordinal.Compare(lastColumn, CompletionSortOrder.Keyword("WHERE", "", 1)) < 0);
            Assert.IsTrue(StringComparer.Ordinal.Compare(lastColumn, CompletionSortOrder.Keyword("ISNULL(", "", 0)) < 0);
            Assert.IsTrue(StringComparer.Ordinal.Compare(lastColumn, CompletionSortOrder.Keyword("GETDATE()", "", 1)) < 0);
        }

        [TestMethod]
        public void MatchingFunctionMovesAboveColumnsAfterTyping()
        {
            var column = CompletionSortOrder.Column(0, 0);
            Assert.IsTrue(StringComparer.Ordinal.Compare(CompletionSortOrder.Keyword("ISNULL(", "is", 0), column) < 0);
            Assert.IsTrue(StringComparer.Ordinal.Compare(CompletionSortOrder.Keyword("FROM", "fr", 0), column) < 0);
            Assert.IsTrue(StringComparer.Ordinal.Compare(CompletionSortOrder.Keyword("CAST(", "is", 1), column) > 0);
        }

        [TestMethod]
        public void ColumnsKeepTableAndOrdinalOrder()
        {
            Assert.IsTrue(StringComparer.Ordinal.Compare(CompletionSortOrder.Column(0, 99), CompletionSortOrder.Column(1, 0)) < 0);
            Assert.IsTrue(StringComparer.Ordinal.Compare(CompletionSortOrder.Column(1, 0), CompletionSortOrder.Column(1, 1)) < 0);
        }

        [TestMethod]
        public void SelectStarBeforeFromOffersBothColumnsAndFrom()
        {
            const string sql = "SELECT * FROM dbo.Budgets b";
            var caret = sql.IndexOf(" FROM", StringComparison.Ordinal);
            Assert.IsTrue(SqlContext.TryGetColumnContext(sql, caret, out _));
            Assert.IsTrue(SqlContext.TryGetKeywordContext(sql, caret, out var keywords));
            Assert.IsTrue(keywords.Suggestions.Exists(s => s.Text == "FROM"));
            Assert.IsTrue(StringComparer.Ordinal.Compare(CompletionSortOrder.Column(0, 0),
                CompletionSortOrder.Keyword("FROM", "", 0)) < 0);
        }
    }
}
