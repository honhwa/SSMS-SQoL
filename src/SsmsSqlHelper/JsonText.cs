using System.Text;

namespace SsmsSqlHelper
{
    /// <summary>Small helpers for the hand-written JSON files (snippets.json, settings.json).</summary>
    internal static class JsonText
    {
        /// <summary>A JSON string literal, quotes included.</summary>
        public static string Quote(string value)
        {
            var sb = new StringBuilder(value.Length + 2).Append('"');
            foreach (var c in value)
            {
                switch (c)
                {
                    case '"': sb.Append("\\\""); break;
                    case '\\': sb.Append("\\\\"); break;
                    case '\n': sb.Append("\\n"); break;
                    case '\r': sb.Append("\\r"); break;
                    case '\t': sb.Append("\\t"); break;
                    default:
                        // 0x2028/0x2029 are line separators in JavaScript; spelled as numbers so the source has no invisible characters
                        if (c < 0x20 || c == (char)0x2028 || c == (char)0x2029)
                            sb.Append("\\u").Append(((int)c).ToString("x4"));
                        else
                            sb.Append(c);
                        break;
                }
            }
            return sb.Append('"').ToString();
        }
    }
}
