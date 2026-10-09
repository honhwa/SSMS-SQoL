using Microsoft.VisualStudio.TestTools.UnitTesting;
using SsmsSqlHelper.Ssms;

namespace SsmsSqlHelper.Tests
{
    [TestClass]
    public class ServerNameMatcherTests
    {
        [DataTestMethod]
        [DataRow("PJM-Dev-SQL.priv,15433", "PJM-DEV-SQL", true)]
        [DataRow("tcp:server.example.com,1433", "SERVER", true)]
        [DataRow("server.example.com\\instance", "server\\instance", true)]
        [DataRow("server.example.com\\instance", "server\\other", false)]
        [DataRow("server-one.example.com", "server-two", false)]
        public void MatchesServerAliases(string queryServer, string explorerServer, bool expected)
        {
            Assert.AreEqual(expected, ServerNameMatcher.Matches(queryServer, explorerServer));
        }
    }
}
