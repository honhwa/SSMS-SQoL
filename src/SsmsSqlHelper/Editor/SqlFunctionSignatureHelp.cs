using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel.Composition;
using System.Linq;
using Microsoft.VisualStudio.Language.Intellisense;
using Microsoft.VisualStudio.Text;
using Microsoft.VisualStudio.Utilities;
using SsmsSqlHelper.Parsing;

namespace SsmsSqlHelper.Editor
{
    [Export(typeof(ISignatureHelpSourceProvider))]
    [Name("SQL Helper Function Signatures")]
    [ContentType("SQL")]
    [Order(Before = "default")]
    internal sealed class SqlFunctionSignatureSourceProvider : ISignatureHelpSourceProvider
    {
        public ISignatureHelpSource TryCreateSignatureHelpSource(ITextBuffer textBuffer) =>
            new SqlFunctionSignatureSource(textBuffer);
    }

    internal sealed class SqlFunctionSignatureSource : ISignatureHelpSource
    {
        private readonly ITextBuffer _buffer;

        public SqlFunctionSignatureSource(ITextBuffer buffer) => _buffer = buffer;

        public void AugmentSignatureHelpSession(ISignatureHelpSession session, IList<ISignature> signatures)
        {
            var snapshot = _buffer.CurrentSnapshot;
            var caret = session.TextView.Caret.Position.BufferPosition.Position;
            if (!SqlFunctionCallParser.TryFindActive(snapshot.GetText(), caret, out var call))
                return;

            var tracking = snapshot.CreateTrackingSpan(
                new Span(call.NameStart, caret - call.NameStart), SpanTrackingMode.EdgeInclusive);
            signatures.Add(new SqlFunctionSignatureItem(call.Signature, call.ArgumentIndex, tracking));
        }

        public ISignature GetBestMatch(ISignatureHelpSession session) =>
            session.Signatures.OfType<SqlFunctionSignatureItem>().FirstOrDefault();

        public void Dispose() { }
    }

    internal sealed class SqlFunctionSignatureItem : ISignature
    {
        public SqlFunctionSignatureItem(SqlFunctionSignature definition, int argumentIndex, ITrackingSpan span)
        {
            Content = PrettyPrintedContent = definition.Syntax;
            Documentation = definition.Description;
            ApplicableToSpan = span;
            var parameters = new List<IParameter>();
            var search = definition.Syntax.IndexOf('(') + 1;
            for (var i = 0; i < definition.Parameters.Length; i++)
            {
                var name = ParameterName(definition.Name, i);
                var start = definition.Syntax.IndexOf(name, search, StringComparison.OrdinalIgnoreCase);
                if (start < 0)
                    start = definition.Syntax.IndexOf(name, StringComparison.OrdinalIgnoreCase);
                if (start < 0)
                    continue;
                parameters.Add(new SqlFunctionParameter(this, name, definition.Parameters[i], new Span(start, name.Length)));
                search = start + name.Length;
            }
            Parameters = new ReadOnlyCollection<IParameter>(parameters);
            CurrentParameter = parameters.Count == 0 ? null : parameters[Math.Min(argumentIndex, parameters.Count - 1)];
        }

        private static string ParameterName(string function, int index)
        {
            switch (function)
            {
                case "ISNULL": return index == 0 ? "check_expression" : "replacement_value";
                case "COALESCE": return "expression";
                case "NULLIF": return "expression";
                case "IIF": return index == 0 ? "boolean_expression" : index == 1 ? "true_value" : "false_value";
                case "CAST":
                case "TRY_CAST": return index == 0 ? "expression" : "data_type";
                case "CONVERT":
                case "TRY_CONVERT": return index == 0 ? "data_type" : index == 1 ? "expression" : "style";
                case "DATEADD": return index == 0 ? "datepart" : index == 1 ? "number" : "date";
                case "DATEDIFF": return index == 0 ? "datepart" : index == 1 ? "startdate" : "enddate";
                case "SUBSTRING": return index == 0 ? "expression" : index == 1 ? "start" : "length";
                case "LEN": return "string_expression";
                case "CONCAT": return "string_value";
                case "ROUND": return index == 0 ? "numeric_expression" : index == 1 ? "length" : "function";
                default: return "expression";
            }
        }

        public ITrackingSpan ApplicableToSpan { get; }
        public string Content { get; }
        public string Documentation { get; }
        public ReadOnlyCollection<IParameter> Parameters { get; }
        public string PrettyPrintedContent { get; }
        public IParameter CurrentParameter { get; }
        public event EventHandler<CurrentParameterChangedEventArgs> CurrentParameterChanged { add { } remove { } }
    }

    internal sealed class SqlFunctionParameter : IParameter
    {
        public SqlFunctionParameter(ISignature signature, string name, string documentation, Span locus)
        {
            Signature = signature;
            Name = name;
            Documentation = documentation;
            Locus = PrettyPrintedLocus = locus;
        }

        public string Documentation { get; }
        public Span Locus { get; }
        public string Name { get; }
        public ISignature Signature { get; }
        public Span PrettyPrintedLocus { get; }
    }
}
