using System;
using System.Collections.Generic;

namespace SsmsSqlHelper.Parsing
{
    internal sealed class SqlFunctionSignature
    {
        public SqlFunctionSignature(string name, string syntax, string description, params string[] parameters)
        {
            Name = name;
            Syntax = syntax;
            Description = description;
            Parameters = parameters;
        }

        public string Name { get; }
        public string Syntax { get; }
        public string Description { get; }
        public string[] Parameters { get; }
    }

    internal static class SqlFunctionSignatures
    {
        private static readonly SqlFunctionSignature[] All =
        {
            new SqlFunctionSignature("ISNULL", "ISNULL(check_expression, replacement_value)", "Replace NULL with another value.", "Value to check for NULL", "Value to use when NULL"),
            new SqlFunctionSignature("COALESCE", "COALESCE(expression, expression [, ...])", "Return the first non-NULL expression.", "First expression", "Next expression; more are allowed"),
            new SqlFunctionSignature("NULLIF", "NULLIF(expression, expression)", "Return NULL if the two expressions are equal.", "First expression", "Expression to compare"),
            new SqlFunctionSignature("IIF", "IIF(boolean_expression, true_value, false_value)", "Choose a value based on a condition.", "Condition", "Value when true", "Value when false"),
            new SqlFunctionSignature("CAST", "CAST(expression AS data_type [(length)])", "Convert an expression to a data type.", "Expression to convert", "Target data type after AS"),
            new SqlFunctionSignature("TRY_CAST", "TRY_CAST(expression AS data_type [(length)])", "Convert an expression; return NULL if conversion fails.", "Expression to convert", "Target data type after AS"),
            new SqlFunctionSignature("CONVERT", "CONVERT(data_type [(length)], expression [, style])", "Convert an expression to a data type.", "Target data type", "Expression to convert", "Optional format style"),
            new SqlFunctionSignature("TRY_CONVERT", "TRY_CONVERT(data_type [(length)], expression [, style])", "Convert an expression; return NULL if conversion fails.", "Target data type", "Expression to convert", "Optional format style"),
            new SqlFunctionSignature("COUNT", "COUNT([ALL | DISTINCT] expression | *)", "Count rows or non-NULL values.", "Expression or *"),
            new SqlFunctionSignature("SUM", "SUM([ALL | DISTINCT] expression)", "Sum numeric values.", "Numeric expression"),
            new SqlFunctionSignature("AVG", "AVG([ALL | DISTINCT] expression)", "Average numeric values.", "Numeric expression"),
            new SqlFunctionSignature("DATEADD", "DATEADD(datepart, number, date)", "Add an interval to a date.", "Date part, such as day", "Number of intervals", "Starting date"),
            new SqlFunctionSignature("DATEDIFF", "DATEDIFF(datepart, startdate, enddate)", "Count date boundaries between two dates.", "Date part, such as day", "Start date", "End date"),
            new SqlFunctionSignature("SUBSTRING", "SUBSTRING(expression, start, length)", "Take part of a string or binary value.", "Source expression", "One-based start position", "Number of characters"),
            new SqlFunctionSignature("LEN", "LEN(string_expression)", "Length of a string, excluding trailing spaces.", "String expression"),
            new SqlFunctionSignature("CONCAT", "CONCAT(string_value, string_value [, ...])", "Join values as a string.", "First value", "Next value; more are allowed"),
            new SqlFunctionSignature("ROUND", "ROUND(numeric_expression, length [, function])", "Round a number to a given length.", "Numeric expression", "Decimal places", "Optional truncate flag"),
        };

        public static SqlFunctionSignature Find(string name)
        {
            foreach (var signature in All)
                if (string.Equals(signature.Name, name, StringComparison.OrdinalIgnoreCase))
                    return signature;
            return null;
        }
    }
}
