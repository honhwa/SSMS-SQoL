using System;
using System.Data;
using System.Threading.Tasks;
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
    internal static class ProcedureAlterEditor
    {
        public static void Open(ActiveConnection connection, ProcedureInfo procedure, UIConnectionInfo ui, IDbConnection live)
        {
            OpenAsync(connection, procedure, ui, live);
        }

        private static async void OpenAsync(ActiveConnection connection, ProcedureInfo procedure, UIConnectionInfo ui, IDbConnection live)
        {
            try
            {
                string definition;
                bool ansiNulls;
                bool quotedIdentifier;
                using (var sql = connection.CreateSqlConnection())
                {
                    await sql.OpenAsync().ConfigureAwait(false);
                    using (var cmd = sql.CreateCommand())
                    {
                        cmd.CommandText = @"SELECT m.definition, m.uses_ansi_nulls, m.uses_quoted_identifier
FROM sys.procedures p
JOIN sys.schemas s ON s.schema_id = p.schema_id
LEFT JOIN sys.sql_modules m ON m.object_id = p.object_id
WHERE s.name = @schema AND p.name = @name";
                        cmd.Parameters.Add(new SqlParameter("@schema", procedure.Schema));
                        cmd.Parameters.Add(new SqlParameter("@name", procedure.Name));
                        using (var reader = await cmd.ExecuteReaderAsync().ConfigureAwait(false))
                        {
                            if (!await reader.ReadAsync().ConfigureAwait(false) || reader.IsDBNull(0))
                            {
                                await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();
                                StatusBar.Show("SQL Helper: definition unavailable for " + procedure + " (encrypted or no VIEW DEFINITION permission)");
                                return;
                            }
                            definition = reader.GetString(0);
                            ansiNulls = reader.GetBoolean(1);
                            quotedIdentifier = reader.GetBoolean(2);
                        }
                    }
                }

                var script = ProcedureAlterScript.Build(connection.Database, definition, ansiNulls, quotedIdentifier);
                await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();
                if (script == null)
                {
                    StatusBar.Show("SQL Helper: could not turn the definition of " + procedure + " into ALTER PROCEDURE");
                    return;
                }
                var factory = ServiceCache.ScriptFactory;
                if (factory == null)
                {
                    StatusBar.Show("SQL Helper: SSMS script factory is unavailable");
                    return;
                }
                var dte = Package.GetGlobalService(typeof(DTE)) as DTE;
                var previousDocumentName = dte?.ActiveDocument?.Name;
                factory.CreateNewBlankScript(ScriptType.Sql, ui, live);
                if (dte?.ActiveDocument == null || dte.ActiveDocument.Name == previousDocumentName)
                {
                    StatusBar.Show("SQL Helper: SSMS did not activate a new query tab");
                    return;
                }
                var selection = dte?.ActiveDocument?.Selection as TextSelection;
                if (selection == null)
                {
                    StatusBar.Show("SQL Helper: new SQL editor could not be populated");
                    return;
                }
                selection.Insert(script);
                StatusBar.Show("SQL Helper: opened ALTER script for " + procedure);
            }
            catch (Exception ex)
            {
                Log.Error("Could not open ALTER script for " + procedure, ex);
                await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();
                StatusBar.Show("SQL Helper: could not open ALTER script for " + procedure + "; see SQL Helper Output or log.txt");
            }
        }
    }
}
