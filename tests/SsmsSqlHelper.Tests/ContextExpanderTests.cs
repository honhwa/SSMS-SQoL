using System;
using System.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using SsmsSqlHelper.Generation;
using SsmsSqlHelper.Metadata;
using SsmsSqlHelper.Parsing;

namespace SsmsSqlHelper.Tests
{
    [TestClass]
    public class ContextExpanderTests
    {
        private const string NL = "\r\n";

        private static DbMetadata Metadata()
        {
            var customer = new TableInfo("dbo", "Customer", false);
            customer.Columns.Add(new ColumnInfo { Name = "Id", TypeName = "int", BaseTypeName = "int", IsIdentity = true, IsPrimaryKey = true });
            customer.Columns.Add(new ColumnInfo { Name = "Name", TypeName = "nvarchar", BaseTypeName = "nvarchar", MaxLength = 200 });
            customer.Columns.Add(new ColumnInfo { Name = "Email", TypeName = "varchar", BaseTypeName = "varchar", MaxLength = 100, IsNullable = true });
            customer.Columns.Add(new ColumnInfo { Name = "CreatedAt", TypeName = "datetime", BaseTypeName = "datetime", HasDefault = true });
            customer.Columns.Add(new ColumnInfo { Name = "RowVer", TypeName = "timestamp", BaseTypeName = "timestamp" });

            var order = new TableInfo("sales", "Order", false);
            order.Columns.Add(new ColumnInfo { Name = "OrderId", TypeName = "uniqueidentifier", BaseTypeName = "uniqueidentifier", IsPrimaryKey = true });
            order.Columns.Add(new ColumnInfo { Name = "CustomerId", TypeName = "int", BaseTypeName = "int" });
            order.Columns.Add(new ColumnInfo { Name = "Total", TypeName = "decimal", BaseTypeName = "decimal", Precision = 18, Scale = 2 });

            var log = new TableInfo("dbo", "Log", false);
            log.Columns.Add(new ColumnInfo { Name = "Message", TypeName = "nvarchar", BaseTypeName = "nvarchar", MaxLength = -1 });

            return new DbMetadata("srv", "db", new[]
            {
                customer, order, log,
                new TableInfo("dbo", "Budgets", false),
                new TableInfo("dbo", "BudgetLines", false),
                new TableInfo("dbo", "OrderNotes", false),
            }, DateTime.Now, TimeSpan.Zero);
        }

        /// <summary>Applies the expansion at the end of <paramref name="text"/> (or at '|') and returns the result with '|' at the caret.</summary>
        private static string Expand(string text)
        {
            var caret = text.IndexOf('|');
            if (caret < 0) caret = text.Length;
            else text = text.Remove(caret, 1);

            var edit = ContextExpander.TryExpand(text, caret, NL, Metadata());
            if (edit == null)
                return null;

            var result = text.Remove(edit.Start, edit.Length).Insert(edit.Start, edit.NewText);
            return result.Insert(edit.Start + edit.CaretOffset, "|");
        }

        [TestMethod]
        public void InsertGeneratesColumnsAndValues()
        {
            Assert.AreEqual(
                "INSERT INTO dbo.Customer" + NL +
                "(" + NL +
                "    Name," + NL +
                "    Email," + NL +
                "    CreatedAt" + NL +
                ")" + NL +
                "VALUES" + NL +
                "(" + NL +
                "    N'|',      -- Name nvarchar(100) NOT NULL" + NL +
                "    '',       -- Email varchar(100) NULL" + NL +
                "    GETDATE() -- CreatedAt datetime NOT NULL (has default)" + NL +
                ")",
                Expand("INSERT INTO dbo.Customer"));
        }

        [TestMethod]
        public void InsertKeepsIndentAndReplacesTrailingSpace()
        {
            var result = Expand("BEGIN" + NL + "    INSERT Customer  ");
            StringAssert.StartsWith(result, "BEGIN" + NL + "    INSERT Customer" + NL + "    (" + NL + "        Name,");
        }

        [TestMethod]
        public void InsertWithOnlyGeneratedColumnsUsesDefaultValues()
        {
            var md = Metadata();
            var t = new TableInfo("dbo", "OnlyId", false);
            t.Columns.Add(new ColumnInfo { Name = "Id", TypeName = "int", IsIdentity = true });
            var edit = SqlGenerator.InsertBody(t, "", NL);
            Assert.AreEqual(NL + "DEFAULT VALUES", edit.Text);
        }

        [DataTestMethod]
        [DataRow("INSERT INTO dbo.Customer (Name) VALUES (N'x')|")]  // caret not after the name
        [DataRow("INSERT INTO dbo.Customer| (Name)")]               // already has a column list
        [DataRow("INSERT INTO dbo.Customer| VALUES")]
        [DataRow("INSERT INTO dbo.Unknown")]
        [DataRow("-- INSERT INTO dbo.Customer")]
        [DataRow("INSERT INTO dbo.Cust|omer")]                      // caret inside the name
        public void InsertDoesNotExpand(string text) => Assert.IsNull(Expand(text));

        [TestMethod]
        public void UpdateGeneratesSetAndPrimaryKeyWhere()
        {
            Assert.AreEqual(
                "UPDATE sales.[Order]" + NL +
                "SET CustomerId = |0, -- int NOT NULL" + NL +
                "    Total = 0       -- decimal(18,2) NOT NULL" + NL +
                "WHERE OrderId = NEWID() -- uniqueidentifier NOT NULL",
                Expand("UPDATE sales.[Order]"));
        }

        [TestMethod]
        public void UpdateWithoutPrimaryKeyLeavesWhereEmpty()
        {
            var result = Expand("UPDATE dbo.Log");
            StringAssert.EndsWith(result, NL + "WHERE ");
        }

        [DataTestMethod]
        [DataRow("UPDATE dbo.Customer| SET Name = N''")]
        [DataRow("ALTER TABLE x ADD CONSTRAINT fk FOREIGN KEY (a) REFERENCES b (a) ON UPDATE Customer")]
        public void UpdateDoesNotExpand(string text) => Assert.IsNull(Expand(text));

        [TestMethod]
        public void StarSingleTableNoAlias()
        {
            Assert.AreEqual(
                "SELECT Id," + NL +
                "       Name," + NL +
                "       Email," + NL +
                "       CreatedAt," + NL +
                "       RowVer| FROM dbo.Customer",
                Expand("SELECT *| FROM dbo.Customer"));
        }

        [TestMethod]
        public void StarPickerStartsWithCandidatesAndCanInsertOnlyCheckedColumns()
        {
            const string sql = "SELECT * FROM dbo.Customer";
            var context = SqlContext.GetTabContext(sql, "SELECT *".Length);
            var candidates = ContextExpander.GetStarColumns(context, Metadata());
            CollectionAssert.AreEqual(new[] { "Id", "Name", "Email", "CreatedAt", "RowVer" },
                candidates.Select(c => c.Column.Name).ToArray());

            var selected = new[] { candidates[1], candidates[3] };
            var edit = ContextExpander.ExpandSelectedStar(sql, context, NL, selected);
            Assert.AreEqual("Name," + NL + "       CreatedAt", edit.NewText);
            Assert.AreEqual(context.ReplaceStart, edit.Start);
            Assert.AreEqual(1, edit.Length);
        }

        [TestMethod]
        public void StarPickerKeepsPrefixesWithJoinAndQualifier()
        {
            const string sql = "SELECT o.* FROM Customer c JOIN sales.[Order] o ON o.CustomerId = c.Id";
            var context = SqlContext.GetTabContext(sql, "SELECT o.*".Length);
            var candidates = ContextExpander.GetStarColumns(context, Metadata());
            CollectionAssert.AreEqual(new[] { "o.OrderId", "o.CustomerId", "o.Total" },
                candidates.Select(c => c.SqlName).ToArray());
            Assert.IsNull(ContextExpander.ExpandSelectedStar(sql, context, NL, new ContextExpander.StarColumn[0]));
        }

        [TestMethod]
        public void StarWithJoinUsesAliases()
        {
            var result = Expand("SELECT *|" + NL + "FROM Customer c" + NL + "JOIN sales.[Order] AS o ON o.CustomerId = c.Id");
            StringAssert.StartsWith(result, "SELECT c.Id," + NL + "       c.Name,");
            StringAssert.Contains(result, "       o.OrderId," + NL + "       o.CustomerId," + NL + "       o.Total|" + NL + "FROM");
        }

        [TestMethod]
        public void QualifiedStarExpandsOnlyThatTable()
        {
            var result = Expand("SELECT o.*| FROM Customer c JOIN sales.[Order] o ON o.CustomerId = c.Id");
            StringAssert.StartsWith(result, "SELECT o.OrderId," + NL + "       o.CustomerId," + NL + "       o.Total| FROM");
        }

        [TestMethod]
        public void StarAfterTopAndWithHint()
        {
            var result = Expand("SELECT TOP (10) *| FROM dbo.Log WITH (NOLOCK) WHERE 1 = 1");
            Assert.AreEqual("SELECT TOP (10) Message| FROM dbo.Log WITH (NOLOCK) WHERE 1 = 1", result);
        }

        [TestMethod]
        public void StarInSubqueryUsesInnerFrom()
        {
            var result = Expand("SELECT x.* FROM (SELECT *| FROM dbo.Log) x");
            Assert.AreEqual("SELECT x.* FROM (SELECT Message| FROM dbo.Log) x", result);
        }

        [TestMethod]
        public void StarStopsAtNextStatement()
        {
            var result = Expand("SELECT *| FROM dbo.Log" + NL + "SELECT * FROM dbo.Customer");
            StringAssert.StartsWith(result, "SELECT Message| FROM dbo.Log");
        }

        [DataTestMethod]
        [DataRow("SELECT COUNT(*|) FROM dbo.Log")]       // aggregate
        [DataRow("SELECT a *| FROM dbo.Log")]           // multiplication
        [DataRow("SELECT *| FROM dbo.Nope")]            // unknown table
        [DataRow("SELECT z.*| FROM dbo.Log l")]         // unknown qualifier
        [DataRow("SELECT *|")]                          // no FROM yet
        public void StarDoesNotExpand(string text) => Assert.IsNull(Expand(text));

        [DataTestMethod]
        [DataRow("SELECT * FROM |", "")]
        [DataRow("SELECT * FROM dbo.Cu|", "dbo.Cu")]
        [DataRow("SELECT * FROM a JOIN Or|", "Or")]
        [DataRow("INSERT INTO |", "")]
        [DataRow("UPDATE [sales].|", "[sales].")]
        [DataRow("TRUNCATE TABLE |", "")]
        public void TableNamePositions(string text, string typed)
        {
            var caret = text.IndexOf('|');
            text = text.Remove(caret, 1);
            Assert.IsTrue(SqlContext.TryGetTableNameSpan(text, caret, out var start, out var end));
            Assert.AreEqual(typed, text.Substring(start, caret - start));
            Assert.AreEqual(caret, end);
        }

        [TestMethod]
        public void TableNameSpanExtendsOverRestOfWord()
        {
            const string text = "SELECT * FROM Customer";
            var caret = text.IndexOf("tomer", StringComparison.Ordinal);
            Assert.IsTrue(SqlContext.TryGetTableNameSpan(text, caret, out var start, out var end));
            Assert.AreEqual("Customer", text.Substring(start, end - start));
        }

        [TestMethod]
        public void FromKeywordBecomesTableNameContextAfterSpace()
        {
            const string beforeSpace = "SELECT * FROM";
            const string afterSpace = "SELECT * FROM ";
            const string partialName = "SELECT * FROM budgets";
            Assert.IsFalse(SqlContext.TryGetTableNameSpan(beforeSpace, beforeSpace.Length, out _, out _));
            Assert.IsTrue(SqlContext.TryGetTableNameSpan(afterSpace, afterSpace.Length, out _, out _));
            Assert.IsTrue(SqlContext.TryGetTableNameSpan(partialName, partialName.Length, out var start, out _));
            Assert.AreEqual("budgets", partialName.Substring(start));
        }

        [DataTestMethod]
        [DataRow("SELECT * |")]
        [DataRow("SELECT * FROM dbo.Customer c|")]
        [DataRow("SELECT * FROM dbo.Customer |")]
        [DataRow("INSERT INT|")]                      // typing INTO
        [DataRow("CREATE TABLE |")]
        [DataRow("-- FROM |")]
        [DataRow("SELECT 'FROM |")]
        [DataRow("ON UPDATE |")]
        public void NotTableNamePositions(string text)
        {
            var caret = text.IndexOf('|');
            Assert.IsFalse(SqlContext.TryGetTableNameSpan(text.Remove(caret, 1), caret, out _, out _));
        }

        [TestMethod]
        public void TokenizerHandlesNestedCommentsAndEscapes()
        {
            var tokens = SqlTokenizer.Tokenize("/* a /* b */ c */ N'it''s' [a]]b] @x");
            CollectionAssert.AreEqual(
                new[] { TokenKind.Comment, TokenKind.WhiteSpace, TokenKind.String, TokenKind.WhiteSpace,
                        TokenKind.QuotedIdentifier, TokenKind.WhiteSpace, TokenKind.Variable },
                tokens.ConvertAll(t => t.Kind));
        }
    }
}
