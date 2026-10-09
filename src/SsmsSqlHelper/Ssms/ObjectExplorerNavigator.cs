using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using Microsoft.SqlServer.Management.Sdk.Sfc;
using Microsoft.SqlServer.Management.UI.VSIntegration;
using Microsoft.SqlServer.Management.UI.VSIntegration.Editors;
using Microsoft.SqlServer.Management.UI.VSIntegration.ObjectExplorer;
using Microsoft.VisualStudio.Shell;
using SsmsSqlHelper.Diagnostics;
using SsmsSqlHelper.Metadata;

namespace SsmsSqlHelper.Ssms
{
    /// <summary>Finds an object on the active query connection and selects it in SSMS Object Explorer.</summary>
    internal static class ObjectExplorerNavigator
    {
        public static bool TrySelectProcedure(ActiveConnection connection, ProcedureInfo procedure)
            => procedure != null && TrySelectObject(connection, procedure.Schema, procedure.Name, "StoredProcedure");

        public static bool TrySelectObject(ActiveConnection connection, SchemaObjectInfo schemaObject)
            => schemaObject != null && TrySelectObject(connection, schemaObject.Schema, schemaObject.Name, schemaObject.ExplorerType);

        public static bool TryOpenTableDesigner(ActiveConnection connection, SchemaObjectInfo table)
        {
            if (table == null || !table.IsTable)
                return false;
            return TryFindObject(connection, table.Schema, table.Name, "Table", (explorer, node) =>
            {
                var managed = node.GetService(typeof(IManagedConnection)) as IManagedConnection;
                var factory = ServiceCache.ScriptFactory;
                if (managed == null || factory == null)
                    throw new InvalidOperationException("SSMS table designer connection is unavailable");
                var method = factory.GetType().GetMethod("CreateDesigner",
                    BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic, null,
                    new[] { typeof(DocumentType), typeof(DocumentOptions), typeof(Urn), typeof(IManagedConnection), typeof(string) }, null);
                if (method == null)
                    throw new MissingMethodException("SSMS table designer API is unavailable");
                method.Invoke(factory, new object[] { DocumentType.Table, DocumentOptions.ManageConnection, new Urn(node.Context), managed, null });
                Log.Info("Opened Table Designer for " + table);
            });
        }

        private static bool TrySelectObject(ActiveConnection connection, string schema, string name, string explorerType)
            => TryFindObject(connection, schema, name, explorerType, (explorer, node) => explorer.SynchronizeTree(node));

        private static bool TryFindObject(ActiveConnection connection, string schema, string name, string explorerType,
            Action<IObjectExplorerService, INodeInformation> onFound)
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            if (connection == null || string.IsNullOrEmpty(connection.Database))
                return false;

            try
            {
                var explorer = Package.GetGlobalService(typeof(IObjectExplorerService)) as IObjectExplorerService;
                if (explorer == null)
                {
                    // SSMS may expose this service through its own provider rather than the VS global provider.
                    var property = typeof(ServiceCache).GetProperty("ServiceProvider", BindingFlags.Public | BindingFlags.Static);
                    var provider = property?.GetValue(null) as IServiceProvider;
                    explorer = provider?.GetService(typeof(IObjectExplorerService)) as IObjectExplorerService;
                }
                if (explorer == null)
                {
                    Log.Info("Object Explorer service is unavailable");
                    return false;
                }

                var server = "Server[@Name='" + Urn.EscapeString(connection.Server) + "']";
                var database = "/Database[@Name='" + Urn.EscapeString(connection.Database) + "']";
                var objectUrn = "/" + explorerType + "[@Name='" + Urn.EscapeString(name) +
                                "' and @Schema='" + Urn.EscapeString(schema) + "']";

                // A query window and Object Explorer can refer to the same server through
                // different connection identities. Start with the exact URNs SSMS assigned
                // to its selected node and hierarchy roots before falling back to a guessed one.
                var candidates = new List<string>();
                foreach (var selected in SelectedNodes(explorer))
                {
                    for (var ancestor = selected; ancestor != null; ancestor = ancestor.Parent)
                    {
                        if (IsDatabase(ancestor, connection))
                            AddCandidate(candidates, ancestor.Context + objectUrn);
                        else if (IsServer(ancestor, connection))
                            AddCandidate(candidates, ancestor.Context + database + objectUrn);
                    }
                }
                var roots = new List<INavigableItem>(HierarchyRoots(explorer));
                foreach (var root in roots)
                {
                    Log.Info("Object Explorer root: " + root.Context?.Context);
                    if (root.Context != null && IsServer(root.Context, connection))
                        AddCandidate(candidates, root.Context.Context + database + objectUrn);
                }
                AddCandidate(candidates, server + database + objectUrn);

                foreach (var urn in candidates)
                {
                    var node = explorer.FindNode(urn);
                    if (node == null)
                    {
                        Log.Info("Object Explorer node not found: " + urn);
                        continue;
                    }

                    onFound(explorer, node);
                    Log.Info("Selected Object Explorer node: " + urn);
                    return true;
                }

                // FindNode can miss a node that is already present in the visible tree,
                // especially when SSMS builds virtual folder nodes between database and procedure.
                foreach (var root in roots)
                {
                    if (root.Context == null || !IsServer(root.Context, connection))
                        continue;
                    var visible = FindVisibleObject(root, connection, schema, name, explorerType, false, 0);
                    if (visible == null)
                        continue;
                    onFound(explorer, visible);
                    Log.Info("Selected visible Object Explorer node: " + visible.Context);
                    return true;
                }
                return false;
            }
            catch (Exception ex)
            {
                Log.Error("Could not select " + schema + "." + name + " in Object Explorer", ex);
                return false;
            }
        }

        private static IEnumerable<INodeInformation> SelectedNodes(IObjectExplorerService explorer)
        {
            explorer.GetSelectedNodes(out var count, out var nodes);
            if (nodes == null)
                yield break;
            for (var i = 0; i < count && i < nodes.Length; i++)
            {
                if (nodes[i] != null)
                {
                    Log.Info("Object Explorer selected node: " + nodes[i].Context);
                    yield return nodes[i];
                }
            }
        }

        private static IEnumerable<INavigableItem> HierarchyRoots(IObjectExplorerService explorer)
        {
            const BindingFlags flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
            var tree = explorer.GetType().GetProperty("Tree", flags)?.GetValue(explorer);
            var hierarchies = tree?.GetType().GetProperty("Hierarchies", flags)?.GetValue(tree) as IDictionary;
            if (hierarchies == null)
                yield break;

            foreach (DictionaryEntry entry in hierarchies)
            {
                var root = entry.Value?.GetType().GetProperty("Root", flags)?.GetValue(entry.Value);
                var item = root?.GetType().GetProperty("ContainedItem", flags)?.GetValue(root) as INavigableItem;
                if (item != null)
                    yield return item;
            }
        }

        private static INodeInformation FindVisibleObject(INavigableItem item, ActiveConnection connection,
            string schema, string name, string explorerType, bool insideDatabase, int depth)
        {
            if (depth > 8 || item?.Context == null)
                return null;
            var context = item.Context.Context;
            var type = UrnType(context);
            if (type == "Database")
            {
                if (!IsDatabase(item.Context, connection))
                    return null;
                insideDatabase = true;
            }
            if (type == explorerType)
                return insideDatabase && IsUrn(context, explorerType, "Name", name) &&
                       IsUrn(context, explorerType, "Schema", schema)
                    ? item.Context : null;

            // Follow only the folders on the way to a stored procedure. This keeps
            // navigation from loading tables, views and unrelated database objects.
            if (insideDatabase && type != "Database" && type != "StoredProceduresFolder" &&
                type != "UserTablesFolder" && type != "ViewsFolder" &&
                type != "Scalar-valuedFunctionsFolder" && type != "Table-valuedFunctionsFolder" &&
                type != "ProgrammabilityFolder" && type != "Programmability" &&
                !string.Equals(item.Context.InvariantName, "Programmability", StringComparison.OrdinalIgnoreCase))
                return null;
            if (!insideDatabase && type != "Server" && type != "DatabasesFolder" && type != "Database")
                return null;

            foreach (var child in item.GetChildren(ItemScope.Any))
            {
                var found = FindVisibleObject(child, connection, schema, name, explorerType, insideDatabase, depth + 1);
                if (found != null)
                    return found;
            }
            return null;
        }

        private static bool IsServer(INodeInformation node, ActiveConnection connection) =>
            IsServerUrn(node.Context, connection.Server);

        private static bool IsDatabase(INodeInformation node, ActiveConnection connection)
        {
            if (!IsUrn(node.Context, "Database", "Name", connection.Database))
                return false;
            var parent = new Urn(node.Context).Parent;
            return parent != null && IsServerUrn(parent.ToString(), connection.Server);
        }

        private static bool IsServerUrn(string text, string queryServer)
        {
            if (UrnType(text) != "Server")
                return false;
            return ServerNameMatcher.Matches(queryServer, new Urn(text).GetAttribute("Name"));
        }

        private static string UrnType(string text)
        {
            try { return new Urn(text).Type; }
            catch (ArgumentException) { return null; }
        }

        private static bool IsUrn(string text, string type, string attribute, string value)
        {
            if (string.IsNullOrEmpty(text))
                return false;
            try
            {
                var urn = new Urn(text);
                return string.Equals(urn.Type, type, StringComparison.OrdinalIgnoreCase) &&
                       string.Equals(urn.GetAttribute(attribute), value, StringComparison.OrdinalIgnoreCase);
            }
            catch (ArgumentException)
            {
                return false;
            }
        }

        private static void AddCandidate(List<string> candidates, string urn)
        {
            if (!candidates.Contains(urn))
                candidates.Add(urn);
        }
    }
}
