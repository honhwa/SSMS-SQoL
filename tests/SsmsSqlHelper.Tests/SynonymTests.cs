using System;
using System.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using SsmsSqlHelper.Generation;
using SsmsSqlHelper.Metadata;
using SsmsSqlHelper.Parsing;

namespace SsmsSqlHelper.Tests
{
    [TestClass]
    public class SynonymTests
    {
        [DataTestMethod]
        [DataRow("[dbo].[Workers]", "", "dbo", "Workers", true)]
        [DataRow("[Site].[dbo].[Workers]", "[Site].", "dbo", "Workers", true)]
        [DataRow("[Central DB].[hr].[Workers]", "[Central DB].", "hr", "Workers", false)]
        [DataRow("[server,15433].[Central DB].[hr].[Workers]", "[server,15433].[Central DB].", "hr", "Workers", false)]
        [DataRow("[remote]..[dbo].[Workers]", "[remote]..", "dbo", "Workers", false)]
        [DataRow("[db]]name].[schema.with.dot].[Workers]", "[db]]name].", "schema.with.dot", "Workers", false)]
        public void ResolvesQuotedLocalCrossDatabaseAndLinkedTargets(string target, string catalog, string schema, string name, bool local)
        {
            var synonym = new SynonymInfo("dbo", "SiteWorkers", target);
            Assert.AreEqual(catalog, synonym.CatalogPrefix);
            Assert.AreEqual(schema, synonym.TargetSchema);
            Assert.AreEqual(name, synonym.TargetName);
            Assert.AreEqual(local, synonym.IsLocal("Site"));
        }

        private static TableInfo Synonym()
        {
            var target = new TableInfo("hr", "Workers", false);
            target.Columns.Add(new ColumnInfo { Name = "Id", Ordinal = 1, TypeName = "int", IsIdentity = true, IsPrimaryKey = true });
            target.Columns.Add(new ColumnInfo { Name = "Name", Ordinal = 2, TypeName = "nvarchar", MaxLength = 200, IsNullable = true });
            return new SynonymInfo("dbo", "SiteWorkers", "[Central].[hr].[Workers]").Bind(target);
        }

        private static DbMetadata Metadata() => new DbMetadata("server", "Site", new[] { Synonym() }, DateTime.Now, TimeSpan.Zero);

        [TestMethod]
        public void ColumnsAreSuggestedThroughTheLocalSynonymAlias()
        {
            const string sql = "SELECT sw. FROM dbo.SiteWorkers sw";
            Assert.IsTrue(SqlContext.TryGetColumnContext(sql, sql.IndexOf(". FROM", StringComparison.Ordinal) + 1, out var context));
            var columns = ColumnSuggester.Suggest(context, Metadata());
            CollectionAssert.AreEqual(new[] { "Id", "Name" }, columns.Select(c => c.InsertText).ToArray());
            Assert.AreEqual("nvarchar(100)", columns[1].Column.DisplayType);
            Assert.IsTrue(columns[0].Column.IsPrimaryKey);
            Assert.IsTrue(columns[0].Table.IsSynonym);
            Assert.AreEqual("dbo.SiteWorkers", columns[0].Table.QualifiedName);
        }

        [TestMethod]
        public void SelectStarUsesSynonymColumnsAndAlias()
        {
            const string sql = "SELECT * FROM dbo.SiteWorkers sw";
            var context = SqlContext.GetTabContext(sql, sql.IndexOf('*') + 1);
            var columns = ContextExpander.GetStarColumns(context, Metadata());
            CollectionAssert.AreEqual(new[] { "sw.Id", "sw.Name" }, columns.Select(c => c.SqlName).ToArray());
        }

        [TestMethod]
        public void InsertThroughSynonymSkipsGeneratedColumns()
        {
            const string sql = "INSERT INTO dbo.SiteWorkers";
            var edit = ContextExpander.TryExpand(sql, sql.Length, "\n", Metadata());
            Assert.IsNotNull(edit);
            StringAssert.Contains(edit.NewText, "Name");
            Assert.IsFalse(edit.NewText.Contains("Id"));
        }

        [TestMethod]
        public void ViewSynonymRetainsTargetAndLocalName()
        {
            var synonym = new SynonymInfo("report", "SiteReport", "[Central].[dbo].[Report]")
                .Bind(new TableInfo("dbo", "Report", true));
            Assert.IsTrue(synonym.IsView);
            Assert.IsTrue(synonym.IsSynonym);
            Assert.AreEqual("report.SiteReport", synonym.QualifiedName);
            Assert.AreEqual("[Central].[dbo].[Report]", synonym.SynonymTarget);
        }
    }
}
