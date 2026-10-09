using System;

namespace SsmsSqlHelper.Ssms
{
    internal static class ServerNameMatcher
    {
        public static bool Matches(string queryServer, string explorerServer)
        {
            if (string.IsNullOrWhiteSpace(queryServer) || string.IsNullOrWhiteSpace(explorerServer))
                return false;
            if (string.Equals(queryServer.Trim(), explorerServer.Trim(), StringComparison.OrdinalIgnoreCase))
                return true;

            return string.Equals(ShortName(queryServer), ShortName(explorerServer), StringComparison.OrdinalIgnoreCase);
        }

        private static string ShortName(string server)
        {
            var name = server.Trim();
            if (name.StartsWith("tcp:", StringComparison.OrdinalIgnoreCase))
                name = name.Substring(4);
            var port = name.IndexOf(',');
            if (port >= 0)
                name = name.Substring(0, port);
            var instance = name.IndexOf('\\');
            var host = instance >= 0 ? name.Substring(0, instance) : name;
            var dot = host.IndexOf('.');
            if (dot > 0)
                host = host.Substring(0, dot);
            return instance >= 0 ? host + name.Substring(instance) : host;
        }
    }
}
