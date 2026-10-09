using Microsoft.VisualStudio.TestTools.UnitTesting;
using SsmsSqlHelper.Updates;

namespace SsmsSqlHelper.Tests
{
    [TestClass]
    public class ReleaseCheckerTests
    {
        [TestMethod]
        public void InstallerVersionWinsWhenReleaseTagDisagrees()
        {
            const string json = "[{\"tag_name\":\"1.0.0-beta\",\"draft\":false," +
                                "\"assets\":[{\"name\":\"SsmsSqlHelper-0.1.0.zip\"}]}]";
            Assert.AreEqual("0.1.0", ReleaseChecker.LatestInstallerVersion(json).ToString());
        }

        [TestMethod]
        public void BetaInstallerVersionIsRecognized()
        {
            const string json = "[{\"tag_name\":\"v1.0.0\",\"draft\":false," +
                                "\"assets\":[{\"name\":\"SsmsSqlHelper-2.0.0-beta.zip\"}]}]";
            Assert.AreEqual("2.0.0", ReleaseChecker.LatestInstallerVersion(json).ToString());
        }

        [TestMethod]
        public void PicksHighestPublishedInstallerAndIgnoresDrafts()
        {
            const string json = "[{\"tag_name\":\"v0.2.0\",\"draft\":false," +
                                "\"assets\":[{\"name\":\"SsmsSqlHelper-0.2.0.zip\"}]} ," +
                                "{\"tag_name\":\"v0.9.0\",\"draft\":true," +
                                "\"assets\":[{\"name\":\"SsmsSqlHelper-0.9.0.zip\"}]} ," +
                                "{\"tag_name\":\"v0.3.0\",\"draft\":false," +
                                "\"assets\":[{\"name\":\"SsmsSqlHelper-0.3.0.zip\"}]}]";
            Assert.AreEqual("0.3.0", ReleaseChecker.LatestInstallerVersion(json).ToString());
        }

        [TestMethod]
        public void UsesTagWhenReleaseHasNoVersionedInstaller()
        {
            const string json = "[{\"tag_name\":\"v0.4.0\",\"draft\":false," +
                                "\"assets\":[{\"name\":\"SsmsSqlHelper.vsix\"}]}]";
            Assert.AreEqual("0.4.0", ReleaseChecker.LatestInstallerVersion(json).ToString());
        }
    }
}
