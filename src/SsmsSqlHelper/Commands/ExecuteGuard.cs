using System;
using System.Collections.Generic;
using System.Linq;
using EnvDTE;
using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Shell.Interop;
using SsmsSqlHelper.Diagnostics;
using SsmsSqlHelper.Editor;
using SsmsSqlHelper.Parsing;
using SsmsSqlHelper.Settings;
using Task = System.Threading.Tasks.Task;

namespace SsmsSqlHelper.Commands
{
    /// <summary>
    /// Asks before a script that has an UPDATE or DELETE without WHERE is run (F5, Ctrl+E, the Execute button): such a statement
    /// changes every row. Runs what SSMS would run, the selection if there is one, else the whole window. Can be turned off in
    /// the snippet editor window.
    /// </summary>
    internal sealed class ExecuteGuard
    {
        // Names in the SSMS command table; the guid and id behind them are looked up at startup rather than hard-coded
        private static readonly string[] CommandNames = { "Query.Execute" };

        private const int IdYes = 6;
        private const int MaxListed = 6;

        private static ExecuteGuard _instance;

        private readonly AsyncPackage _package;
        private readonly List<CommandEvents> _events = new List<CommandEvents>();   // must stay referenced or the events stop firing

        private ExecuteGuard(AsyncPackage package)
        {
            _package = package;
        }

        public static async Task InitializeAsync(AsyncPackage package)
        {
            await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync(package.DisposalToken);

            var dte = await package.GetServiceAsync(typeof(DTE)) as DTE;
            if (dte == null)
            {
                Log.Error("DTE unavailable; UPDATE/DELETE without WHERE will not be flagged");
                return;
            }

            _instance = new ExecuteGuard(package);
            _instance.Hook(dte);
        }

        private void Hook(DTE dte)
        {
            ThreadHelper.ThrowIfNotOnUIThread();

            foreach (var name in CommandNames)
            {
                try
                {
                    var command = dte.Commands.Item(name);
                    var events = dte.Events.get_CommandEvents(command.Guid, command.ID);
                    events.BeforeExecute += OnBeforeExecute;
                    _events.Add(events);
                    Log.Info($"Watching {name} ({command.Guid}, {command.ID}) for UPDATE/DELETE without WHERE");
                }
                catch (Exception ex)
                {
                    Log.Error($"Could not watch command '{name}'; UPDATE/DELETE without WHERE will not be flagged for it", ex);
                    LogExecuteLikeCommands(dte);
                }
            }
        }

        // If SSMS ever renames the command, the log shows what it is called now
        private static void LogExecuteLikeCommands(DTE dte)
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            try
            {
                var names = new List<string>();
                foreach (Command c in dte.Commands)
                {
                    var n = c.Name;
                    if (!string.IsNullOrEmpty(n) && n.StartsWith("Query.", StringComparison.OrdinalIgnoreCase) && n.IndexOf("Exec", StringComparison.OrdinalIgnoreCase) >= 0)
                        names.Add(n);
                }
                Log.Info("Commands that look like Execute: " + (names.Count == 0 ? "(none)" : string.Join(", ", names)));
            }
            catch (Exception ex)
            {
                Log.Error("Could not list commands", ex);
            }
        }

        private void OnBeforeExecute(string guid, int id, object customIn, object customOut, ref bool cancelDefault)
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            try
            {
                if (!SettingsStore.Instance.Current.WarnMissingWhere)
                    return;

                var view = ActiveEditor.Current;
                if (view == null)
                    return;

                string text;
                var firstLine = 1;
                if (view.Selection.IsEmpty)
                {
                    text = view.TextSnapshot.GetText();
                }
                else
                {
                    var span = view.Selection.SelectedSpans[0];
                    text = span.GetText();
                    firstLine = view.TextSnapshot.GetLineNumberFromPosition(span.Start.Position) + 1;
                }

                var found = UnsafeStatementChecker.Find(text, firstLine);
                if (found.Count == 0)
                    return;

                var answer = VsShellUtilities.ShowMessageBox(_package, BuildMessage(found), "SQL Helper",
                    OLEMSGICON.OLEMSGICON_WARNING, OLEMSGBUTTON.OLEMSGBUTTON_YESNO, OLEMSGDEFBUTTON.OLEMSGDEFBUTTON_SECOND);
                if (answer != IdYes)
                    cancelDefault = true;
            }
            catch (Exception ex)
            {
                // A bug in the check must never stop someone from running their query
                Log.Error("UPDATE/DELETE check failed; running the script unchecked", ex);
            }
        }

        internal static string BuildMessage(IReadOnlyList<UnsafeStatement> found)
        {
            var lines = found.Take(MaxListed).Select(s => $"  line {s.Line}: {s.Text}");
            var more = found.Count > MaxListed ? $"\n  ... and {found.Count - MaxListed} more" : "";
            return (found.Count == 1 ? "This statement has no WHERE, so it changes every row:" : "These statements have no WHERE, so they change every row:") +
                   "\n\n" + string.Join("\n", lines) + more + "\n\nRun it anyway?";
        }
    }
}
