using System;
using System.IO;
using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Shell.Interop;

namespace SsmsSqlHelper.Diagnostics
{
    /// <summary>
    /// Writes to the "SQL Helper" Output window pane and to %LocalAppData%\SsmsSqlHelper\log.txt.
    /// The file log survives when the package fails before the Output pane is available.
    /// </summary>
    internal static class Log
    {
        private static readonly Guid PaneGuid = new Guid("e7b3c1a9-2f4d-4c8e-a5b6-9d0e1f3a7c42");
        private static readonly string LogFile = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "SsmsSqlHelper", "log.txt");
        private static readonly object FileLock = new object();
        private static IVsOutputWindowPane _pane;

        public static void Info(string message) => Write("INFO", message);

        public static void Error(string message, Exception ex = null) =>
            Write("ERROR", ex == null ? message : $"{message}: {ex}");

        private static void Write(string level, string message)
        {
            var line = $"{DateTime.Now:HH:mm:ss.fff} [{level}] {message}";
            WriteFile(line);
#pragma warning disable VSSDK007 // Fire-and-forget: callers must not block on logging
            ThreadHelper.JoinableTaskFactory.RunAsync(() => WriteToPaneAsync(line)).FileAndForget("SsmsSqlHelper/Log");
#pragma warning restore VSSDK007
        }

        private static async System.Threading.Tasks.Task WriteToPaneAsync(string line)
        {
            await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();
            GetPane()?.OutputStringThreadSafe(line + Environment.NewLine);
        }

        private static IVsOutputWindowPane GetPane()
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            if (_pane != null)
                return _pane;

            if (!(Package.GetGlobalService(typeof(SVsOutputWindow)) is IVsOutputWindow output))
                return null;

            var guid = PaneGuid;
            output.CreatePane(ref guid, "SQL Helper", 1, 1);
            output.GetPane(ref guid, out _pane);
            return _pane;
        }

        private static void WriteFile(string line)
        {
            try
            {
                lock (FileLock)
                {
                    Directory.CreateDirectory(Path.GetDirectoryName(LogFile));
                    File.AppendAllText(LogFile, line + Environment.NewLine);
                }
            }
            catch (IOException)
            {
                // Logging must never break the editor
            }
        }
    }
}
