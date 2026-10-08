using System;
using System.Data;
using System.Reflection;
using System.Security;
using Microsoft.Data.SqlClient;
using Microsoft.SqlServer.Management.Smo.RegSvrEnum;
using Microsoft.SqlServer.Management.UI.VSIntegration;
using Microsoft.VisualStudio;
using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Shell.Interop;
using SsmsSqlHelper.Diagnostics;

namespace SsmsSqlHelper.Ssms
{
    /// <summary>
    /// The only place that touches undocumented SSMS internals. If an SSMS update breaks
    /// the extension, the fix should be contained to this class.
    /// </summary>
    internal static class SsmsConnectionAdapter
    {
        // UIConnectionInfo.AuthenticationType: 0 = Windows, 1 = SQL Server
        private const int SqlServerAuthentication = 1;

        // ScriptAndResultsEditorControl keeps the query window's live connection here (SSMS 22)
        private const string LiveConnectionField = "m_connection";

        private static bool _loggedDocViewProblem;

        public static ActiveConnection GetActiveConnection()
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            try
            {
                var ui = ServiceCache.ScriptFactory?.CurrentlyActiveWndConnectionInfo?.UIConnectionInfo;
                if (ui == null)
                    return null;

                var live = GetLiveConnection();
                var database = !string.IsNullOrEmpty(live?.Database) ? live.Database : ui.AdvancedOptions?["DATABASE"];
                var isSqlAuth = ui.AuthenticationType == SqlServerAuthentication;

                return new ActiveConnection(
                    ui.ServerName,
                    database,
                    ui.UserName,
                    BuildConnectionString(live?.ConnectionString, ui, database),
                    isSqlAuth ? GetPassword(ui) : null);
            }
            catch (Exception ex)
            {
                Log.Error("Failed to read active connection from SSMS", ex);
                return null;
            }
        }

        /// <summary>
        /// The live connection reflects USE statements and the database dropdown, which
        /// UIConnectionInfo does not. Read via reflection because the field is private.
        /// </summary>
        private static IDbConnection GetLiveConnection()
        {
            ThreadHelper.ThrowIfNotOnUIThread();

            if (!(Package.GetGlobalService(typeof(SVsShellMonitorSelection)) is IVsMonitorSelection selection) ||
                ErrorHandler.Failed(selection.GetCurrentElementValue((uint)VSConstants.VSSELELEMID.SEID_DocumentFrame, out var frameObj)) ||
                !(frameObj is IVsWindowFrame frame) ||
                ErrorHandler.Failed(frame.GetProperty((int)__VSFPROPID.VSFPROPID_DocView, out var docView)) ||
                docView == null)
            {
                return null;
            }

            for (var type = docView.GetType(); type != null; type = type.BaseType)
            {
                var field = type.GetField(LiveConnectionField, BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.DeclaredOnly);
                if (field != null)
                    return field.GetValue(docView) as IDbConnection;
            }

            if (!_loggedDocViewProblem)
            {
                _loggedDocViewProblem = true;
                Log.Error($"'{LiveConnectionField}' not found on {docView.GetType().FullName}; falling back to the initial database");
            }
            return null;
        }

        private static string BuildConnectionString(string liveConnectionString, UIConnectionInfo ui, string database)
        {
            SqlConnectionStringBuilder builder;
            try
            {
                // Reuse the window's options (encryption, certificate trust, timeouts, ...)
                builder = new SqlConnectionStringBuilder(liveConnectionString ?? string.Empty);
            }
            catch (ArgumentException ex)
            {
                Log.Error("Could not parse the query window's connection string; using defaults", ex);
                builder = new SqlConnectionStringBuilder();
            }

            if (string.IsNullOrEmpty(builder.DataSource))
            {
                builder.DataSource = ui.ServerName;
                // Pre-SqlClient-4 behaviour: encrypt only if the server requires it
                builder.Encrypt = SqlConnectionEncryptOption.Optional;
                builder.IntegratedSecurity = ui.AuthenticationType != SqlServerAuthentication;
            }

            // Credentials travel separately via SqlCredential
            builder.Remove("User ID");
            builder.Remove("Password");
            if (!string.IsNullOrEmpty(database))
                builder.InitialCatalog = database;
            builder.ApplicationName = "SsmsSqlHelper";
            builder.Pooling = true;
            return builder.ConnectionString;
        }

        private static SecureString GetPassword(UIConnectionInfo ui)
        {
            SecureString password;
            if (ui.InMemoryPassword != null && ui.InMemoryPassword.Length > 0)
            {
                password = ui.InMemoryPassword.Copy();
            }
            else
            {
                password = new SecureString();
                foreach (var c in ui.Password ?? string.Empty)
                    password.AppendChar(c);
            }
            password.MakeReadOnly();
            return password;
        }
    }
}
