using System;
using System.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using SsmsSqlHelper.Generation;
using SsmsSqlHelper.Metadata;
using SsmsSqlHelper.Parsing;

namespace SsmsSqlHelper.Tests
{
    [TestClass]
    public class ColumnTests
    {
        private static ColumnInfo Col(string name, string type = "int", bool pk = false, bool identity = false, bool nullable = false, int maxLength = 4) =>
            new ColumnInfo { Name = name, TypeName = type, BaseTypeName = type, IsPrimaryKey = pk, IsIdentity = identity, IsNullable = nullable, MaxLength = maxLength };

        private static DbMetadata Metadata()
        {
            var budgets = new TableInfo("dbo", "Budgets", false);
            budgets.Columns.Add(Col("Id", pk: true, identity: true));
            budgets.Columns.Add(Col("Name", "nvarchar", nullable: true, maxLength: 200));
            budgets.Columns.Add(Col("Order", "int"));   // needs brackets

            var lines = new TableInfo("dbo", "BudgetLines", false);
            lines.Columns.Add(Col("Id", pk: true, identity: true));
            lines.Columns.Add(Col("BudgetId"));
            lines.Columns.Add(Col("Qty", "decimal"));

            var fk = new ForeignKeyInfo("FK_BudgetBudgetLine", lines, budgets);
            fk.Columns.Add(("BudgetId", "Id"));
            return new DbMetadata("s", "d", new[] { budgets, lines }, DateTime.Now, TimeSpan.Zero, new[] { fk });
        }

        private static ColumnContext Context(string text)
        {
            var caret = text.IndexOf('|');
            text = text.Remove(caret, 1);
            return SqlContext.TryGetColumnContext(text, caret, out var context) ? context : null;
        }

        private static string[] Names(ColumnContext c, DbMetadata md = null) =>
            ColumnSuggester.Suggest(c, md ?? Metadata()).Select(s => s.InsertText).ToArray();

        // ---- where columns are offered ----

        [TestMethod]
        public void AfterAliasDot()
        {
            var c = Context("SELECT b.| FROM Budgets b");
            Assert.AreEqual(ColumnContextKind.Qualified, c.Kind);
            Assert.AreEqual("b", c.Qualifier);
            Assert.AreEqual(c.Start, c.End);
            Assert.AreEqual("Budgets", c.Tables.Single().Name.Text);
        }

        [TestMethod]
        public void AfterAliasDotWithPartialWord()
        {
            const string text = "SELECT b.Na FROM Budgets b";
            Assert.IsTrue(SqlContext.TryGetColumnContext(text, text.IndexOf(" FROM", StringComparison.Ordinal), out var c));
            Assert.AreEqual(ColumnContextKind.Qualified, c.Kind);
            Assert.AreEqual("Na", text.Substring(c.Start, c.End - c.Start));
        }

        [DataTestMethod]
        [DataRow("SELECT | FROM Budgets b")]
        [DataRow("SELECT DISTINCT | FROM Budgets b")]
        [DataRow("SELECT TOP 10 | FROM Budgets b")]
        [DataRow("SELECT TOP (10) PERCENT | FROM Budgets b")]
        [DataRow("SELECT Id, | FROM Budgets b")]
        [DataRow("SELECT Na| FROM Budgets b")]
        [DataRow("SELECT COUNT(| FROM Budgets b")]
        [DataRow("SELECT COUNT(|) FROM Budgets b")]
        [DataRow("SELECT ISNULL(Name, |) FROM Budgets b")]
        [DataRow("SELECT Id FROM Budgets b WHERE Id IN (|)")]
        [DataRow("SELECT Id FROM Budgets b WHERE (Id = 1 AND |)")]
        [DataRow("SELECT Id FROM Budgets b WHERE |")]
        [DataRow("SELECT Id FROM Budgets b WHERE Id = |")]
        [DataRow("SELECT Id FROM Budgets b WHERE Id > 1 AND |")]
        [DataRow("SELECT Id FROM Budgets b WHERE Id IN (|")]
        [DataRow("SELECT Id FROM Budgets b GROUP BY |")]
        [DataRow("SELECT Id FROM Budgets b ORDER BY Id, |")]
        [DataRow("SELECT Id FROM Budgets b JOIN BudgetLines bl ON bl.BudgetId = |")]
        [DataRow("SELECT CASE WHEN | THEN 1 END FROM Budgets b")]
        public void ExpressionPositions(string text)
        {
            var c = Context(text);
            Assert.IsNotNull(c, text);
            Assert.AreEqual(ColumnContextKind.Expression, c.Kind);
            Assert.IsTrue(c.Tables.Count >= 1);
        }

        [DataTestMethod]
        [DataRow("SELECT * FROM |")]                              // a table name goes here
        [DataRow("SELECT * FROM Budgets b JOIN |")]
        [DataRow("SELECT * FROM Budgets b |")]                    // alias / next clause
        [DataRow("SELECT Id AS | FROM Budgets b")]                // new name
        [DataRow("SELECT Id | FROM Budgets b")]                   // alias of the column
        [DataRow("SELECT | ")]                                    // no tables to take columns from
        [DataRow("SELECT 'a| FROM Budgets b")]                    // inside a string
        [DataRow("-- SELECT | FROM Budgets b")]                   // inside a comment
        [DataRow("INSERT INTO Budgets VALUES (|")]
        [DataRow("INSERT INTO Budgets (Name) VALUES (|")]
        [DataRow("DECLARE @x int = |")]
        [DataRow("SELECT * FROM Budgets b WHERE Id = 1 |")]
        public void NotColumnPositions(string text) => Assert.IsNull(Context(text), text);

        [TestMethod]
        public void InsertColumnList()
        {
            var c = Context("INSERT INTO dbo.Budgets (|");
            Assert.AreEqual(ColumnContextKind.InsertList, c.Kind);
            Assert.AreEqual("dbo.Budgets", c.InsertTarget.Text);

            Assert.AreEqual(ColumnContextKind.InsertList, Context("INSERT INTO Budgets (Name, |").Kind);
            Assert.AreEqual(ColumnContextKind.InsertList, Context("INSERT Budgets (Na|").Kind);
            Assert.IsNull(Context("INSERT INTO Budgets (Name) SELECT (|"), "second bracket is not the column list");
        }

        [TestMethod]
        public void UpdateUsesItsTargetTable()
        {
            Assert.AreEqual("Budgets", Context("UPDATE Budgets SET |").Tables.Single().Name.Text);
            Assert.AreEqual("Budgets", Context("UPDATE Budgets SET Name = 'x', |").Tables.Single().Name.Text);
            Assert.AreEqual("Budgets", Context("UPDATE Budgets SET Name = 'x' WHERE |").Tables.Single().Name.Text);
            Assert.AreEqual("b", Context("UPDATE Budgets AS b SET |").Tables.Single().Alias);
        }

        [TestMethod]
        public void UpdateWithFromTakesTablesFromTheFromClause()
        {
            // UPDATE b ... FROM Budgets b: "b" is the alias, so only the FROM table is listed
            var c = Context("UPDATE b SET Name = | FROM Budgets b JOIN BudgetLines bl ON bl.BudgetId = b.Id");
            CollectionAssert.AreEqual(new[] { "Budgets", "BudgetLines" }, c.Tables.Select(t => t.Name.Text).ToArray());
        }

        [TestMethod]
        public void SubqueryHasItsOwnScope()
        {
            var c = Context("SELECT * FROM Units u WHERE EXISTS (SELECT | FROM Budgets b)");
            Assert.AreEqual("Budgets", c.Tables.Single().Name.Text);
        }

        // ---- which columns ----

        [TestMethod]
        public void QualifiedByAliasOffersThatTablesColumns()
        {
            var c = Context("SELECT bl.| FROM Budgets b JOIN BudgetLines bl ON 1 = 1");
            CollectionAssert.AreEqual(new[] { "Id", "BudgetId", "Qty" }, Names(c));
        }

        [TestMethod]
        public void QuotesColumnsThatNeedIt() =>
            CollectionAssert.Contains(Names(Context("SELECT b.| FROM Budgets b")), "[Order]");

        [DataTestMethod]
        [DataRow("SELECT Budgets.| FROM Budgets")]
        [DataRow("SELECT dbo.Budgets.| FROM dbo.Budgets")]
        [DataRow("SELECT [Budgets].| FROM [dbo].[Budgets]")]
        [DataRow("SELECT budgets.| FROM Budgets")]
        public void QualifiedByTableNameWithoutAlias(string text) =>
            Assert.AreEqual(3, Names(Context(text)).Length);

        [TestMethod]
        public void AnAliasHidesTheTableName() =>
            Assert.AreEqual(0, Names(Context("SELECT Budgets.| FROM Budgets b")).Length);

        [TestMethod]
        public void UnknownQualifierOffersNothing() =>
            Assert.AreEqual(0, Names(Context("SELECT zz.| FROM Budgets b")).Length);

        [TestMethod]
        public void SingleTableWithoutAliasIsNotQualified()
        {
            var names = Names(Context("SELECT | FROM Budgets"));
            CollectionAssert.AreEqual(new[] { "Id", "Name", "[Order]" }, names);
        }

        [DataTestMethod]
        [DataRow("SELECT * FROM Budgets b WHERE |")]
        [DataRow("SELECT * FROM Budgets b WHERE Id = 1 AND |")]
        [DataRow("SELECT | FROM Budgets b")]
        [DataRow("SELECT * FROM Budgets b ORDER BY |")]
        [DataRow("SELECT * FROM Budgets AS b WHERE |")]
        public void SingleTableWithAliasUsesTheAliasEverywhere(string text)
        {
            // one table and no join, but the select list says b.Id, so WHERE and the rest say b.Id too
            var names = Names(Context(text));
            CollectionAssert.AreEqual(new[] { "b.Id", "b.Name", "b.[Order]" }, names);
        }

        [TestMethod]
        public void TheAliasIsShownInTheListToo()
        {
            var suggestions = ColumnSuggester.Suggest(Context("SELECT * FROM Budgets b WHERE |"), Metadata());
            Assert.IsTrue(suggestions.All(s => s.IsQualified && s.Qualifier == "b"));
            Assert.IsFalse(ColumnSuggester.Suggest(Context("SELECT * FROM Budgets WHERE |"), Metadata()).Any(s => s.IsQualified));
        }

        [TestMethod]
        public void UpdateWithAnAliasedFromTableUsesTheAlias()
        {
            var names = Names(Context("UPDATE b SET Name = 'x' FROM Budgets b WHERE |"));
            CollectionAssert.AreEqual(new[] { "b.Id", "b.Name", "b.[Order]" }, names);
        }

        [TestMethod]
        public void SeveralTablesAreQualifiedAndGroupedInQueryOrder()
        {
            var names = Names(Context("SELECT | FROM Budgets b JOIN BudgetLines bl ON bl.BudgetId = b.Id"));
            CollectionAssert.AreEqual(new[] { "b.Id", "b.Name", "b.[Order]", "bl.Id", "bl.BudgetId", "bl.Qty" }, names);
        }

        [TestMethod]
        public void TableWithoutAliasIsQualifiedByItsName()
        {
            var names = Names(Context("SELECT | FROM Budgets JOIN BudgetLines bl ON 1 = 1"));
            CollectionAssert.Contains(names, "Budgets.Id");
            CollectionAssert.Contains(names, "bl.Id");
        }

        [TestMethod]
        public void UnknownTablesAreSkipped()
        {
            var names = Names(Context("SELECT | FROM Budgets b JOIN #temp t ON 1 = 1"));
            // the temp table has no known columns, but it is still a second table in the query, so the alias stays
            CollectionAssert.AreEqual(new[] { "b.Id", "b.Name", "b.[Order]" }, names);
        }

        [TestMethod]
        public void InsertListSkipsGeneratedColumns()
        {
            var names = Names(Context("INSERT INTO BudgetLines (|"));
            CollectionAssert.AreEqual(new[] { "BudgetId", "Qty" }, names);
        }

        [TestMethod]
        public void DescriptionMentionsKeysAndForeignKeys()
        {
            var md = Metadata();
            var all = ColumnSuggester.Suggest(Context("SELECT bl.| FROM BudgetLines bl"), md);

            var id = ColumnSuggester.Describe(all.Single(s => s.Column.Name == "Id"), md);
            StringAssert.Contains(id, "BudgetLines.Id");
            StringAssert.Contains(id, "primary key");
            StringAssert.Contains(id, "identity");

            var budgetId = ColumnSuggester.Describe(all.Single(s => s.Column.Name == "BudgetId"), md);
            StringAssert.Contains(budgetId, "foreign key → Budgets(Id)");
        }
    }
}
