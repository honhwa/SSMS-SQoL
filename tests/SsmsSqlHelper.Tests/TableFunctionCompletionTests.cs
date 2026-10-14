using Microsoft.VisualStudio.TestTools.UnitTesting;
using SsmsSqlHelper.Metadata;
using SsmsSqlHelper.Parsing;

namespace SsmsSqlHelper.Tests
{
    [TestClass]
    public class TableFunctionCompletionTests
    {
        [DataTestMethod]
        [DataRow("SELECT * FROM dbo.fn_organizationDep|", true)]
        [DataRow("SELECT * JOIN dbo.fn_organizationDep|", true)]
        [DataRow("INSERT INTO dbo.fn_organizationDep|", false)]
        [DataRow("UPDATE dbo.fn_organizationDep|", false)]
        public void FunctionsAreOnlyOfferedForQuerySources(string markedSql, bool expected)
        {
            var caret = markedSql.IndexOf('|');
            var sql = markedSql.Remove(caret, 1);
            Assert.IsTrue(SqlContext.TryGetTableNameSpan(sql, caret, out var start, out _));
            Assert.AreEqual(expected, SqlContext.IsTableFunctionSource(sql, start));
        }

        [TestMethod]
        public void SignatureIncludesFunctionParameters()
        {
            var function = new TableFunctionInfo("dbo", "fn_organizationDepends", "IF", "Organization dependencies");
            function.Parameters.Add(new ProcedureParameterInfo
            {
                Name = "@OrgId", TypeSchema = "sys", TypeName = "int",
            });
            Assert.AreEqual("dbo.fn_organizationDepends(@OrgId int)", function.Signature);
            Assert.AreEqual("Inline table-valued function", function.Kind);
        }
    }
}
