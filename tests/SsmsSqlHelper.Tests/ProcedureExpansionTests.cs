using System;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using SsmsSqlHelper.Generation;
using SsmsSqlHelper.Metadata;
using SsmsSqlHelper.Parsing;

namespace SsmsSqlHelper.Tests
{
    [TestClass]
    public class ProcedureExpansionTests
    {
        private static DbMetadata Metadata()
        {
            var save = new ProcedureInfo("dbo", "Save Customer");
            save.Parameters.Add(new ProcedureParameterInfo { Name = "@Name", TypeSchema = "sys", TypeName = "nvarchar", MaxLength = 200 });
            save.Parameters.Add(new ProcedureParameterInfo { Name = "@Count", TypeSchema = "sys", TypeName = "int" });
            var lookup = new ProcedureInfo("sales", "Lookup");
            lookup.Parameters.Add(new ProcedureParameterInfo { Name = "@Result", TypeSchema = "sys", TypeName = "int", IsOutput = true });
            var batch = new ProcedureInfo("sales", "Batch");
            batch.Parameters.Add(new ProcedureParameterInfo { Name = "@Rows", TypeSchema = "sales", TypeName = "RowList", IsReadOnly = true });
            return new DbMetadata("srv", "db", new TableInfo[0], DateTime.Now, TimeSpan.Zero,
                procedures: new[] { save, lookup, batch });
        }

        private static string Expand(string source)
        {
            var caret = source.IndexOf('|');
            var text = caret < 0 ? source : source.Remove(caret, 1);
            if (caret < 0) caret = text.Length;
            var edit = ContextExpander.TryExpand(text, caret, "\r\n", Metadata());
            if (edit == null) return null;
            var result = text.Remove(edit.Start, edit.Length).Insert(edit.Start, edit.NewText);
            return result.Insert(edit.Start + edit.CaretOffset, "|");
        }

        [TestMethod]
        public void ExecExpandsNamedParametersAndTypes()
        {
            Assert.AreEqual("EXEC dbo.[Save Customer]\r\n    @Name = |NULL /* nvarchar(100) */,\r\n    @Count = NULL /* int */",
                Expand("EXEC dbo.[Save Customer]"));
        }

        [TestMethod]
        public void ExecuteOutputDeclaresVariable()
        {
            Assert.AreEqual("    BEGIN\r\n        DECLARE @SqlHelper_Result int;\r\n        EXEC sales.Lookup\r\n            @Result = |@SqlHelper_Result OUTPUT /* int */\r\n    END",
                Expand("    EXEC sales.Lookup"));
        }

        [TestMethod]
        public void TableValuedParameterDeclaresTypedVariable()
        {
            Assert.AreEqual("BEGIN\r\n    DECLARE @SqlHelper_Rows sales.RowList;\r\n    EXEC sales.Batch\r\n        @Rows = |@SqlHelper_Rows /* sales.RowList READONLY */\r\nEND",
                Expand("EXEC sales.Batch"));
        }

        [DataTestMethod]
        [DataRow("EXEC dbo.[Save Customer] @Name = N'x'|")]
        [DataRow("EXEC dbo.[Save Customer]| @Name = N'x'")]
        [DataRow("SELECT 'EXEC dbo.[Save Customer]|'")]
        [DataRow("EXEC dbo.[Save Customer]\r\n|")]
        [DataRow("EXEC missing|")]
        public void DoesNotExpandExistingOrUnrelatedStatements(string text) => Assert.IsNull(Expand(text));

        [TestMethod]
        public void ProcedureLookupPrefersDboAndRejectsAmbiguity()
        {
            var md = Metadata();
            Assert.AreEqual("dbo.Save Customer", md.FindProcedure("[Save Customer]").ToString());
            Assert.AreEqual("sales.Lookup", md.FindProcedure("sales.Lookup").ToString());
            Assert.AreEqual("sales.Lookup", md.FindProcedure("db.sales.Lookup").ToString());
            Assert.IsNull(md.FindProcedure("otherdb.sales.Lookup"));
            Assert.IsNull(md.FindProcedure("Missing"));
        }

        [DataTestMethod]
        [DataRow("EXEC |", true)]
        [DataRow("EXEC|", true)]
        [DataRow("EXEC dbo.Sav|", true)]
        [DataRow("EXECUTE sales.|", true)]
        [DataRow("SELECT 'EXEC |'", false)]
        [DataRow("SELECT dbo.Sav|", false)]
        public void ProcedureCompletionOnlyFollowsExecute(string source, bool expected)
        {
            var caret = source.IndexOf('|');
            var text = source.Remove(caret, 1);
            Assert.AreEqual(expected, SqlContext.TryGetProcedureNameSpan(text, caret, out _, out _));
        }

        [TestMethod]
        public void ExecBeforeNextStatementCanExpand()
        {
            StringAssert.StartsWith(Expand("EXEC dbo.[Save Customer]|\r\nSELECT 1"),
                "EXEC dbo.[Save Customer]\r\n    @Name = ");
        }

        [DataTestMethod]
        [DataRow("EXEC dbo.[Save Cus|tomer]", "dbo.[Save Customer]")]
        [DataRow("EXECUTE sales.Look|up @Result = NULL", "sales.Lookup")]
        [DataRow("EXEC @return_code = sales.Look|up", "sales.Lookup")]
        [DataRow("EXEC\r\n    sales.Look|up", "sales.Lookup")]
        [DataRow("CREATE OR ALTER PROCEDURE dbo.[Save Cus|tomer] AS SELECT 1", "dbo.[Save Customer]")]
        [DataRow("dbo.[Save Cus|tomer]", "dbo.[Save Customer]")]
        [DataRow("    sales.Look|up @Result = 1", "sales.Lookup")]
        [DataRow("SELECT 1\r\n    dbo.[Save Cus|tomer]", "dbo.[Save Customer]")]
        [DataRow("SELECT 'EXEC dbo.[Save Cus|tomer]'", null)]
        [DataRow("SELECT dbo.[Save Cus|tomer]", null)]
        [DataRow("-- dbo.[Save Cus|tomer]", null)]
        [DataRow("EXEC dbo.Lookup @Result = |NULL", null)]
        public void F12FindsOnlyProcedureNames(string source, string expected)
        {
            var caret = source.IndexOf('|');
            var text = source.Remove(caret, 1);
            Assert.AreEqual(expected, SqlContext.GetProcedureAtCaret(text, caret)?.Text);
        }
    }
}
