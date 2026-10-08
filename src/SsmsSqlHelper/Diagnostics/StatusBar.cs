using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Shell.Interop;

namespace SsmsSqlHelper.Diagnostics
{
    internal static class StatusBar
    {
        public static void Show(string text)
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            if (Package.GetGlobalService(typeof(SVsStatusbar)) is IVsStatusbar statusBar)
                statusBar.SetText(text);
        }
    }
}
