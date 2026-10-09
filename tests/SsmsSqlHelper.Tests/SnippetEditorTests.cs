using System.Collections.Generic;
using System.Linq;
using System.Runtime.Serialization;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using SsmsSqlHelper.Settings;
using SsmsSqlHelper.Snippets;

namespace SsmsSqlHelper.Tests
{
    [TestClass]
    public class SnippetEditorTests
    {
        private static Snippet S(string shortcut, string body = "SELECT 1", string description = "") =>
            new Snippet { Shortcut = shortcut, Description = description, Body = body };

        // ---- file format ----

        [TestMethod]
        public void WriteThenParseKeepsEverything()
        {
            var original = new List<Snippet>
            {
                S("ssf", "SELECT * FROM $CURSOR$", "SELECT * FROM"),
                S("uu", "UPDATE $CURSOR$\n    SET \nWHERE ", "update"),
                S("q", "SELECT 'it''s' AS \"quoted\", N'\\' -- back\\slash\ttab", "quotes \" and \\"),
                S("thai", "SELECT N'สวัสดี' AS greeting", "ภาษาไทย"),
                S("odd", "line" + (char)0x2028 + "separator" + (char)1 + "control", ""),
            };

            var roundTripped = SnippetFileFormat.Parse(SnippetFileFormat.Write(original));

            Assert.AreEqual(original.Count, roundTripped.Count);
            for (var i = 0; i < original.Count; i++)
            {
                Assert.AreEqual(original[i].Shortcut, roundTripped[i].Shortcut);
                Assert.AreEqual(original[i].Description, roundTripped[i].Description);
                Assert.AreEqual(original[i].Body, roundTripped[i].Body);
            }
        }

        [TestMethod]
        public void WriteNormalisesLineBreaksAndKeepsOneSnippetPerLine()
        {
            var json = SnippetFileFormat.Write(new[] { S("a", "x\r\ny\rz"), S("b") });
            var parsed = SnippetFileFormat.Parse(json);

            Assert.AreEqual("x\ny\nz", parsed[0].Body);
            Assert.AreEqual(2, json.Split('\n').Count(l => l.Contains("\"shortcut\"")));
        }

        [TestMethod]
        public void WriteEmptyListIsValidJson() =>
            Assert.AreEqual(0, SnippetFileFormat.Parse(SnippetFileFormat.Write(new Snippet[0])).Count);

        [TestMethod]
        public void ParseKeepsOrderAndDuplicatesAndTrimsShortcuts()
        {
            var parsed = SnippetFileFormat.Parse(
                "{ \"snippets\": [ { \"shortcut\": \" b \", \"body\": \"1\" }, { \"shortcut\": \"a\", \"body\": \"2\" }, { \"shortcut\": \"a\", \"body\": \"3\" } ] }");
            CollectionAssert.AreEqual(new[] { "b", "a", "a" }, parsed.Select(s => s.Shortcut).ToArray());
        }

        [DataTestMethod]
        [DataRow("not json")]
        [DataRow("{ \"snippets\": [ { \"shortcut\": ")]
        public void ParseThrowsOnBrokenJson(string json) =>
            Assert.ThrowsException<SerializationException>(() => SnippetFileFormat.Parse(json));

        // ---- validation ----

        [TestMethod]
        public void ValidSnippetsHaveNoIssues() =>
            Assert.AreEqual(0, SnippetFileFormat.Validate(new[] { S("ssf"), S("st100"), S("_x"), S("é") }).Count);

        [DataTestMethod]
        [DataRow("")]
        [DataRow("   ")]
        [DataRow("two words")]
        [DataRow("a.b")]
        [DataRow("@v")]
        [DataRow("a-b")]
        public void BadShortcutsAreErrors(string shortcut)
        {
            var issues = SnippetFileFormat.Validate(new[] { S(shortcut) });
            Assert.AreEqual(1, issues.Count);
            Assert.IsTrue(issues[0].IsError);
            Assert.AreEqual(0, issues[0].Index);
        }

        [TestMethod]
        public void DuplicateShortcutsAreErrorsOnTheLaterOnesOnly()
        {
            var issues = SnippetFileFormat.Validate(new[] { S("ssf"), S("x"), S("SSF"), S("ssf") });
            CollectionAssert.AreEqual(new[] { 2, 3 }, issues.Select(i => i.Index).ToArray());
            StringAssert.Contains(issues[0].Message, "snippet 1");
        }

        [TestMethod]
        public void EmptyBodyIsAnErrorAndRepeatedCursorMarkerIsOnlyAWarning()
        {
            var issues = SnippetFileFormat.Validate(new[] { S("a", " "), S("b", "$CURSOR$ and $CURSOR$") });
            Assert.IsTrue(issues.Single(i => i.Index == 0).IsError);
            Assert.IsFalse(issues.Single(i => i.Index == 1).IsError);
        }

        // ---- settings ----

        [TestMethod]
        public void SettingsDefaultToEverythingOn()
        {
            foreach (var settings in new[] { new UserSettings(), UserSettings.Parse("{}") })   // "{}": keys missing in an older file
            {
                Assert.IsTrue(settings.ShowSnippetHints);
                Assert.IsTrue(settings.ShowColumnHints);
                Assert.IsTrue(settings.ShowKeywordHints);
                Assert.IsTrue(settings.HighlightActiveBracket);
                Assert.IsTrue(settings.ColorBracketPairs);
                Assert.IsTrue(settings.AutoAlias);
                Assert.IsTrue(settings.WarnMissingWhere);
            }
        }

        [TestMethod]
        public void OldSettingsFileKeepsItsValuesAndDefaultsTheRest()
        {
            var settings = UserSettings.Parse("{ \"showSnippetHints\": false }");
            Assert.IsFalse(settings.ShowSnippetHints);
            Assert.IsTrue(settings.AutoAlias);
            Assert.IsTrue(settings.WarnMissingWhere);
            Assert.IsTrue(settings.HighlightActiveBracket);
            Assert.IsTrue(settings.ColorBracketPairs);
        }

        [TestMethod]
        public void SettingsRoundTrip()
        {
            var original = new UserSettings { ShowSnippetHints = false, ShowColumnHints = true, ShowKeywordHints = false, HighlightActiveBracket = true, ColorBracketPairs = false, AutoAlias = false, WarnMissingWhere = true };
            var back = UserSettings.Parse(original.ToJson());

            Assert.IsFalse(back.ShowSnippetHints);
            Assert.IsTrue(back.ShowColumnHints);
            Assert.IsFalse(back.ShowKeywordHints);
            Assert.IsTrue(back.HighlightActiveBracket);
            Assert.IsFalse(back.ColorBracketPairs);
            Assert.IsFalse(back.AutoAlias);
            Assert.IsTrue(back.WarnMissingWhere);
        }

        // ---- where a shortcut may be expanded or suggested ----

        [DataTestMethod]
        [DataRow("ss", 0)]
        [DataRow("SELECT 1; ss", 10)]
        [DataRow("  ss", 2)]
        [DataRow("WHERE a = ss", 10)]
        [DataRow("f(ss", 2)]
        public void FindsTheWordBeingTyped(string line, int expectedStart)
        {
            Assert.IsTrue(SnippetExpander.TryFindShortcut(line, allowEmpty: false, out var start));
            Assert.AreEqual(expectedStart, start);
        }

        [DataTestMethod]
        [DataRow("dbo.ss")]
        [DataRow("@ss")]
        [DataRow("#ss")]
        [DataRow("[ss")]
        [DataRow("SELECT 'ss")]
        [DataRow("-- ss")]
        [DataRow("")]
        [DataRow("SELECT ")]
        public void RejectsPlacesWhereTheWordIsNotAShortcut(string line) =>
            Assert.IsFalse(SnippetExpander.TryFindShortcut(line, allowEmpty: false, out _));

        [TestMethod]
        public void EmptyWordIsAllowedOnlyWhenAsked()
        {
            Assert.IsFalse(SnippetExpander.TryFindShortcut("SELECT ", allowEmpty: false, out _));
            Assert.IsTrue(SnippetExpander.TryFindShortcut("SELECT ", allowEmpty: true, out var start));
            Assert.AreEqual(7, start);
            Assert.IsTrue(SnippetExpander.TryFindShortcut("", allowEmpty: true, out _));
            Assert.IsFalse(SnippetExpander.TryFindShortcut("dbo.", allowEmpty: true, out _));      // after a dot: a name part is expected
            Assert.IsFalse(SnippetExpander.TryFindShortcut("SELECT 'a ", allowEmpty: true, out _)); // inside a string
        }

        [TestMethod]
        public void RenderIndentsLaterLinesAndPlacesTheCaret()
        {
            var r = SnippetExpander.Render(S("uu", "UPDATE $CURSOR$\nSET \n\nWHERE "), "  ", "\r\n");
            Assert.AreEqual("UPDATE \r\n  SET \r\n\r\n  WHERE ", r.Text);
            Assert.AreEqual(7, r.CaretOffset);

            var noMarker = SnippetExpander.Render(S("ob", "ORDER BY"), "", "\r\n");
            Assert.AreEqual(8, noMarker.CaretOffset);
        }
    }
}
