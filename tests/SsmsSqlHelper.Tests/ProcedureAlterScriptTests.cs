using Microsoft.VisualStudio.TestTools.UnitTesting;
using SsmsSqlHelper.Generation;

namespace SsmsSqlHelper.Tests
{
    [TestClass]
    public class ProcedureAlterScriptTests
    {
        [DataTestMethod]
        [DataRow("CREATE PROCEDURE dbo.p AS SELECT 'CREATE PROCEDURE'", "ALTER PROCEDURE dbo.p AS SELECT 'CREATE PROCEDURE'")]
        [DataRow("create proc [dbo].[p] as select 1", "ALTER proc [dbo].[p] as select 1")]
        [DataRow("CREATE OR ALTER PROC dbo.p AS SELECT 1", "ALTER PROC dbo.p AS SELECT 1")]
        [DataRow("ALTER PROCEDURE dbo.p AS SELECT 1", "ALTER PROCEDURE dbo.p AS SELECT 1")]
        [DataRow("-- CREATE PROC fake\r\nCREATE PROCEDURE dbo.p AS SELECT 1", "-- CREATE PROC fake\r\nALTER PROCEDURE dbo.p AS SELECT 1")]
        public void ConvertsOnlyProcedureHeader(string definition, string expected)
        {
            var script = ProcedureAlterScript.Build("My]Db", definition, true, false);
            StringAssert.StartsWith(script, "USE [My]]Db]\r\nGO\r\nSET ANSI_NULLS ON\r\nGO\r\nSET QUOTED_IDENTIFIER OFF\r\nGO\r\n");
            StringAssert.Contains(script, expected + "\r\nGO\r\n");
        }

        [TestMethod]
        public void RejectsNonProcedureDefinition()
        {
            Assert.IsNull(ProcedureAlterScript.Build("db", "CREATE VIEW dbo.v AS SELECT 1", true, true));
            Assert.IsNull(ProcedureAlterScript.Build("db", "", true, true));
        }

        [DataTestMethod]
        [DataRow("VIEW", "CREATE VIEW dbo.v AS SELECT 1", "ALTER VIEW dbo.v AS SELECT 1")]
        [DataRow("FUNCTION", "CREATE OR ALTER FUNCTION dbo.f() RETURNS int AS BEGIN RETURN 1 END", "ALTER FUNCTION dbo.f() RETURNS int AS BEGIN RETURN 1 END")]
        public void ConvertsViewAndFunctionHeaders(string kind, string definition, string expected)
        {
            StringAssert.Contains(ProcedureAlterScript.BuildModule("db", definition, true, true, kind), expected);
        }
    }
}
