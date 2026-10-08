using System;
using System.IO;
using System.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using SsmsSqlHelper.Snippets;

namespace SsmsSqlHelper.Tests
{
    /// <summary>The snippets that ship with the extension must be valid and usable as written.</summary>
    [TestClass]
    public class DefaultSnippetsTests
    {
        private static System.Collections.Generic.List<Snippet> Defaults() =>
            SnippetFileFormat.Parse(File.ReadAllText(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "DefaultSnippets.json")));

        [TestMethod]
        public void FileParsesAndHasNoProblems()
        {
            var snippets = Defaults();
            Assert.IsTrue(snippets.Count >= 14);
            Assert.AreEqual(0, SnippetFileFormat.Validate(snippets).Count, "errors or warnings in DefaultSnippets.json");
        }

        [TestMethod]
        public void EveryDefaultRendersWithAllVariablesAvailable()
        {
            foreach (var snippet in Defaults())
            {
                var rendered = SnippetTemplate.Render(snippet.Body, "    ", "\r\n", name => "<" + name + ">");
                Assert.IsTrue(rendered.Text.Length > 0, snippet.Shortcut);
                Assert.IsTrue(rendered.FinalOffset >= 0 && rendered.FinalOffset <= rendered.Text.Length, snippet.Shortcut);
                foreach (var stop in rendered.Stops)
                    Assert.IsTrue(stop.Start >= 0 && stop.Start + stop.Length <= rendered.Text.Length, snippet.Shortcut);
            }
        }

        [TestMethod]
        public void CreateTableLeadsThroughThreePlaceholders()
        {
            var ct = Defaults().Single(s => s.Shortcut == "ct");
            var rendered = SnippetTemplate.Render(ct.Body, "", "\r\n");

            CollectionAssert.AreEqual(new[] { 1, 2, 3 }, rendered.Stops.Select(s => s.Index).ToArray());
            Assert.AreEqual("dbo.TableName", rendered.Text.Substring(rendered.Stops[0].Start, rendered.Stops[0].Length));
            Assert.AreEqual("nvarchar(100)", rendered.Text.Substring(rendered.Stops[2].Start, rendered.Stops[2].Length));
            Assert.IsTrue(rendered.Text.Substring(rendered.FinalOffset).StartsWith("\r\n)"));      // the caret ends before the closing bracket
        }

        [TestMethod]
        public void SurroundSnippetsKeepTheSelectionBetweenTheirLines()
        {
            var bt = Defaults().Single(s => s.Shortcut == "bt");
            var rendered = SnippetTemplate.Render(bt.Body, "", "\r\n", name => name == "SELECTED" ? "UPDATE t SET a = 1" : "");

            StringAssert.Contains(rendered.Text, "BEGIN TRANSACTION\r\n\r\nUPDATE t SET a = 1\r\n\r\nROLLBACK TRANSACTION");
        }

        [TestMethod]
        public void SurroundSnippetsAlsoWorkTypedAsPlainShortcuts()
        {
            // nothing selected: the same body, with the caret where the selection would have gone
            var tc = Defaults().Single(s => s.Shortcut == "tc");
            var rendered = SnippetTemplate.Render(tc.Body, "", "\r\n", name => "");
            Assert.AreEqual("BEGIN TRY\r\n".Length, rendered.FinalOffset);
        }
    }
}
