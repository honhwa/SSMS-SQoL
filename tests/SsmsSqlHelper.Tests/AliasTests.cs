using System;
using System.Collections.Generic;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using SsmsSqlHelper.Generation;
using SsmsSqlHelper.Metadata;

namespace SsmsSqlHelper.Tests
{
    [TestClass]
    public class AliasTests
    {
        private const string NL = "\r\n";

        private static HashSet<string> Used(params string[] names) => new HashSet<string>(names, StringComparer.OrdinalIgnoreCase);

        [DataTestMethod]
        [DataRow("Budgets", "b")]
        [DataRow("BudgetLines", "bl")]
        [DataRow("ProjectStockWithdrawEquipments", "pswe")]
        [DataRow("PRHeader", "prh")]            // every capital counts
        [DataRow("budgetLines", "bl")]
        [DataRow("budget_lines", "bl")]
        [DataRow("budgets", "b")]
        [DataRow("BUDGET_LINES", "bl")]         // all caps: split on separators only
        [DataRow("BUDGETS", "b")]
        [DataRow("_DataChangeLog", "dcl")]
        [DataRow("tbl_BudgetLine", "tbl")]
        [DataRow("Budget2Lines", "bl")]
        [DataRow("Table2", "t")]
        [DataRow("2020", "t")]                  // no letters at all
        [DataRow("", "t")]
        public void FromTableName(string table, string expected) => Assert.AreEqual(expected, AliasGenerator.FromTableName(table));

        [TestMethod]
        public void UniqueAddsNumberOnCollision()
        {
            Assert.AreEqual("b", AliasGenerator.Unique("Budgets", Used()));
            Assert.AreEqual("b2", AliasGenerator.Unique("Budgets", Used("b")));
            Assert.AreEqual("b3", AliasGenerator.Unique("Budgets", Used("b", "b2")));
            Assert.AreEqual("b2", AliasGenerator.Unique("Budgets", Used("B")));   // case-insensitive
        }

        [TestMethod]
        public void UniqueSkipsReservedWords()
        {
            Assert.AreEqual("on2", AliasGenerator.Unique("OrderNotes", Used()));   // "on" can't be an alias
            Assert.AreEqual("o", AliasGenerator.Unique("Orders", Used()));
        }

        // ---- end-to-end through ContextExpander (the same path Tab and the completion commit use) ----

        private static DbMetadata Metadata() => new DbMetadata("srv", "db", new[]
        {
            new TableInfo("dbo", "Budgets", false),
            new TableInfo("dbo", "BudgetLines", false),
            new TableInfo("dbo", "OrderNotes", false),
            new TableInfo("sales", "Customer", false),
        }, DateTime.Now, TimeSpan.Zero);

        private static string Expand(string text)
        {
            var caret = text.IndexOf('|');
            if (caret < 0) caret = text.Length;
            else text = text.Remove(caret, 1);

            var edit = ContextExpander.TryExpand(text, caret, NL, Metadata());
            if (edit == null)
                return null;
            return text.Remove(edit.Start, edit.Length).Insert(edit.Start, edit.NewText).Insert(edit.Start + edit.CaretOffset, "|");
        }

        [DataTestMethod]
        [DataRow("SELECT * FROM Budgets", "SELECT * FROM Budgets b|")]
        [DataRow("SELECT * FROM dbo.BudgetLines", "SELECT * FROM dbo.BudgetLines bl|")]
        [DataRow("SELECT * FROM [dbo].[BudgetLines]", "SELECT * FROM [dbo].[BudgetLines] bl|")]
        [DataRow("SELECT * FROM budgetlines", "SELECT * FROM budgetlines bl|")]     // alias comes from the real table name
        [DataRow("SELECT * FROM Budgets   ", "SELECT * FROM Budgets b|")]           // gap on the same line is replaced
        [DataRow("SELECT * FROM Budgets b JOIN BudgetLines", "SELECT * FROM Budgets b JOIN BudgetLines bl|")]
        [DataRow("SELECT * FROM Budgets b JOIN Budgets", "SELECT * FROM Budgets b JOIN Budgets b2|")]
        [DataRow("SELECT * FROM Budgets b JOIN Budgets b2 JOIN Budgets", "SELECT * FROM Budgets b JOIN Budgets b2 JOIN Budgets b3|")]
        [DataRow("SELECT * FROM Budgets JOIN BudgetLines", "SELECT * FROM Budgets JOIN BudgetLines bl|")]
        [DataRow("SELECT * FROM sales.Customer c JOIN OrderNotes", "SELECT * FROM sales.Customer c JOIN OrderNotes on2|")]
        public void AddsAliasAfterTableName(string input, string expected) => Assert.AreEqual(expected, Expand(input));

        [TestMethod]
        public void AliasGoesBeforeLaterClauses()
        {
            Assert.AreEqual("SELECT * FROM Budgets b| WITH (NOLOCK) WHERE 1 = 1", Expand("SELECT * FROM Budgets| WITH (NOLOCK) WHERE 1 = 1"));
            Assert.AreEqual("SELECT * FROM Budgets b|, BudgetLines", Expand("SELECT * FROM Budgets|, BudgetLines"));
            Assert.AreEqual("SELECT * FROM Budgets b| JOIN BudgetLines bl ON bl.Id = b.Id", Expand("SELECT * FROM Budgets| JOIN BudgetLines bl ON bl.Id = b.Id"));
        }

        [TestMethod]
        public void UsedAliasesLaterInTheSameQueryAreAvoided()
        {
            // the first table gets b2 because the JOIN below already uses b
            Assert.AreEqual("SELECT * FROM Budgets b2| JOIN Budgets b ON 1 = 1", Expand("SELECT * FROM Budgets| JOIN Budgets b ON 1 = 1"));
        }

        [TestMethod]
        public void OtherStatementsAndSubqueriesDoNotCount()
        {
            Assert.AreEqual("SELECT * FROM Budgets b" + NL + "SELECT * FROM Budgets b|",
                Expand("SELECT * FROM Budgets b" + NL + "SELECT * FROM Budgets"));
            Assert.AreEqual("SELECT * FROM Budgets b; SELECT * FROM Budgets b|", Expand("SELECT * FROM Budgets b; SELECT * FROM Budgets"));
            Assert.AreEqual("SELECT * FROM (SELECT * FROM Budgets b|) x JOIN BudgetLines b ON 1 = 1",
                Expand("SELECT * FROM (SELECT * FROM Budgets|) x JOIN BudgetLines b ON 1 = 1"));
            // a derived table in between doesn't hide the later JOIN's alias from the outer query
            Assert.AreEqual("SELECT * FROM Budgets b2| JOIN (SELECT 1 AS n) s ON 1 = 1 JOIN Budgets b ON 1 = 1",
                Expand("SELECT * FROM Budgets| JOIN (SELECT 1 AS n) s ON 1 = 1 JOIN Budgets b ON 1 = 1"));
        }

        [DataTestMethod]
        [DataRow("SELECT * FROM Budgets b")]                 // already aliased
        [DataRow("SELECT * FROM Budgets AS b")]
        [DataRow("SELECT * FROM Budgets [b]")]
        [DataRow("SELECT * FROM Budgets WITH (NOLOCK)")]     // caret after ')' is not right after the name
        [DataRow("DELETE FROM Budgets")]                     // DELETE FROM takes no alias
        [DataRow("SELECT * FROM Unknown")]
        [DataRow("SELECT * FROM Budgets(1)")]
        [DataRow("SELECT * FROM Budgets" + "\r\n" + "    ")]  // Tab on the next line indents; it must not touch the line break
        [DataRow("SELECT * FROM Budgets -- note ")]
        public void DoesNotAddAlias(string text)
        {
            Assert.IsNull(Expand(text));
        }

        [TestMethod]
        public void StarExpansionUsesGeneratedAliases()
        {
            var budgets = new TableInfo("dbo", "Budgets", false);
            budgets.Columns.Add(new ColumnInfo { Name = "Id", TypeName = "int", BaseTypeName = "int" });
            var lines = new TableInfo("dbo", "BudgetLines", false);
            lines.Columns.Add(new ColumnInfo { Name = "BudgetId", TypeName = "int", BaseTypeName = "int" });
            var md = new DbMetadata("s", "d", new[] { budgets, lines }, DateTime.Now, TimeSpan.Zero);

            const string text = "SELECT *  FROM Budgets b JOIN BudgetLines bl ON bl.BudgetId = b.Id";
            var edit = ContextExpander.TryExpand(text, 8, NL, md);
            Assert.AreEqual("b.Id," + NL + "       bl.BudgetId", edit.NewText);
        }
    }
}
