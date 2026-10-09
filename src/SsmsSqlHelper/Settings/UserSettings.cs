using System.Collections.Generic;
using System.IO;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;
using System.Text;

namespace SsmsSqlHelper.Settings
{
    [DataContract]
    internal sealed class UserSettings
    {
        public UserSettings()
        {
            SetDefaults(default(StreamingContext));
            ConnectionColors = Settings.ConnectionColors.Defaults();
        }

        /// <summary>Show a list of matching snippet shortcuts while typing (Ctrl+Space shows all of them).</summary>
        [DataMember(Name = "showSnippetHints")]
        public bool ShowSnippetHints { get; set; }

        /// <summary>Offer column names while typing in a query (after <c>alias.</c> and where an expression starts).</summary>
        [DataMember(Name = "showColumnHints")]
        public bool ShowColumnHints { get; set; }

        /// <summary>Offer SQL keywords (WHERE, GROUP BY, JOIN, ...) while typing a word that starts one; Ctrl+Space always offers them.</summary>
        [DataMember(Name = "showKeywordHints")]
        public bool ShowKeywordHints { get; set; }

        /// <summary>Add an alias (and a join condition after JOIN) when a table is picked or Tab is pressed after its name.</summary>
        [DataMember(Name = "autoAlias")]
        public bool AutoAlias { get; set; }

        /// <summary>Ask before running a script that contains UPDATE or DELETE without WHERE.</summary>
        [DataMember(Name = "warnMissingWhere")]
        public bool WarnMissingWhere { get; set; }

        /// <summary>Keep a copy of every unsaved query tab on disk, so a crash or End Task does not lose it.</summary>
        [DataMember(Name = "backupUnsavedTabs")]
        public bool BackupUnsavedTabs { get; set; }

        /// <summary>A banner above every query window naming the server and database it is connected to.</summary>
        [DataMember(Name = "showConnectionBanner")]
        public bool ShowConnectionBanner { get; set; }

        /// <summary>Colours for the banner by server or database name; the first rule that matches wins, no match is the neutral banner.</summary>
        [DataMember(Name = "connectionColors")]
        public List<ConnectionColorRule> ConnectionColors { get; set; }

        // DataContractJsonSerializer skips constructors, so settings missing from an older file get their defaults here
        [OnDeserializing]
        private void SetDefaults(StreamingContext context)
        {
            ShowSnippetHints = true;
            ShowColumnHints = true;
            ShowKeywordHints = true;
            AutoAlias = true;
            WarnMissingWhere = true;
            BackupUnsavedTabs = true;
            ShowConnectionBanner = true;
            ConnectionColors = Settings.ConnectionColors.LegacyDefaults();
        }

        public static UserSettings Parse(string json)
        {
            var serializer = new DataContractJsonSerializer(typeof(UserSettings));
            using (var stream = new MemoryStream(Encoding.UTF8.GetBytes(json)))
                return (UserSettings)serializer.ReadObject(stream);
        }

        public string ToJson()
        {
            var nl = System.Environment.NewLine;
            return "{" + nl +
                   "  \"showSnippetHints\": " + Flag(ShowSnippetHints) + "," + nl +
                   "  \"showColumnHints\": " + Flag(ShowColumnHints) + "," + nl +
                   "  \"showKeywordHints\": " + Flag(ShowKeywordHints) + "," + nl +
                   "  \"autoAlias\": " + Flag(AutoAlias) + "," + nl +
                   "  \"warnMissingWhere\": " + Flag(WarnMissingWhere) + "," + nl +
                   "  \"backupUnsavedTabs\": " + Flag(BackupUnsavedTabs) + "," + nl +
                   "  \"showConnectionBanner\": " + Flag(ShowConnectionBanner) + "," + nl +
                   "  \"connectionColors\": " + Settings.ConnectionColors.ToJson(ConnectionColors, "  ") + nl +
                   "}" + nl;
        }

        private static string Flag(bool value) => value ? "true" : "false";
    }
}
