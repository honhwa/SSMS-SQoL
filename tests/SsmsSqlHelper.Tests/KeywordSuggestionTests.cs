using System.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using SsmsSqlHelper.Parsing;

namespace SsmsSqlHelper.Tests
{
    [TestClass]
    public class KeywordSuggestionTests
    {
        /// <summary>The words offered at the caret ('|' in the text), or null when no keyword belongs there.</summary>
        private static string[] Offered(string text)
        {
            var caret = text.IndexOf('|');
            text = text.Remove(caret, 1);
            return SqlContext.TryGetKeywordContext(text, caret, out var context) ? context.Suggestions.Select(s => s.Text).ToArray() : null;
        }

        private static void AssertStartsWith(string text, params string[] expected)
        {
            var offered = Offered(text);
            Assert.IsNotNull(offered, text);
            CollectionAssert.AreEqual(expected, offered.Take(expected.Length).ToArray(), text);
        }

        private static void AssertContains(string text, params string[] expected)
        {
            var offered = Offered(text);
            Assert.IsNotNull(offered, text);
            foreach (var e in expected)
                CollectionAssert.Contains(offered, e, text);
        }

        private static void AssertDoesNotContain(string text, params string[] unexpected)
        {
            var offered = Offered(text) ?? new string[0];
            foreach (var u in unexpected)
                CollectionAssert.DoesNotContain(offered, u, text);
        }

        // ---- after a table ----

        [DataTestMethod]
        [DataRow("SELECT * FROM Budgets |")]
        [DataRow("SELECT * FROM dbo.Budgets b |")]
        [DataRow("SELECT * FROM Budgets AS b |")]
        [DataRow("SELECT * FROM Budgets b WITH (NOLOCK) |")]
        [DataRow("SELECT * FROM a, b |")]
        [DataRow("select * from budgets b |")]
        public void AfterATableTheClausesThatFollow(string text)
        {
            AssertStartsWith(text, "WHERE", "INNER JOIN", "LEFT JOIN", "RIGHT JOIN", "FULL JOIN", "CROSS JOIN");
            AssertContains(text, "GROUP BY", "ORDER BY", "UNION", "UNION ALL");
        }

        [TestMethod]
        public void NoNoLockHintWhenOneIsAlreadyThere() =>
            Assert.IsNotNull(Offered("SELECT * FROM Budgets b WITH (NOLOCK) |"));

        [DataTestMethod]
        [DataRow("SELECT * FROM |")]                     // a table name goes here
        [DataRow("SELECT * FROM Budgets|")]              // still typing the table name
        [DataRow("SELECT * FROM Budgets b JOIN |")]
        [DataRow("SELECT * FROM a, |")]
        [DataRow("SELECT * FROM Budgets AS |")]
        public void NoKeywordsWhereATableNameIsExpected(string text) => Assert.IsNull(Offered(text), text);

        [TestMethod]
        public void AfterAJoinedTableComesON()
        {
            AssertStartsWith("SELECT * FROM a JOIN b |", "ON");
            AssertStartsWith("SELECT * FROM a INNER JOIN b bb |", "ON");
            AssertStartsWith("SELECT * FROM a LEFT OUTER JOIN b |", "ON");
        }

        [TestMethod]
        public void CrossJoinAndApplyHaveNoON()
        {
            AssertStartsWith("SELECT * FROM a CROSS JOIN b |", "WHERE");
            AssertStartsWith("SELECT * FROM a CROSS APPLY f(a.x) t |", "WHERE");
            AssertDoesNotContain("SELECT * FROM a CROSS JOIN b |", "ON");
        }

        [TestMethod]
        public void AfterAnONConditionMoreConditionsJoinsAndClauses()
        {
            AssertStartsWith("SELECT * FROM a JOIN b ON a.id = b.id |", "AND", "OR", "INNER JOIN", "LEFT JOIN");
            AssertContains("SELECT * FROM a JOIN b ON a.id = b.id |", "WHERE", "GROUP BY", "ORDER BY");
            AssertContains("SELECT * FROM a JOIN b ON a.id = b.id AND a.x = 1 |", "WHERE");
        }

        [TestMethod]
        public void AfterAModifierOnlyJoinFollows()
        {
            AssertStartsWith("SELECT * FROM a LEFT |", "JOIN", "OUTER JOIN");
            AssertStartsWith("SELECT * FROM a INNER |", "JOIN");
            AssertStartsWith("SELECT * FROM a CROSS |", "JOIN", "APPLY");
            AssertStartsWith("SELECT * FROM a b OUTER |", "APPLY", "JOIN");
        }

        // ---- conditions ----

        [TestMethod]
        public void WhereStartsWithNotOrExists()
        {
            AssertStartsWith("SELECT * FROM a WHERE |", "NOT", "EXISTS");
            AssertStartsWith("SELECT * FROM a WHERE x = 1 AND |", "NOT", "EXISTS");
            AssertStartsWith("SELECT * FROM a WHERE x = 1 OR |", "NOT", "EXISTS");
            AssertStartsWith("SELECT * FROM a WHERE NOT |", "EXISTS");
        }

        [TestMethod]
        public void AfterAValueTheComparisonOperators()
        {
            AssertStartsWith("SELECT * FROM a WHERE Name |", "=", "<>", ">", "<", ">=", "<=", "LIKE");
            AssertContains("SELECT * FROM a WHERE Name |", "IN", "IS NULL", "IS NOT NULL", "BETWEEN", "NOT LIKE");
            AssertContains("SELECT * FROM a WHERE x = 1 AND Name |", "LIKE");
            AssertContains("SELECT * FROM a WHERE LEN(Name) |", "=");
            AssertContains("SELECT * FROM a WHERE a.Name |", "=");
        }

        [TestMethod]
        public void AfterAFinishedComparisonTheNextClause()
        {
            AssertStartsWith("SELECT * FROM a WHERE Name = 'x' |", "AND", "OR", "GROUP BY", "ORDER BY");
            AssertStartsWith("SELECT * FROM a WHERE Name LIKE 'x%' |", "AND", "OR");
            AssertStartsWith("SELECT * FROM a WHERE Id IN (1, 2) |", "AND", "OR");
            AssertStartsWith("SELECT * FROM a WHERE EXISTS (SELECT 1) |", "AND", "OR");
            AssertStartsWith("SELECT * FROM a WHERE Name IS NULL |", "AND", "OR");
            AssertStartsWith("SELECT * FROM a WHERE (x = 1) |", "AND", "OR");
            AssertStartsWith("SELECT * FROM a WHERE x = 1 AND y > 2 |", "AND", "OR");
            AssertStartsWith("SELECT * FROM a WHERE x = @p |", "AND", "OR");
            AssertStartsWith("SELECT * FROM a WHERE x = b.y + 1 |", "AND", "OR");
        }

        [TestMethod]
        public void HalfWrittenComparisonsOfferNothingOrTheirContinuation()
        {
            Assert.IsNull(Offered("SELECT * FROM a WHERE Name = |"));
            Assert.IsNull(Offered("SELECT * FROM a WHERE Name LIKE |"));
            Assert.IsNull(Offered("SELECT * FROM a WHERE Name AND |".Replace("Name AND", "x =")));
            AssertStartsWith("SELECT * FROM a WHERE Name IS |", "NULL", "NOT NULL");
            AssertStartsWith("SELECT * FROM a WHERE Name IS NOT |", "NULL");
            AssertStartsWith("SELECT * FROM a WHERE Name NOT |", "LIKE", "IN", "BETWEEN");
        }

        [TestMethod]
        public void BetweenKeepsItsOwnAnd()
        {
            AssertStartsWith("SELECT * FROM a WHERE x BETWEEN 1 |", "AND");
            Assert.IsNull(Offered("SELECT * FROM a WHERE x BETWEEN 1 AND |"));
            AssertStartsWith("SELECT * FROM a WHERE x BETWEEN 1 AND 5 |", "AND", "OR", "GROUP BY");
            AssertStartsWith("SELECT * FROM a WHERE x BETWEEN 1 AND 5 AND |", "NOT", "EXISTS");
        }

        [TestMethod]
        public void HavingHasNoGroupBy()
        {
            AssertStartsWith("SELECT a, COUNT(*) FROM t GROUP BY a HAVING COUNT(*) > 1 |", "AND", "OR", "ORDER BY");
            AssertDoesNotContain("SELECT a FROM t GROUP BY a HAVING COUNT(*) > 1 |", "GROUP BY", "WHERE");
            AssertStartsWith("SELECT a FROM t GROUP BY a HAVING |", "NOT", "EXISTS");
        }

        // ---- group, order, paging ----

        [TestMethod]
        public void GroupByAndOrderBy()
        {
            AssertStartsWith("SELECT a FROM t GROUP |", "BY");
            AssertStartsWith("SELECT a FROM t ORDER |", "BY");
            Assert.IsNull(Offered("SELECT a FROM t GROUP BY |"));
            Assert.IsNull(Offered("SELECT a FROM t ORDER BY a, |"));
            AssertStartsWith("SELECT a FROM t GROUP BY a |", "HAVING", "ORDER BY");
            AssertStartsWith("SELECT a FROM t GROUP BY a, b |", "HAVING", "ORDER BY");
            AssertStartsWith("SELECT a FROM t ORDER BY a |", "ASC", "DESC", "OFFSET");
            AssertStartsWith("SELECT a FROM t ORDER BY a DESC |", "OFFSET");
            AssertStartsWith("SELECT a FROM t ORDER BY a ASC, b |", "ASC", "DESC");
        }

        [TestMethod]
        public void OffsetAndFetch()
        {
            AssertStartsWith("SELECT a FROM t ORDER BY a OFFSET 0 |", "ROWS");
            AssertStartsWith("SELECT a FROM t ORDER BY a OFFSET 0 ROWS |", "FETCH NEXT");
            Assert.AreEqual("FETCH NEXT 10 ROWS ONLY", SqlKeywordInsert("SELECT a FROM t ORDER BY a OFFSET 0 ROWS |", "FETCH NEXT"));
        }

        private static string SqlKeywordInsert(string text, string keyword)
        {
            var caret = text.IndexOf('|');
            Assert.IsTrue(SqlContext.TryGetKeywordContext(text.Remove(caret, 1), caret, out var c));
            return c.Suggestions.Single(s => s.Text == keyword).InsertText;
        }

        // ---- the select list ----

        [TestMethod]
        public void SelectListStartsWithModifiers()
        {
            AssertStartsWith("SELECT |", "DISTINCT", "TOP", "ALL", "CASE");
            AssertStartsWith("SELECT DISTINCT |", "TOP", "CASE");
            Assert.IsNull(Offered("SELECT TOP 10 |"));
            Assert.IsNull(Offered("SELECT TOP (10) PERCENT |"));
        }

        [TestMethod]
        public void AfterASelectedItemFromOrAs()
        {
            AssertStartsWith("SELECT a |", "FROM", "AS");
            AssertStartsWith("SELECT a, b |", "FROM", "AS");
            AssertStartsWith("SELECT * |", "FROM");
            AssertStartsWith("SELECT a, * |", "FROM");
            AssertStartsWith("SELECT COUNT(*) |", "FROM", "AS");
            AssertStartsWith("SELECT t.Name |", "FROM", "AS");
            AssertStartsWith("SELECT 1 |", "FROM", "AS");
            Assert.IsNull(Offered("SELECT a AS |"));
            Assert.IsNull(Offered("SELECT a, |"));
            Assert.IsNull(Offered("SELECT a + |"));
        }

        [TestMethod]
        public void SelectIntoThenFrom() =>
            AssertStartsWith("SELECT a INTO #t |", "FROM");

        // ---- CASE ----

        [TestMethod]
        public void CaseWalksThroughWhenThenElseEnd()
        {
            AssertStartsWith("SELECT CASE |", "WHEN");
            AssertStartsWith("SELECT CASE x |", "WHEN");
            AssertStartsWith("SELECT CASE WHEN |", "NOT", "EXISTS");
            AssertStartsWith("SELECT CASE WHEN a |", "=");
            AssertStartsWith("SELECT CASE WHEN a = 1 |", "THEN", "AND", "OR");
            Assert.IsNull(Offered("SELECT CASE WHEN a = 1 THEN |"));
            AssertStartsWith("SELECT CASE WHEN a = 1 THEN 2 |", "WHEN", "ELSE", "END");
            AssertStartsWith("SELECT CASE WHEN a = 1 THEN 2 ELSE 3 |", "END");
            AssertStartsWith("SELECT CASE WHEN a = 1 THEN 2 ELSE 3 END |", "FROM", "AS");
        }

        [TestMethod]
        public void NestedCaseIsTrackedSeparately()
        {
            AssertStartsWith("SELECT CASE WHEN a = 1 THEN CASE WHEN b = 2 THEN 3 |", "WHEN", "ELSE", "END");
            AssertStartsWith("SELECT CASE WHEN a = 1 THEN CASE WHEN b = 2 THEN 3 END |", "WHEN", "ELSE", "END");
            AssertStartsWith("SELECT CASE WHEN a = 1 THEN CASE WHEN b = 2 THEN 3 END ELSE 4 END |", "FROM", "AS");
        }

        // ---- UPDATE, DELETE, INSERT ----

        [TestMethod]
        public void UpdateWantsASetAfterItsTable()
        {
            AssertStartsWith("UPDATE Budgets |", "SET");
            AssertStartsWith("UPDATE dbo.Budgets b |", "SET");
            Assert.IsNull(Offered("UPDATE |"));
            Assert.IsNull(Offered("UPDATE Budgets SET |"));
            AssertStartsWith("UPDATE Budgets SET Name |", "=");
            AssertStartsWith("UPDATE Budgets SET Name = 'x' |", "WHERE", "FROM");
            AssertStartsWith("UPDATE Budgets SET Name = 'x', Code = 1 |", "WHERE", "FROM");
            Assert.IsNull(Offered("UPDATE Budgets SET Name = |"));
            AssertStartsWith("UPDATE b SET Name = 'x' FROM Budgets b |", "WHERE", "INNER JOIN");
            AssertStartsWith("UPDATE Budgets SET Name = 'x' WHERE |", "NOT", "EXISTS");
        }

        [TestMethod]
        public void DeleteWantsFromThenWhere()
        {
            AssertStartsWith("DELETE |", "FROM");
            AssertStartsWith("DELETE FROM Budgets |", "WHERE", "OUTPUT");
            AssertStartsWith("DELETE b |", "FROM");
            AssertStartsWith("DELETE b FROM Budgets b |", "WHERE", "INNER JOIN");
            AssertStartsWith("DELETE FROM Budgets WHERE Id = 1 |", "AND", "OR");
        }

        [TestMethod]
        public void InsertWantsIntoThenValues()
        {
            AssertStartsWith("INSERT |", "INTO");
            Assert.IsNull(Offered("INSERT INTO |"));
            AssertStartsWith("INSERT INTO Budgets |", "VALUES", "SELECT");
            AssertStartsWith("INSERT INTO dbo.Budgets (a, b) |", "VALUES", "SELECT", "EXEC");
            Assert.AreEqual("VALUES (", SqlKeywordInsert("INSERT INTO Budgets (a) |", "VALUES"));
            AssertStartsWith("INSERT INTO Budgets (a) SELECT a FROM t |", "WHERE", "INNER JOIN");
        }

        // ---- set operators ----

        [TestMethod]
        public void UnionIsFollowedByAllOrSelect()
        {
            AssertStartsWith("SELECT a FROM t UNION |", "ALL", "SELECT");
            AssertStartsWith("SELECT a FROM t UNION ALL |", "SELECT");
            AssertStartsWith("SELECT a FROM t EXCEPT |", "SELECT");
            AssertStartsWith("SELECT a FROM t INTERSECT |", "SELECT");
            AssertStartsWith("SELECT a FROM t UNION SELECT b FROM u |", "WHERE", "INNER JOIN");
        }

        // ---- the start of a statement ----

        [DataTestMethod]
        [DataRow("|")]
        [DataRow("   |")]
        [DataRow("SELECT 1;\r\n|")]
        [DataRow("SELECT * FROM a\r\nGO\r\n|")]
        [DataRow("SELECT * FROM a\r\nGO 2\r\n|")]
        public void AStatementStartsWithTheUsualVerbs(string text) =>
            AssertStartsWith(text, "SELECT", "INSERT INTO", "UPDATE", "DELETE FROM");

        [TestMethod]
        public void GoOnlyCountsOnALineOfItsOwn()
        {
            // "GO" inside a line is a word like any other, the SELECT is still open
            AssertStartsWith("SELECT a GO |", "FROM", "AS");
        }

        // ---- subqueries ----

        [TestMethod]
        public void ASubqueryIsAnalysedOnItsOwn()
        {
            AssertStartsWith("SELECT * FROM a WHERE x IN (SELECT y FROM b |)", "WHERE", "INNER JOIN");
            AssertStartsWith("SELECT * FROM a WHERE x IN (SELECT y FROM b WHERE |)", "NOT", "EXISTS");
            AssertStartsWith("SELECT * FROM (SELECT y FROM b |) t", "WHERE", "INNER JOIN");
            AssertStartsWith("SELECT * FROM (SELECT y FROM b) t |", "WHERE", "INNER JOIN");
        }

        // ---- where nothing is offered ----

        [DataTestMethod]
        [DataRow("SELECT COUNT(|")]                           // inside a function call
        [DataRow("SELECT * FROM a WHERE x IN (|")]
        [DataRow("SELECT * FROM a WHERE (x = 1 AND |")]
        [DataRow("SELECT a.|")]                               // after a dot
        [DataRow("SELECT * FROM a WHERE a.Na|")]              // a column being typed
        [DataRow("SELECT 'text |")]                           // inside a string
        [DataRow("-- SELECT |")]                              // inside a comment
        [DataRow("/* WHERE | */")]
        [DataRow("DECLARE @x int = |")]                       // not understood
        [DataRow("EXEC sp_who |")]
        [DataRow("SELECT * FROM #tmp|")]
        public void NothingOffered(string text) => Assert.IsNull(Offered(text), text);

        // ---- the word being typed ----

        [TestMethod]
        public void TheWordBeingTypedIsReplaced()
        {
            const string text = "SELECT * FROM Budgets b wh";
            Assert.IsTrue(SqlContext.TryGetKeywordContext(text, text.Length, out var c));
            Assert.AreEqual("wh", text.Substring(c.Start, c.End - c.Start));
            Assert.AreEqual("WHERE", c.Suggestions[0].Text);
        }

        [TestMethod]
        public void NoWordTypedMeansAnEmptySpanAtTheCaret()
        {
            const string text = "SELECT * FROM Budgets b ";
            Assert.IsTrue(SqlContext.TryGetKeywordContext(text, text.Length, out var c));
            Assert.AreEqual(text.Length, c.Start);
            Assert.AreEqual(text.Length, c.End);
        }

        [TestMethod]
        public void RightAfterABracketTheWordIsEmpty()
        {
            const string text = "SELECT * FROM a WHERE (x = 1)";
            Assert.IsTrue(SqlContext.TryGetKeywordContext(text, text.Length, out var c));
            Assert.AreEqual(c.Start, c.End);
            Assert.AreEqual("AND", c.Suggestions[0].Text);
        }

        [TestMethod]
        public void EveryOfferHasAnInsertTextThatStartsWithItsWords()
        {
            foreach (var text in new[] { "SELECT * FROM a |", "SELECT * FROM a WHERE x |", "SELECT |", "|" })
            {
                var caret = text.IndexOf('|');
                Assert.IsTrue(SqlContext.TryGetKeywordContext(text.Remove(caret, 1), caret, out var c));
                foreach (var s in c.Suggestions)
                {
                    Assert.IsTrue(s.Text.Length > 0 && s.InsertText.StartsWith(s.Text.Split(' ')[0]), s.Text);
                    Assert.IsTrue(s.Description != null, s.Text);
                }
            }
        }

        // ---- case of the suggested words ----

        [DataTestMethod]
        [DataRow("select * from budgets |", true)]
        [DataRow("select * from budgets where x = 1 |", true)]
        [DataRow("SELECT * FROM Budgets |", false)]
        [DataRow("Select * From budgets b where |", true)]       // judged by the nearest keyword
        [DataRow("SELECT * FROM budgets WHERE |", false)]
        [DataRow("|", false)]                                    // nothing to go by
        [DataRow("budgets |", false)]
        public void SuggestionsFollowTheCaseAlreadyUsed(string text, bool lowercase)
        {
            var caret = text.IndexOf('|');
            Assert.AreEqual(lowercase, SqlContext.PrefersLowercaseKeywords(text.Remove(caret, 1), caret), text);
        }

        [TestMethod]
        public void TheWordBeingTypedIsNotWhatTheCaseIsJudgedBy()
        {
            const string text = "SELECT * FROM budgets wh";
            Assert.IsFalse(SqlContext.PrefersLowercaseKeywords(text, text.Length));
        }

        // ---- statement start ----

        [TestMethod]
        public void OnlyTheStartOfAStatementIsMarkedAsSuch()
        {
            Assert.IsTrue(Context("|").IsStatementStart);
            Assert.IsTrue(Context("SELECT 1;" + NL + "|").IsStatementStart);
            Assert.IsTrue(Context("SELECT * FROM a" + NL + "GO" + NL + "|").IsStatementStart);
            Assert.IsFalse(Context("SELECT |").IsStatementStart);
            Assert.IsFalse(Context("SELECT * FROM a |").IsStatementStart);
            Assert.IsFalse(Context("UPDATE Budgets |").IsStatementStart);
        }

        private const string NL = "\r\n";

        private static KeywordContext Context(string text)
        {
            var caret = text.IndexOf('|');
            return SqlContext.TryGetKeywordContext(text.Remove(caret, 1), caret, out var c) ? c : null;
        }
    }
}
