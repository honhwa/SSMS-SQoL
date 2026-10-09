using System;
using System.Data;
using EnvDTE;
using Microsoft.Data.SqlClient;
using Microsoft.SqlServer.Management.Smo.RegSvrEnum;
using Microsoft.SqlServer.Management.UI.VSIntegration;
using Microsoft.SqlServer.Management.UI.VSIntegration.Editors;
using Microsoft.VisualStudio.Shell;
using SsmsSqlHelper.Diagnostics;
using SsmsSqlHelper.Generation;
using SsmsSqlHelper.Metadata;
using StatusBar = SsmsSqlHelper.Diagnostics.StatusBar;

namespace SsmsSqlHelper.Ssms
{
    internal static class SchemaObjectEditor
    {
        public static void Open(ActiveConnection connection, SchemaObjectInfo schemaObject, UIConnectionInfo ui, IDbConnection live)
            => OpenAsync(connection, schemaObject, ui, live);

        private static async void OpenAsync(ActiveConnection connection, SchemaObjectInfo schemaObject, UIConnectionInfo ui, IDbConnection live)
        {
            try
            {
                if (!schemaObject.IsSqlModule)
                {
                    StatusBar.Show("SQL Helper: ALTER script is unavailable for " + schemaObject);
                    return;
                }
                string definition;
                bool ansiNulls;
                bool quotedIdentifier;
                using (var sql = connection.CreateSqlConnection())
                {
                    await sql.OpenAsync().ConfigureAwait(false);
                    using (var cmd = sql.CreateCommand())
                    {
                        cmd.CommandText = @"SELECT m.definition, m.uses_ansi_nulls, m.uses_quoted_identifier
FROM sys.objects o
JOIN sys.schemas s ON s.schema_id = o.schema_id
LEFT JOIN sys.sql_modules m ON m.object_id = o.object_id
WHERE s.name = @schema AND o.name = @name AND o.type = @type";
                        cmd.Parameters.Add(new SqlParameter("@schema", schemaObject.Schema));
                        cmd.Parameters.Add(new SqlParameter("@name", schemaObject.Name));
                        cmd.Parameters.Add(new SqlParameter("@type", schemaObject.Type));
                        using (var reader = await cmd.ExecuteReaderAsync().ConfigureAwait(false))
                        {
                            if (!await reader.ReadAsync().ConfigureAwait(false) || reader.IsDBNull(0))
                            {
                                await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();
                                StatusBar.Show("SQL Helper: definition unavailable for " + schemaObject);
                                return;
                            }
                            definition = reader.GetString(0);
                            ansiNulls = reader.GetBoolean(1);
                            quotedIdentifier = reader.GetBoolean(2);
                        }
                    }
                }
                var script = ProcedureAlterScript.BuildModule(connection.Database, definition, ansiNulls,
                    quotedIdentifier, schemaObject.ScriptType);
                await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();
                if (script == null)
                {
                    StatusBar.Show("SQL Helper: could not build ALTER " + schemaObject.ScriptType + " for " + schemaObject);
                    return;
                }
                var factory = ServiceCache.ScriptFactory;
                var dte = Package.GetGlobalService(typeof(DTE)) as DTE;
                if (factory == null || dte == null)
                {
                    StatusBar.Show("SQL Helper: new query editor is unavailable");
                    return;
                }
                var previousName = dte.ActiveDocument?.Name;
                factory.CreateNewBlankScript(ScriptType.Sql, ui, live);
                if (dte.ActiveDocument == null || dte.ActiveDocument.Name == previousName ||
                    !(dte.ActiveDocument.Selection is TextSelection selection))
                {
                    StatusBar.Show("SQL Helper: new query tab could not be populated");
                    return;
                }
                selection.Insert(script);
                StatusBar.Show("SQL Helper: opened ALTER " + schemaObject.ScriptType + " for " + schemaObject);
                Log.Info("Opened ALTER " + schemaObject.ScriptType + " for " + schemaObject);
            }
            catch (Exception ex)
            {
                Log.Error("Could not open ALTER script for " + schemaObject, ex);
                await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();
                StatusBar.Show("SQL Helper: could not open ALTER script for " + schemaObject);
            }
        }
    }
}
