using System.Collections.Generic;
using System.Runtime.Serialization;

namespace SsmsSqlHelper.Snippets
{
    [DataContract]
    internal sealed class Snippet
    {
        [DataMember(Name = "shortcut")]
        public string Shortcut { get; set; }

        [DataMember(Name = "description")]
        public string Description { get; set; }

        /// <summary>Text to insert. "\n" separates lines; $CURSOR$ marks the caret position after expansion.</summary>
        [DataMember(Name = "body")]
        public string Body { get; set; }
    }

    [DataContract]
    internal sealed class SnippetFile
    {
        [DataMember(Name = "snippets")]
        public List<Snippet> Snippets { get; set; }
    }
}
