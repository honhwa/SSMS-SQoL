using Microsoft.VisualStudio.TestTools.UnitTesting;
using SsmsSqlHelper.Parsing;

namespace SsmsSqlHelper.Tests
{
    [TestClass]
    public class SchemaObjectAtCaretTests
    {
        [DataTestMethod]
        [DataRow("SELECT * FROM dbo.Cust|omers", "dbo.Customers")]
        [DataRow("JOIN [sales].[Or|ders] o ON 1=1", "[sales].[Orders]")]
        [DataRow("UPDATE dbo.Cust|omers SET Name = 'x'", "dbo.Customers")]
        [DataRow("SELECT dbo.GetPri|ce(1)", "dbo.GetPrice")]
        [DataRow("dbo.Cust|omers", "dbo.Customers")]
        [DataRow("CREATE VIEW dbo.ActiveCust|omers AS SELECT 1", "dbo.ActiveCustomers")]
        [DataRow("SELECT 'FROM dbo.Cust|omers'", null)]
        [DataRow("-- dbo.Cust|omers", null)]
        [DataRow("SELECT c.Cust|omerId FROM dbo.Customers c", null)]
        public void FindsSchemaObjectReference(string source, string expected)
        {
            var caret = source.IndexOf('|');
            var text = source.Remove(caret, 1);
            Assert.AreEqual(expected, SqlContext.GetSchemaObjectAtCaret(text, caret)?.Text);
        }
    }
}
