using System;
using System.IO;
using System.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using SsmsSqlHelper.Backup;

namespace SsmsSqlHelper.Tests
{
    [TestClass]
    public class BackupStoreTests
    {
        private string _root;

        [TestInitialize]
        public void Setup() => _root = Path.Combine(Path.GetTempPath(), "SsmsSqlHelperTests-" + Guid.NewGuid().ToString("N"));

        [TestCleanup]
        public void Cleanup()
        {
            foreach (var dir in new[] { _root, _root + "-recovered" })
            {
                try
                {
                    if (Directory.Exists(dir))
                        Directory.Delete(dir, true);
                }
                catch (IOException)
                {
                }
            }
        }

        private static BackupMeta Meta(string server = "PJM-Dev-SQL", string database = "master", string filePath = null, DateTime? savedAt = null) =>
            new BackupMeta { Server = server, Database = database, FilePath = filePath, SavedAt = (savedAt ?? DateTime.Now).ToString("yyyy-MM-ddTHH:mm:ss") };

        /// <summary>A session that ran, saved some tabs and died: its store is disposed but the folder is left, as after a crash.</summary>
        private void CrashedSession(Action<BackupStore> work)
        {
            using (var store = new BackupStore(_root))
            {
                store.StartSession();
                work(store);
            }
        }

        // ---- writing and removing ----

        [TestMethod]
        public void SaveWritesTheTextAndItsMeta()
        {
            using (var store = new BackupStore(_root))
            {
                store.Save("tab1", Meta("srv", "db"), "SELECT 1");

                Assert.AreEqual("SELECT 1", File.ReadAllText(Path.Combine(store.SessionDir, "tab1.sql")));
                StringAssert.Contains(File.ReadAllText(Path.Combine(store.SessionDir, "tab1.json")), "\"server\"");
            }
        }

        [TestMethod]
        public void SaveAgainReplacesTheCopyAndLeavesNoTempFiles()
        {
            using (var store = new BackupStore(_root))
            {
                store.Save("tab1", Meta(), "first");
                store.Save("tab1", Meta(), "second, longer");

                Assert.AreEqual("second, longer", File.ReadAllText(Path.Combine(store.SessionDir, "tab1.sql")));
                Assert.IsFalse(Directory.GetFiles(store.SessionDir, "*.tmp").Any());
            }
        }

        [TestMethod]
        public void TextKeepsUnicodeAndLineBreaks()
        {
            const string text = "SELECT N'สวัสดี'\r\nWHERE 1 = 1\r\n";
            using (var store = new BackupStore(_root))
            {
                store.Save("t", Meta(), text);
                Assert.AreEqual(text, File.ReadAllText(Path.Combine(store.SessionDir, "t.sql")));
            }
        }

        [TestMethod]
        public void RemoveDeletesBothFilesAndIsHarmlessTwiceOrBeforeAnySave()
        {
            using (var store = new BackupStore(_root))
            {
                store.Remove("never-saved");          // no session yet
                store.Save("tab1", Meta(), "x");
                store.Remove("tab1");
                store.Remove("tab1");

                Assert.IsFalse(File.Exists(Path.Combine(store.SessionDir, "tab1.sql")));
                Assert.IsFalse(File.Exists(Path.Combine(store.SessionDir, "tab1.json")));
            }
        }

        // ---- which sessions are dead ----

        [TestMethod]
        public void ACrashedSessionLeavesItsTabsToRecover()
        {
            CrashedSession(s =>
            {
                s.Save("a", Meta("srv1", "db1"), "SELECT a");
                s.Save("b", Meta("srv2", "db2"), "SELECT b");
            });

            using (var next = new BackupStore(_root))
            {
                next.StartSession();
                var orphans = next.FindOrphans(TimeSpan.FromDays(14));

                CollectionAssert.AreEquivalent(new[] { "srv1", "srv2" }, orphans.Select(o => o.Meta.Server).ToArray());
                Assert.AreEqual("SELECT a", File.ReadAllText(orphans.Single(o => o.Meta.Server == "srv1").TextPath));
            }
        }

        [TestMethod]
        public void ARunningSessionIsLeftAlone()
        {
            using (var running = new BackupStore(_root))
            {
                running.Save("a", Meta(), "SELECT 1");

                using (var other = new BackupStore(_root))
                {
                    other.StartSession();
                    Assert.AreEqual(0, other.FindOrphans(TimeSpan.FromDays(14)).Count);
                }

                Assert.IsTrue(File.Exists(Path.Combine(running.SessionDir, "a.sql")));
            }
        }

        [TestMethod]
        public void TheCurrentSessionIsNeverAnOrphan()
        {
            using (var store = new BackupStore(_root))
            {
                store.Save("a", Meta(), "SELECT 1");
                Assert.AreEqual(0, store.FindOrphans(TimeSpan.FromDays(14)).Count);
            }
        }

        [TestMethod]
        public void NoBackupFolderAtAllIsFine()
        {
            using (var store = new BackupStore(Path.Combine(_root, "does-not-exist")))
                Assert.AreEqual(0, store.FindOrphans(TimeSpan.FromDays(14)).Count);
        }

        [TestMethod]
        public void TheSessionLockIsHeldOnlyWhileTheSessionRuns()
        {
            var dir = Path.Combine(_root, "s1");
            Assert.IsFalse(SessionLock.IsHeld(dir));
            using (SessionLock.Acquire(dir))
                Assert.IsTrue(SessionLock.IsHeld(dir));
            Assert.IsFalse(SessionLock.IsHeld(dir));
        }

        // ---- what is not worth offering ----

        [TestMethod]
        public void OldCopiesAreDeletedNotOffered()
        {
            CrashedSession(s =>
            {
                s.Save("old", Meta(savedAt: DateTime.Now.AddDays(-30)), "SELECT old");
                s.Save("new", Meta(savedAt: DateTime.Now.AddHours(-1)), "SELECT new");
            });

            using (var next = new BackupStore(_root))
            {
                var orphans = next.FindOrphans(TimeSpan.FromDays(14));

                Assert.AreEqual("new", orphans.Single().Id);
                Assert.IsFalse(Directory.GetFiles(_root, "old.sql", SearchOption.AllDirectories).Any());
            }
        }

        [TestMethod]
        public void ACopyIdenticalToItsSavedFileIsDroppedQuietly()
        {
            Directory.CreateDirectory(_root);
            var saved = Path.Combine(_root, "query.sql");
            File.WriteAllText(saved, "SELECT 1\r\nFROM t\r\n");
            CrashedSession(s =>
            {
                s.Save("same", Meta(filePath: saved), "SELECT 1\nFROM t");      // only the line breaks differ
                s.Save("changed", Meta(filePath: saved), "SELECT 2");
            });

            using (var next = new BackupStore(_root))
            {
                var orphans = next.FindOrphans(TimeSpan.FromDays(14));
                Assert.AreEqual("changed", orphans.Single().Id);
            }
        }

        [TestMethod]
        public void AnEmptiedSessionFolderIsRemoved()
        {
            CrashedSession(s => s.Save("old", Meta(savedAt: DateTime.Now.AddDays(-30)), "x"));
            var sessionDir = Directory.GetDirectories(_root).Single();

            using (var next = new BackupStore(_root))
            {
                next.FindOrphans(TimeSpan.FromDays(14));
                Assert.IsFalse(Directory.Exists(sessionDir));
            }
        }

        // ---- damaged copies ----

        [TestMethod]
        public void TextWithoutMetaIsStillRecoverable()
        {
            CrashedSession(s => s.Save("a", Meta(), "SELECT 1"));
            var json = Directory.GetFiles(_root, "a.json", SearchOption.AllDirectories).Single();
            File.Delete(json);               // died between writing the text and its meta

            using (var next = new BackupStore(_root))
            {
                var entry = next.FindOrphans(TimeSpan.FromDays(14)).Single();
                Assert.IsNull(entry.Meta.Server);
                Assert.AreEqual("SELECT 1", File.ReadAllText(entry.TextPath));
            }
        }

        [TestMethod]
        public void DamagedMetaStillGivesTheText()
        {
            CrashedSession(s => s.Save("a", Meta(), "SELECT 1"));
            File.WriteAllText(Directory.GetFiles(_root, "a.json", SearchOption.AllDirectories).Single(), "{ not json");

            using (var next = new BackupStore(_root))
                Assert.AreEqual("SELECT 1", File.ReadAllText(next.FindOrphans(TimeSpan.FromDays(14)).Single().TextPath));
        }

        [TestMethod]
        public void AMetaWithoutTextIsIgnored()
        {
            CrashedSession(s => s.Save("a", Meta(), "SELECT 1"));
            File.Delete(Directory.GetFiles(_root, "a.sql", SearchOption.AllDirectories).Single());

            using (var next = new BackupStore(_root))
                Assert.AreEqual(0, next.FindOrphans(TimeSpan.FromDays(14)).Count);
        }

        // ---- recovering and discarding ----

        [TestMethod]
        public void RecoverMovesTheTextToItsOwnFile()
        {
            CrashedSession(s => s.Save("a", Meta("PJM-Dev-SQL.priv,15433", "my db", savedAt: new DateTime(2026, 10, 8, 10, 14, 0)), "SELECT 1"));
            var recovered = _root + "-recovered";

            using (var next = new BackupStore(_root))
            {
                var entry = next.FindOrphans(TimeSpan.FromDays(14)).Single();
                var path = BackupStore.Recover(entry, recovered);

                Assert.AreEqual("SELECT 1", File.ReadAllText(path));
                Assert.AreEqual("Recovered_2026-10-08_10-14_PJM-Dev-SQL.priv_15433_my_db.sql", Path.GetFileName(path));
                Assert.AreEqual(0, next.FindOrphans(TimeSpan.FromDays(14)).Count);       // nothing left to recover twice
            }
        }

        [TestMethod]
        public void RecoveredFilesNeverOverwriteEachOther()
        {
            var when = new DateTime(2026, 10, 8, 10, 14, 0);
            CrashedSession(s =>
            {
                s.Save("a", Meta("srv", "db", savedAt: when), "first");
                s.Save("b", Meta("srv", "db", savedAt: when), "second");
            });
            var recovered = _root + "-recovered";

            using (var next = new BackupStore(_root))
            {
                var paths = next.FindOrphans(TimeSpan.FromDays(14)).Select(e => BackupStore.Recover(e, recovered)).ToList();

                Assert.AreEqual(2, paths.Distinct().Count());
                CollectionAssert.AreEquivalent(new[] { "first", "second" }, paths.Select(File.ReadAllText).ToArray());
            }
        }

        [TestMethod]
        public void DiscardRemovesTheCopyAndItsEmptyFolder()
        {
            CrashedSession(s => s.Save("a", Meta(), "SELECT 1"));
            var sessionDir = Directory.GetDirectories(_root).Single();

            using (var next = new BackupStore(_root))
            {
                BackupStore.Discard(next.FindOrphans(TimeSpan.FromDays(14)).Single());
                Assert.IsFalse(Directory.Exists(sessionDir));
            }
        }

        [TestMethod]
        public void FileNamesAreSafeForTheFileSystem()
        {
            var entry = new BackupEntry { SavedAt = new DateTime(2026, 1, 2, 3, 4, 0), Meta = new BackupMeta { Server = "a\\b:c*d", Database = "x/y?" } };
            var name = BackupStore.RecoveredFileName(entry);

            Assert.IsFalse(name.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0, name);
            StringAssert.StartsWith(name, "Recovered_2026-01-02_03-04_");
            StringAssert.EndsWith(name, ".sql");
            Assert.AreEqual("Recovered_2026-01-02_03-04.sql", BackupStore.RecoveredFileName(new BackupEntry { SavedAt = new DateTime(2026, 1, 2, 3, 4, 0), Meta = new BackupMeta() }));
        }

        [TestMethod]
        public void PreviewIsTheFirstLineWithSomethingOnIt()
        {
            CrashedSession(s => s.Save("a", Meta(), "\r\n   \r\n  SELECT * FROM Budgets  \r\nWHERE 1 = 1"));
            using (var next = new BackupStore(_root))
                Assert.AreEqual("SELECT * FROM Budgets", next.FindOrphans(TimeSpan.FromDays(14)).Single().Preview);
        }
    }
}
