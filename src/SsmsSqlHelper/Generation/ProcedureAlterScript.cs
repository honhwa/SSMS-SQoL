using System;
using System.Linq;
using System.Text;
using SsmsSqlHelper.Parsing;

namespace SsmsSqlHelper.Generation
{
    internal static class ProcedureAlterScript
    {
        public static string Build(string database, string definition, bool ansiNulls, bool quotedIdentifier)
            => BuildModule(database, definition, ansiNulls, quotedIdentifier, "PROCEDURE");

        public static string BuildModule(string database, string definition, bool ansiNulls, bool quotedIdentifier, string kind)
        {
            if (string.IsNullOrWhiteSpace(definition))
                return null;

            var tokens = SqlTokenizer.Tokenize(definition).Where(t => !t.IsTrivia).ToList();
            if (tokens.Count < 2)
                return null;

            var first = tokens[0];
            var procedureIndex = 1;
            var editEnd = first.End;
            if (first.IsKeyword("CREATE") && tokens.Count > 3 &&
                tokens[1].IsKeyword("OR") && tokens[2].IsKeyword("ALTER"))
            {
                procedureIndex = 3;
                editEnd = tokens[2].End;
            }
            else if (!first.IsKeyword("CREATE") && !first.IsKeyword("ALTER"))
                return null;

            if (tokens.Count <= procedureIndex ||
                !(tokens[procedureIndex].IsKeyword(kind) ||
                  (kind == "PROCEDURE" && tokens[procedureIndex].IsKeyword("PROC"))))
                return null;

            var altered = definition.Substring(0, first.Start) + "ALTER" + definition.Substring(editEnd);
            var script = new StringBuilder();
            script.Append("USE [").Append(database.Replace("]", "]]")).Append("]\r\nGO\r\n");
            script.Append("SET ANSI_NULLS ").Append(ansiNulls ? "ON" : "OFF").Append("\r\nGO\r\n");
            script.Append("SET QUOTED_IDENTIFIER ").Append(quotedIdentifier ? "ON" : "OFF").Append("\r\nGO\r\n");
            script.Append(altered.TrimEnd()).Append("\r\nGO\r\n");
            return script.ToString();
        }
    }
}
