using System;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using SsmsSqlHelper.Metadata;

namespace SsmsSqlHelper.Tests
{
    [TestClass]
    public class MetadataTests
    {
        private static DbMetadata CreateMetadata() => new DbMetadata("srv", "db", new[]
        {
            new TableInfo("dbo", "Customer", false),
            new TableInfo("sales", "Customer", false),
            new TableInfo("sales", "Invoice", false),
            new TableInfo("hr", "Order Line", false),
            new TableInfo("a", "Dup", false),
            new TableInfo("b", "Dup", false),
        }, DateTime.Now, TimeSpan.Zero);

        [DataTestMethod]
        [DataRow("Customer", "dbo.Customer")]
        [DataRow("customer", "dbo.Customer")]
        [DataRow("sales.Customer", "sales.Customer")]
        [DataRow("[sales].[Customer]", "sales.Customer")]
        [DataRow("mydb.sales.Customer", "sales.Customer")]
        [DataRow("Invoice", "sales.Invoice")]          // unique outside dbo
        [DataRow("[hr].[Order Line]", "hr.Order Line")]
        [DataRow("mydb..Customer", "dbo.Customer")]    // empty schema means default
        public void FindTableResolves(string input, string expected) =>
            Assert.AreEqual(expected, CreateMetadata().FindTable(input)?.ToString());

        [DataTestMethod]
        [DataRow("Dup")]         // ambiguous across schemas
        [DataRow("Nope")]
        [DataRow("dbo.Invoice")] // wrong schema
        [DataRow("")]
        public void FindTableReturnsNull(string input) => Assert.IsNull(CreateMetadata().FindTable(input));

        [DataTestMethod]
        [DataRow("Customer", "Customer")]
        [DataRow("Order Line", "[Order Line]")]
        [DataRow("Order", "[Order]")]
        [DataRow("1st", "[1st]")]
        [DataRow("a]b", "[a]]b]")]
        public void QuoteOnlyWhenNeeded(string name, string expected) => Assert.AreEqual(expected, SqlIdentifier.Quote(name));

        [TestMethod]
        public void SplitHandlesEscapedBrackets() =>
            CollectionAssert.AreEqual(new[] { "dbo", "a]b" }, (System.Collections.ICollection)SqlIdentifier.Split("dbo.[a]]b]"));

        [DataTestMethod]
        [DataRow("nvarchar", 200, 0, 0, "nvarchar(100)")]
        [DataRow("nvarchar", -1, 0, 0, "nvarchar(MAX)")]
        [DataRow("varchar", 50, 0, 0, "varchar(50)")]
        [DataRow("decimal", 9, 18, 2, "decimal(18,2)")]
        [DataRow("datetime2", 8, 27, 7, "datetime2")]
        [DataRow("datetime2", 6, 19, 0, "datetime2(0)")]
        [DataRow("int", 4, 10, 0, "int")]
        public void DisplayType(string type, int maxLength, int precision, int scale, string expected) =>
            Assert.AreEqual(expected, new ColumnInfo { TypeName = type, MaxLength = maxLength, Precision = precision, Scale = scale }.DisplayType);

        [TestMethod]
        public void GeneratedColumns()
        {
            Assert.IsTrue(new ColumnInfo { TypeName = "int", IsIdentity = true }.IsGenerated);
            Assert.IsTrue(new ColumnInfo { TypeName = "int", IsComputed = true }.IsGenerated);
            Assert.IsTrue(new ColumnInfo { TypeName = "timestamp" }.IsGenerated);
            Assert.IsFalse(new ColumnInfo { TypeName = "int", HasDefault = true }.IsGenerated);
        }
    }
}
