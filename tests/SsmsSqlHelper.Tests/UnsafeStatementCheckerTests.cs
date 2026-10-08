using System.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using SsmsSqlHelper.Parsing;

namespace SsmsSqlHelper.Tests
{
    [TestClass]
    public class UnsafeStatementCheckerTests
    {
        private static string[] Found(string sql) =>
            UnsafeStatementChecker.Find(sql).Select(s => s.Keyword + "@" + s.Line).ToArray();

        // ---- statements that are flagged ----

        [DataTestMethod]
        [DataRow("UPDATE Budgets SET Name = 'x'", "UPDATE@1")]
        [DataRow("update dbo.Budgets set Name = 'x'", "UPDATE@1")]
        [DataRow("DELETE FROM Budgets", "DELETE@1")]
        [DataRow("DELETE Budgets", "DELETE@1")]
        [DataRow("UPDATE b SET Name = 'x' FROM Budgets b JOIN Lines l ON l.BudgetId = b.Id", "UPDATE@1")]
        [DataRow("DELETE b FROM Budgets b JOIN Lines l ON l.BudgetId = b.Id", "DELETE@1")]
        [DataRow("UPDATE t SET a = 1; SELECT 1", "UPDATE@1")]
        [DataRow("IF @x = 1 UPDATE t SET a = 1", "UPDATE@1")]
        [DataRow("WITH c AS (SELECT 1 AS x) UPDATE t SET a = 1", "UPDATE@1")]
        [DataRow("UPDATE t SET a = (SELECT 1 FROM u WHERE u.id = 1)", "UPDATE@1")]   // that WHERE belongs to the subquery
        [DataRow("DELETE FROM t OUTPUT deleted.*", "DELETE@1")]
        public void FlagsStatementsWithoutWhere(string sql, string expected) =>
            CollectionAssert.AreEqual(new[] { expected }, Found(sql));

        [TestMethod]
        public void ReportsEachStatementWithItsLine()
        {
            var sql = "SELECT 1\r\nUPDATE t SET a = 1\r\nDELETE FROM u WHERE id = 1\r\nDELETE FROM v\r\n";
            CollectionAssert.AreEqual(new[] { "UPDATE@2", "DELETE@4" }, Found(sql));
        }

        [TestMethod]
        public void NextStatementEndsTheSearchForWhere()
        {
            // the WHERE belongs to the DELETE, not to the UPDATE before it
            CollectionAssert.AreEqual(new[] { "UPDATE@1" }, Found("UPDATE t SET a = 1\nDELETE FROM u WHERE id = 1"));
            // a second SET is a new statement
            CollectionAssert.AreEqual(new[] { "UPDATE@1" }, Found("UPDATE t SET a = 1\nSET NOCOUNT ON\nSELECT 1 WHERE 1 = 1"));
        }

        [TestMethod]
        public void LineNumbersCanStartAfterTheFirstLine()
        {
            var found = UnsafeStatementChecker.Find("SELECT 1\nDELETE FROM t", firstLine: 10);
            Assert.AreEqual(11, found.Single().Line);
            StringAssert.StartsWith(found.Single().Text, "DELETE FROM t");
        }

        [TestMethod]
        public void PreviewIsTheFirstLineOfTheStatement() =>
            Assert.AreEqual("UPDATE t SET a = 1", UnsafeStatementChecker.Find("UPDATE t SET a = 1\r\n  , b = 2").Single().Text);

        // ---- statements that are fine ----

        [DataTestMethod]
        [DataRow("UPDATE t SET a = 1 WHERE id = 1")]
        [DataRow("DELETE FROM t WHERE id = 1")]
        [DataRow("DELETE FROM t OUTPUT deleted.* WHERE id = 1")]
        [DataRow("UPDATE t SET a = 1 FROM t JOIN u ON u.id = t.id WHERE u.x = 1")]
        [DataRow("DELETE t FROM t JOIN u ON u.id = t.id WHERE u.x = 1")]
        [DataRow("UPDATE t SET a = 1 FROM t WITH (NOLOCK) WHERE id = 1")]
        [DataRow("UPDATE t SET a = 1 WHERE CURRENT OF c")]
        [DataRow("UPDATE t SET a = 1 WHERE 1 = 1")]
        public void HasWhere(string sql) => Assert.AreEqual(0, Found(sql).Length);

        [DataTestMethod]
        [DataRow("CREATE TABLE t (a int, FOREIGN KEY (a) REFERENCES u (a) ON DELETE CASCADE ON UPDATE CASCADE)")]
        [DataRow("ALTER TABLE t ADD CONSTRAINT fk FOREIGN KEY (a) REFERENCES u (a) ON DELETE SET NULL")]
        [DataRow("UPDATE STATISTICS dbo.t")]
        [DataRow("IF UPDATE(a) PRINT 1")]
        [DataRow("DECLARE c CURSOR FOR SELECT a FROM t FOR UPDATE")]
        [DataRow("GRANT SELECT, UPDATE, DELETE ON t TO u")]
        [DataRow("DENY UPDATE ON t TO u")]
        [DataRow("MERGE t USING s ON t.id = s.id WHEN MATCHED THEN UPDATE SET a = 1 WHEN MATCHED AND s.x = 1 THEN DELETE WHEN NOT MATCHED THEN INSERT (a) VALUES (1);")]
        public void NotDataChangingStatements(string sql) => Assert.AreEqual(0, Found(sql).Length);

        [DataTestMethod]
        [DataRow("CREATE PROCEDURE p AS UPDATE t SET a = 1")]
        [DataRow("create or alter procedure p as delete from t")]
        [DataRow("ALTER FUNCTION f() RETURNS int AS BEGIN UPDATE t SET a = 1; RETURN 1 END")]
        [DataRow("CREATE TRIGGER tr ON t AFTER UPDATE AS DELETE FROM u")]
        [DataRow("ALTER VIEW v AS SELECT 1")]
        public void DefinitionsAreStoredNotRun(string sql) => Assert.AreEqual(0, Found(sql).Length);

        [TestMethod]
        public void TheBatchAfterADefinitionIsStillChecked() =>
            CollectionAssert.AreEqual(new[] { "UPDATE@3" }, Found("CREATE PROC p AS UPDATE t SET a = 1\nGO\nUPDATE t SET b = 2"));

        [TestMethod]
        public void GoNeedsALineOfItsOwn()
        {
            // "GO" inside a statement or a label is not a batch separator, so the definition still covers the rest
            Assert.AreEqual(0, Found("CREATE PROC p AS GOTO x; UPDATE t SET a = 1").Length);
            CollectionAssert.AreEqual(new[] { "DELETE@3" }, Found("CREATE PROC p AS SELECT 1\nGO 2\nDELETE FROM t"));
        }

        [DataTestMethod]
        [DataRow("DELETE FROM #t")]
        [DataRow("UPDATE #t SET a = 1")]
        [DataRow("DELETE FROM @t")]
        [DataRow("UPDATE @t SET a = 1")]
        public void ScratchTablesAreIgnored(string sql) => Assert.AreEqual(0, Found(sql).Length);

        [DataTestMethod]
        [DataRow("UPDATE TOP (100) t SET a = 1")]
        [DataRow("DELETE TOP (10) FROM t")]
        public void TopBatchesAreDeliberate(string sql) => Assert.AreEqual(0, Found(sql).Length);

        [DataTestMethod]
        [DataRow("-- UPDATE t SET a = 1")]
        [DataRow("/* DELETE FROM t */ SELECT 1")]
        [DataRow("SELECT 'UPDATE t SET a = 1'")]
        [DataRow("EXEC('DELETE FROM t')")]
        public void CommentsAndStringsAreIgnored(string sql) => Assert.AreEqual(0, Found(sql).Length);

        [TestMethod]
        public void EmptyAndPlainTextAreFine()
        {
            Assert.AreEqual(0, Found("").Length);
            Assert.AreEqual(0, Found("SELECT * FROM t").Length);
        }
    }
}
