using System;
using System.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using SsmsSqlHelper.Snippets;

namespace SsmsSqlHelper.Tests
{
    [TestClass]
    public class SnippetTemplateTests
    {
        private const string NL = "\r\n";

        private static RenderedSnippet Render(string body, string indent = "", Func<string, string> variable = null) =>
            SnippetTemplate.Render(body, indent, NL, variable);

        [TestMethod]
        public void PlainTextHasNoStopsAndEndsAtTheEnd()
        {
            var r = Render("SELECT * FROM ");
            Assert.AreEqual("SELECT * FROM ", r.Text);
            Assert.AreEqual(0, r.Stops.Count);
            Assert.AreEqual(14, r.FinalOffset);
            Assert.AreEqual(14, r.InitialCaretOffset);
        }

        [DataTestMethod]
        [DataRow("SELECT $CURSOR$ FROM")]
        [DataRow("SELECT $0 FROM")]
        [DataRow("SELECT ${0} FROM")]
        public void FinalMarkerPositionsTheCaret(string body)
        {
            var r = Render(body);
            Assert.AreEqual("SELECT  FROM", r.Text);
            Assert.AreEqual(7, r.FinalOffset);
            Assert.AreEqual(7, r.InitialCaretOffset);
        }

        [TestMethod]
        public void TabStopsKeepTheirTextAndGoFirstInNumberOrder()
        {
            var r = Render("UPDATE ${2:table} SET ${1:col} = ${3} WHERE $0");
            Assert.AreEqual("UPDATE table SET col =  WHERE ", r.Text);
            CollectionAssert.AreEqual(new[] { 1, 2, 3 }, r.Stops.Select(s => s.Index).ToArray());
            Assert.AreEqual("col", r.Text.Substring(r.Stops[0].Start, r.Stops[0].Length));
            Assert.AreEqual("table", r.Text.Substring(r.Stops[1].Start, r.Stops[1].Length));
            Assert.AreEqual(0, r.Stops[2].Length);
            Assert.AreEqual(r.Text.Length, r.FinalOffset);
            Assert.AreEqual(r.Stops[0].Start, r.InitialCaretOffset);
        }

        [TestMethod]
        public void BareNumberIsAnEmptyStop()
        {
            var r = Render("a $1 b $2 c");
            Assert.AreEqual("a  b  c", r.Text);
            CollectionAssert.AreEqual(new[] { 2, 5 }, r.Stops.Select(s => s.Start).ToArray());
        }

        [TestMethod]
        public void SameNumberIsMirroredInTextOrder()
        {
            var r = Render("${1:x} = ${2:y} / ${1:x}");
            CollectionAssert.AreEqual(new[] { 1, 1, 2 }, r.Stops.Select(s => s.Index).ToArray());
            Assert.IsTrue(r.Stops[0].Start < r.Stops[1].Start);
        }

        [TestMethod]
        public void LaterLinesAreIndentedAndStopsLandAfterTheIndent()
        {
            var r = Render("BEGIN\n${1:body}\nEND", "  ");
            Assert.AreEqual("BEGIN" + NL + "  body" + NL + "  END", r.Text);
            Assert.AreEqual(9, r.Stops[0].Start);                               // after "BEGIN\r\n  "
        }

        [TestMethod]
        public void EmptyLinesAreNotIndentedButAStopOnThemIs()
        {
            var r = Render("a\n\n$1\nb", "  ");
            Assert.AreEqual("a" + NL + NL + "  " + NL + "  b", r.Text);
            Assert.AreEqual(1 + 2 + 2 + 2, r.Stops[0].Start);                    // two line breaks, then the indentation, then the stop
        }

        [TestMethod]
        public void DefaultTextCanSpanLines()
        {
            var r = Render("${1:one\ntwo}", "  ");
            Assert.AreEqual("one" + NL + "  two", r.Text);
            Assert.AreEqual(r.Text.Length, r.Stops[0].Length);
        }

        [TestMethod]
        public void VariablesAreFilledIn()
        {
            var r = Render("-- $USER$ on $DATE$ ($UNKNOWN$)", "", name => name == "USER" ? "jirakit" : name == "DATE" ? "2026-10-08" : null);
            Assert.AreEqual("-- jirakit on 2026-10-08 ($UNKNOWN$)", r.Text);
        }

        [TestMethod]
        public void MissingResolverMeansEmptyVariables() =>
            Assert.AreEqual("a  b", Render("a $DATE$ b").Text);

        [TestMethod]
        public void SelectedTextKeepsItsOwnIndentationAndLineBreaks()
        {
            var r = Render("BEGIN TRAN\n$SELECTED$\nROLLBACK", "  ", n => n == "SELECTED" ? "    SELECT 1\r\n    SELECT 2" : null);
            Assert.AreEqual("BEGIN TRAN" + NL + "    SELECT 1" + NL + "    SELECT 2" + NL + "  ROLLBACK", r.Text);
        }

        [TestMethod]
        public void SelectedTextCanBeEmpty() =>
            Assert.AreEqual("BEGIN" + NL + NL + "END", Render("BEGIN\n$SELECTED$\nEND", "", n => "").Text.Replace(NL + NL + NL, NL + NL));

        [DataTestMethod]
        [DataRow("SELECT $$100", "SELECT $100")]
        [DataRow("SELECT $action", "SELECT $action")]
        [DataRow("SELECT 5 $ 3", "SELECT 5 $ 3")]
        [DataRow("${1:unclosed", "${1:unclosed")]
        [DataRow("$NOPE$ x", "$NOPE$ x")]
        public void OtherDollarsStayAsWritten(string body, string expected) => Assert.AreEqual(expected, Render(body).Text);

        [TestMethod]
        public void OnlyTheFirstFinalMarkerCounts()
        {
            var r = Render("a $0 b $CURSOR$ c");
            Assert.AreEqual(2, r.FinalOffset);
            Assert.AreEqual(2, SnippetTemplate.CountFinalMarkers("a $0 b $CURSOR$ c"));
            Assert.AreEqual(0, SnippetTemplate.CountFinalMarkers("a $1 b"));
        }

        [TestMethod]
        public void ExpanderCarriesStopsThrough()
        {
            var snippet = new Snippet { Shortcut = "uu", Body = "UPDATE ${1:t}\nSET ${2:c} = $0" };
            var e = SnippetExpander.TryExpand("  uu", "  ", NL, s => snippet);

            Assert.AreEqual(2, e.ReplaceStart);
            Assert.AreEqual(2, e.Stops.Count);
            Assert.AreEqual(e.Stops[0].Start, e.CaretOffset);                    // caret starts on the first stop
            Assert.AreEqual(e.Text.Length, e.FinalOffset);
        }
    }
}
