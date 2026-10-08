using System.Security;
using Microsoft.Data.SqlClient;

namespace SsmsSqlHelper.Ssms
{
    /// <summary>Connection details of the active query window, decoupled from SSMS types.</summary>
    internal sealed class ActiveConnection
    {
        private readonly string _connectionString;
        private readonly SecureString _password;

        /// <param name="connectionString">Connection string without credentials, already pointing at <paramref name="database"/>.</param>
        /// <param name="password">Read-only password for SQL Server authentication; null for Windows authentication.</param>
        public ActiveConnection(string server, string database, string userName, string connectionString, SecureString password)
        {
            Server = server;
            Database = database;
            UserName = userName;
            _connectionString = connectionString;
            _password = password;
        }

        public string Server { get; }
        public string Database { get; }
        public string UserName { get; }
        public bool IsSqlAuth => _password != null;

        public string CacheKey => $"{Server}|{Database}|{UserName}".ToUpperInvariant();

        public SqlConnection CreateSqlConnection() =>
            IsSqlAuth
                ? new SqlConnection(_connectionString, new SqlCredential(UserName, _password))
                : new SqlConnection(_connectionString);

        public override string ToString() =>
            $"{Server} / {(string.IsNullOrEmpty(Database) ? "(default db)" : Database)} as {(IsSqlAuth ? UserName : "Windows")}";
    }
}
