using System;
using System.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using SsmsSqlHelper.Settings;

namespace SsmsSqlHelper.Tests
{
    [TestClass]
    public class ConnectionColorsTests
    {
        private static ConnectionColorRule R(string match, string color) => new ConnectionColorRule { Match = match, Color = color };

        // ---- matching ----

        [DataTestMethod]
        [DataRow("prod", "PJM-Prod-SQL.priv,15433", "master", true)]     // part of the server name, any case
        [DataRow("PROD", "pjm-prod-sql", "master", true)]
        [DataRow("prod", "PJM-Dev-SQL", "ProdCopy", true)]               // or of the database name
        [DataRow("prod", "PJM-Dev-SQL.priv,15433", "master", false)]
        [DataRow("  prod ", "PJM-Prod-SQL", "master", true)]             // surrounding blanks don't matter
        [DataRow("", "PJM-Prod-SQL", "master", false)]
        [DataRow(null, "PJM-Prod-SQL", "master", false)]
        public void PlainTextIsASubstringMatch(string pattern, string server, string database, bool expected) =>
            Assert.AreEqual(expected, ConnectionColors.Matches(pattern, server, database));

        [DataTestMethod]
        [DataRow("pjm-prod*", "PJM-Prod-SQL", "master", true)]           // wildcards must cover the whole name
        [DataRow("prod*", "PJM-Prod-SQL", "master", false)]
        [DataRow("*prod*", "PJM-Prod-SQL", "master", true)]
        [DataRow("*-SQL", "PJM-Prod-SQL", "master", true)]
        [DataRow("PJM-?ev-SQL", "PJM-Dev-SQL", "master", true)]
        [DataRow("PJM-?ev-SQL", "PJM-Prev-SQL", "master", false)]
        [DataRow("*/master", "anything", "master", true)]                // server/database form
        [DataRow("pjm-dev*/orders", "PJM-Dev-SQL", "Orders", true)]
        [DataRow("pjm-dev*/orders", "PJM-Dev-SQL", "Sales", false)]
        [DataRow("*", "x", "y", true)]
        public void WildcardsMatchTheWholeName(string pattern, string server, string database, bool expected) =>
            Assert.AreEqual(expected, ConnectionColors.Matches(pattern, server, database));

        [TestMethod]
        public void NullNamesDoNotThrow()
        {
            Assert.IsFalse(ConnectionColors.Matches("prod", null, null));
            Assert.IsTrue(ConnectionColors.Matches("*", null, null));
        }

        [TestMethod]
        public void FirstMatchingRuleWins()
        {
            var rules = new[] { R("staging", "Orange"), R("prod", "#B71C1C"), R("sql", "Blue") };
            Assert.AreEqual("#B71C1C", ConnectionColors.Resolve(rules, "PJM-Prod-SQL", "db"));
            Assert.AreEqual("Orange", ConnectionColors.Resolve(rules, "PJM-Staging-Prod", "db"));
            Assert.AreEqual("Blue", ConnectionColors.Resolve(rules, "PJM-Dev-SQL", "db"));
            Assert.IsNull(ConnectionColors.Resolve(rules, "laptop", "db"));
            Assert.IsNull(ConnectionColors.Resolve(null, "PJM-Prod-SQL", "db"));
        }

        [TestMethod]
        public void NullRulesInTheListAreSkipped() =>
            Assert.AreEqual("Red", ConnectionColors.Resolve(new[] { null, R("prod", "Red") }, "prod", "db"));

        // ---- the text the settings window edits ----

        [TestMethod]
        public void ParsesOneRulePerLine()
        {
            var rules = ConnectionColors.Parse("prod = #B71C1C\r\n\r\n// a note\r\n  uat=Orange  \r\npjm-*/orders = #112233\r\n", null, out var problems);

            Assert.AreEqual(0, problems.Count);
            CollectionAssert.AreEqual(new[] { "prod", "uat", "pjm-*/orders" }, rules.Select(r => r.Match).ToArray());
            CollectionAssert.AreEqual(new[] { "#B71C1C", "Orange", "#112233" }, rules.Select(r => r.Color).ToArray());
        }

        [TestMethod]
        public void ReportsEachBadLineWithItsNumber()
        {
            Func<string, bool> valid = c => c.StartsWith("#") || c == "Red";
            var rules = ConnectionColors.Parse("prod = #B71C1C\r\nno equals sign\r\n = Red\r\nuat = notacolor\r\nok = Red", valid, out var problems);

            Assert.AreEqual(2, rules.Count);
            Assert.AreEqual(3, problems.Count);
            StringAssert.StartsWith(problems[0], "Line 2");
            StringAssert.StartsWith(problems[1], "Line 3");
            StringAssert.StartsWith(problems[2], "Line 4");
        }

        [TestMethod]
        public void FormatAndParseRoundTrip()
        {
            var original = new[] { R("prod", "#B71C1C"), R("uat", "Orange") };
            var back = ConnectionColors.Parse(ConnectionColors.Format(original), null, out var problems);

            Assert.AreEqual(0, problems.Count);
            CollectionAssert.AreEqual(original.Select(r => r.Match + r.Color).ToArray(), back.Select(r => r.Match + r.Color).ToArray());
        }

        // ---- stored in settings.json ----

        [TestMethod]
        public void RulesSurviveTheSettingsFile()
        {
            var settings = new UserSettings
            {
                ShowConnectionBanner = false,
                ConnectionColors = new System.Collections.Generic.List<ConnectionColorRule> { R("prod", "#B71C1C"), R("a \"quoted\" name", "Red") },
            };

            var back = UserSettings.Parse(settings.ToJson());

            Assert.IsFalse(back.ShowConnectionBanner);
            Assert.AreEqual(2, back.ConnectionColors.Count);
            Assert.AreEqual("a \"quoted\" name", back.ConnectionColors[1].Match);
            Assert.AreEqual("Red", back.ConnectionColors[1].Color);
        }

        [TestMethod]
        public void NoRulesIsAnEmptyListNotTheDefaults()
        {
            var back = UserSettings.Parse(new UserSettings { ConnectionColors = new System.Collections.Generic.List<ConnectionColorRule>() }.ToJson());
            Assert.AreEqual(0, back.ConnectionColors.Count);
        }

        [TestMethod]
        public void OldSettingsFileGetsTheBannerAndDefaultRules()
        {
            var settings = UserSettings.Parse("{ \"autoAlias\": false }");

            Assert.IsFalse(settings.AutoAlias);
            Assert.IsTrue(settings.ShowConnectionBanner);
            Assert.AreEqual("prod", settings.ConnectionColors.Single().Match);
        }
    }
}
