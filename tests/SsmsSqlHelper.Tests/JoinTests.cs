using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using SsmsSqlHelper.Generation;
using SsmsSqlHelper.Metadata;
using SsmsSqlHelper.Parsing;

namespace SsmsSqlHelper.Tests
{
    [TestClass]
    public class JoinTests
    {
        private const string NL = "\r\n";

        private static ColumnInfo Col(string name, string type = "int", bool pk = false) =>
            new ColumnInfo { Name = name, TypeName = type, BaseTypeName = type, IsPrimaryKey = pk };

        private static TableInfo Table(string name, params ColumnInfo[] columns)
        {
            var t = new TableInfo("dbo", name, false);
            t.Columns.AddRange(columns);
            return t;
        }

        /// <summary>Mirrors the shape of the real database: one declared FK, the rest only by naming convention.</summary>
        private static DbMetadata Metadata()
        {
            var budgets = Table("Budgets", Col("Id", pk: true), Col("Name", "nvarchar"));
            var lines = Table("BudgetLines", Col("Id", pk: true), Col("BudgetId"), Col("WBSId"), Col("UnitId"), Col("ItemCategoryId"), Col("TempEditReviseBudgetId"));
            var wbs = Table("WBS", Col("Id", pk: true), Col("BudgetId"));
            var units = Table("Units", Col("Id", pk: true));
            var categories = Table("ItemCategories", Col("Id", pk: true));
            var employees = Table("Employees", Col("Id", pk: true), Col("ManagerId"));
            var guidKey = Table("Tokens", Col("Id", "uniqueidentifier", pk: true));
            var tokenUser = Table("Users", Col("Id", pk: true), Col("TokenId"));        // int vs guid: no match
            var unrelated = Table("Absences", Col("Id", pk: true), Col("Type", "nvarchar"));
            var paid = Table("Payments", Col("Id", pk: true), Col("Paid"), Col("Valid"));  // "Paid"/"Valid" are not references

            var fk = new ForeignKeyInfo("FK_BudgetBudgetLine", lines, budgets);
            fk.Columns.Add(("BudgetId", "Id"));

            return new DbMetadata("s", "d", new[] { budgets, lines, wbs, units, categories, employees, guidKey, tokenUser, unrelated, paid },
                DateTime.Now, TimeSpan.Zero, new[] { fk });
        }

        private static List<JoinCandidate> Suggest(string target, string targetAlias, params (string Table, string Alias)[] others)
        {
            var md = Metadata();
            return JoinSuggester.Suggest(md, md.FindTable(target), targetAlias, others.Select(o => new JoinScopeTable(md.FindTable(o.Table), o.Alias)));
        }

        private static string[] Conditions(List<JoinCandidate> c) => c.Select(x => x.Condition).ToArray();

        // ---- JoinSuggester ----

        [TestMethod]
        public void DeclaredForeignKeyChildJoinsParent()
        {
            var result = Suggest("BudgetLines", "bl", ("Budgets", "b"));
            Assert.AreEqual("bl.BudgetId = b.Id", result[0].Condition);
            Assert.IsTrue(result[0].IsForeignKey);
            Assert.AreEqual("FK_BudgetBudgetLine", result[0].Source);
            Assert.AreEqual("BudgetLines(BudgetId) → Budgets(Id)", result[0].Detail);
        }

        [TestMethod]
        public void DeclaredForeignKeyParentJoinsChild()
        {
            // target stays on the left even when it is the referenced table
            Assert.AreEqual("b.Id = bl.BudgetId", Suggest("Budgets", "b", ("BudgetLines", "bl"))[0].Condition);
        }

        [TestMethod]
        public void DeclaredKeyIsNotRepeatedAsGuess()
        {
            // BudgetId is both a declared key and a name match; it must be listed once
            Assert.AreEqual(1, Suggest("BudgetLines", "bl", ("Budgets", "b")).Count(c => c.Condition == "bl.BudgetId = b.Id"));
        }

        [TestMethod]
        public void GuessesFromColumnNamesWhenNoKeyIsDeclared()
        {
            var result = Suggest("BudgetLines", "bl", ("WBS", "w"));
            Assert.AreEqual("bl.WBSId = w.Id", result.Single(c => !c.IsForeignKey && c.Condition.StartsWith("bl.WBSId")).Condition);
            Assert.IsTrue(result.All(c => !c.IsForeignKey));
            Assert.AreEqual("name match", result[0].Source);
        }

        [DataTestMethod]
        [DataRow("Units", "u", "bl.UnitId = u.Id")]                    // + s
        [DataRow("ItemCategories", "ic", "bl.ItemCategoryId = ic.Id")] // y -> ies
        public void GuessHandlesPlurals(string other, string alias, string expected) =>
            CollectionAssert.Contains(Conditions(Suggest("BudgetLines", "bl", (other, alias))), expected);

        [TestMethod]
        public void GuessDropsLeadingWordsOfTheStem()
        {
            // TempEditReviseBudgetId -> ...Budget -> Budgets, ranked after the exact BudgetId match
            var result = Suggest("BudgetLines", "bl", ("Budgets", "b"));
            CollectionAssert.AreEqual(new[] { "bl.BudgetId = b.Id", "bl.TempEditReviseBudgetId = b.Id" }, Conditions(result));
            Assert.IsTrue(result.Last().Detail.Contains("guessed"));
        }

        [TestMethod]
        public void ForeignKeysComeBeforeGuessesAndExactBeforeSuffixMatches()
        {
            var result = Suggest("Budgets", "b", ("BudgetLines", "bl"));
            Assert.IsTrue(result[0].IsForeignKey);
            Assert.AreEqual("b.Id = bl.BudgetId", result[0].Condition);
            Assert.AreEqual("b.Id = bl.TempEditReviseBudgetId", result[1].Condition);
        }

        [TestMethod]
        public void ColumnTypeMustMatchTheKey() =>
            Assert.AreEqual(0, Suggest("Users", "u", ("Tokens", "t")).Count);

        [TestMethod]
        public void PaidAndValidAreNotReferences() =>
            Assert.AreEqual(0, Suggest("Payments", "p", ("Absences", "a")).Count);

        [TestMethod]
        public void UnrelatedTablesGiveNothing() =>
            Assert.AreEqual(0, Suggest("Absences", "a", ("Budgets", "b")).Count);

        [TestMethod]
        public void SeveralTablesInScope()
        {
            var result = Suggest("BudgetLines", "bl", ("Budgets", "b"), ("Units", "u"));
            // declared key, then the exact name match, then the looser one
            CollectionAssert.AreEqual(new[] { "bl.BudgetId = b.Id", "bl.UnitId = u.Id", "bl.TempEditReviseBudgetId = b.Id" }, Conditions(result));
        }

        [TestMethod]
        public void SelfReferenceOffersBothDirections()
        {
            var employees = Table("Employees", Col("Id", pk: true), Col("ManagerId"));
            var fk = new ForeignKeyInfo("FK_Manager", employees, employees);
            fk.Columns.Add(("ManagerId", "Id"));
            var md = new DbMetadata("s", "d", new[] { employees }, DateTime.Now, TimeSpan.Zero, new[] { fk });

            var result = JoinSuggester.Suggest(md, employees, "m", new[] { new JoinScopeTable(employees, "e") });
            CollectionAssert.AreEquivalent(new[] { "m.ManagerId = e.Id", "m.Id = e.ManagerId" }, Conditions(result));
        }

        [TestMethod]
        public void CompositeForeignKeyJoinsOnAllColumns()
        {
            var a = Table("Parents", Col("A", pk: true), Col("B", pk: true));
            var b = Table("Children", Col("Id", pk: true), Col("PA"), Col("PB"));
            var fk = new ForeignKeyInfo("FK_C_P", b, a);
            fk.Columns.Add(("PA", "A"));
            fk.Columns.Add(("PB", "B"));
            var md = new DbMetadata("s", "d", new[] { a, b }, DateTime.Now, TimeSpan.Zero, new[] { fk });

            var result = JoinSuggester.Suggest(md, b, "c", new[] { new JoinScopeTable(a, "p") });
            Assert.AreEqual("c.PA = p.A AND c.PB = p.B", result.Single().Condition);
        }

        [TestMethod]
        public void QuotesColumnNamesThatNeedIt()
        {
            var a = Table("A", Col("Id", pk: true));
            var b = Table("B", Col("Id", pk: true), Col("A Id"));
            var fk = new ForeignKeyInfo("FK", b, a);
            fk.Columns.Add(("A Id", "Id"));
            var md = new DbMetadata("s", "d", new[] { a, b }, DateTime.Now, TimeSpan.Zero, new[] { fk });
            Assert.AreEqual("b.[A Id] = a.Id", JoinSuggester.Suggest(md, b, "b", new[] { new JoinScopeTable(a, "a") }).Single().Condition);
        }

        [TestMethod]
        public void AreRelated()
        {
            var md = Metadata();
            Assert.IsTrue(JoinSuggester.AreRelated(md, md.FindTable("Budgets"), md.FindTable("BudgetLines")));   // FK, either order
            Assert.IsTrue(JoinSuggester.AreRelated(md, md.FindTable("BudgetLines"), md.FindTable("Budgets")));
            Assert.IsTrue(JoinSuggester.AreRelated(md, md.FindTable("Units"), md.FindTable("BudgetLines")));     // guess
            Assert.IsFalse(JoinSuggester.AreRelated(md, md.FindTable("Absences"), md.FindTable("Budgets")));
        }

        // ---- context detection ----

        private static string Expand(string text, DbMetadata md = null)
        {
            var caret = text.IndexOf('|');
            if (caret < 0) caret = text.Length;
            else text = text.Remove(caret, 1);

            var edit = ContextExpander.TryExpand(text, caret, NL, md ?? Metadata());
            if (edit == null)
                return null;
            return text.Remove(edit.Start, edit.Length).Insert(edit.Start, edit.NewText).Insert(edit.Start + edit.CaretOffset, "|");
        }

        [TestMethod]
        public void TabAfterJoinTableWritesTheDeclaredForeignKey() =>
            Assert.AreEqual("SELECT * FROM Budgets b JOIN BudgetLines bl ON bl.BudgetId = b.Id|",
                Expand("SELECT * FROM Budgets b JOIN BudgetLines"));

        [TestMethod]
        public void TabWorksWithoutAnAliasOnTheFirstTable() =>
            Assert.AreEqual("SELECT * FROM Budgets JOIN BudgetLines bl ON bl.BudgetId = Budgets.Id|",
                Expand("SELECT * FROM Budgets JOIN BudgetLines"));

        [TestMethod]
        public void TabAfterJoinWithOnlyGuessesLeavesOnAndOffersTheList()
        {
            var md = Metadata();
            var edit = ContextExpander.TryExpand("SELECT * FROM BudgetLines bl JOIN Units", "SELECT * FROM BudgetLines bl JOIN Units".Length, NL, md);
            Assert.AreEqual(" u ON ", edit.NewText);
            Assert.IsTrue(edit.OfferJoinConditions);
        }

        [TestMethod]
        public void GuessesNextToADeclaredKeyDoNotTurnItIntoAList()
        {
            // BudgetLines also has TempEditReviseBudgetId (a guess), but the one real FK is written directly
            var text = "SELECT * FROM Budgets b JOIN BudgetLines";
            var edit = ContextExpander.TryExpand(text, text.Length, NL, Metadata());
            Assert.AreEqual(" bl ON bl.BudgetId = b.Id", edit.NewText);
            Assert.IsFalse(edit.OfferJoinConditions);
        }

        [TestMethod]
        public void SeveralDeclaredKeysLeaveOnAndOfferTheList()
        {
            var addresses = Table("Addresses", Col("Id", pk: true));
            var orders = Table("Orders", Col("Id", pk: true), Col("BillToId"), Col("ShipToId"));
            var bill = new ForeignKeyInfo("FK_Bill", orders, addresses);
            bill.Columns.Add(("BillToId", "Id"));
            var ship = new ForeignKeyInfo("FK_Ship", orders, addresses);
            ship.Columns.Add(("ShipToId", "Id"));
            var md = new DbMetadata("s", "d", new[] { addresses, orders }, DateTime.Now, TimeSpan.Zero, new[] { bill, ship });

            var text = "SELECT * FROM Addresses a JOIN Orders";
            var edit = ContextExpander.TryExpand(text, text.Length, NL, md);
            Assert.AreEqual(" o ON ", edit.NewText);
            Assert.IsTrue(edit.OfferJoinConditions);
        }

        [TestMethod]
        public void NoRelationMeansAliasOnly() =>
            Assert.AreEqual("SELECT * FROM Budgets b JOIN Absences a|", Expand("SELECT * FROM Budgets b JOIN Absences"));

        [DataTestMethod]
        [DataRow("SELECT * FROM Budgets b CROSS JOIN BudgetLines", "SELECT * FROM Budgets b CROSS JOIN BudgetLines bl|")]
        [DataRow("SELECT * FROM Budgets b JOIN BudgetLines ON 1 = 1", null)]    // alias missing but ON already there: caret isn't after the name
        public void CrossJoinTakesNoCondition(string text, string expected) => Assert.AreEqual(expected, Expand(text));

        [TestMethod]
        public void ExistingOnIsNotDuplicated() =>
            Assert.AreEqual("SELECT * FROM Budgets b JOIN BudgetLines bl| ON 1 = 1",
                Expand("SELECT * FROM Budgets b JOIN BudgetLines| ON 1 = 1"));

        [TestMethod]
        public void LeftJoinGetsConditionsToo() =>
            Assert.AreEqual("SELECT * FROM Budgets b LEFT JOIN BudgetLines bl ON bl.BudgetId = b.Id|",
                Expand("SELECT * FROM Budgets b LEFT JOIN BudgetLines"));

        [TestMethod]
        public void SubqueryJoinsOnlySeeTablesOfTheirOwnQuery() =>
            Assert.AreEqual("SELECT * FROM Units u WHERE EXISTS (SELECT 1 FROM Budgets b JOIN BudgetLines bl ON bl.BudgetId = b.Id|)",
                Expand("SELECT * FROM Units u WHERE EXISTS (SELECT 1 FROM Budgets b JOIN BudgetLines|)"));

        [TestMethod]
        public void JoinScopeListsTablesToTheLeft()
        {
            const string text = "SELECT * FROM Budgets b JOIN Units u ON 1 = 1 JOIN ";
            var scope = SqlContext.GetJoinScope(text, text.Length);
            CollectionAssert.AreEqual(new[] { "b", "u" }, scope.Select(r => r.Alias).ToArray());
        }

        [DataTestMethod]
        [DataRow("SELECT * FROM Budgets b CROSS JOIN ")]
        [DataRow("SELECT * FROM Budgets b, ")]
        [DataRow("SELECT * FROM ")]
        public void JoinScopeIsNullOutsidePlainJoin(string text) => Assert.IsNull(SqlContext.GetJoinScope(text, text.Length));

        [TestMethod]
        public void TryGetJoinOnFindsTargetAndEarlierTables()
        {
            const string text = "SELECT * FROM Budgets b JOIN Units u ON 1 = 1 JOIN BudgetLines bl ON ";
            Assert.IsTrue(SqlContext.TryGetJoinOn(text, text.Length, out var start, out var end, out var target, out var earlier));
            Assert.AreEqual("BudgetLines", target.Name.Text);
            Assert.AreEqual("bl", target.Alias);
            CollectionAssert.AreEqual(new[] { "b", "u" }, earlier.Select(r => r.Alias).ToArray());
            Assert.AreEqual(start, end);
        }

        [TestMethod]
        public void TryGetJoinOnReportsTheWordBeingTyped()
        {
            const string text = "SELECT * FROM Budgets b JOIN BudgetLines bl ON bl.Bud";
            Assert.IsTrue(SqlContext.TryGetJoinOn(text, text.Length, out var start, out var end, out _, out _));
            Assert.AreEqual("bl.Bud", text.Substring(start, end - start));
        }

        [DataTestMethod]
        [DataRow("SELECT * FROM Budgets b JOIN BudgetLines bl ON bl.BudgetId = b.Id AND ")]   // not the start of the condition
        [DataRow("SELECT * FROM Budgets b JOIN BudgetLines bl ON")]                           // still typing "ON"
        [DataRow("SELECT * FROM Budgets b JOIN BudgetLines bl ")]
        [DataRow("SELECT * FROM Budgets b, BudgetLines bl ON ")]                              // comma join, not a JOIN
        [DataRow("SELECT * FROM Budgets b JOIN BudgetLines bl ON -- ")]                       // inside a comment
        [DataRow("MERGE t USING s ON ")]
        public void TryGetJoinOnRejects(string text) =>
            Assert.IsFalse(SqlContext.TryGetJoinOn(text, text.Length, out _, out _, out _, out _));

        [TestMethod]
        public void ExpansionNeverAddsOnWhenTheTargetIsUnknown() =>
            Assert.IsNull(Expand("SELECT * FROM Budgets b JOIN Nope"));
    }
}
