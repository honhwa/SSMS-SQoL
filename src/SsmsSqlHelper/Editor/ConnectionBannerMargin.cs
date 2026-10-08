using System;
using System.ComponentModel.Composition;
using System.Windows;
using System.Windows.Threading;
using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Text.Editor;
using Microsoft.VisualStudio.Utilities;
using SsmsSqlHelper.Settings;
using SsmsSqlHelper.Ssms;
using SsmsSqlHelper.UI;

namespace SsmsSqlHelper.Editor
{
    [Export(typeof(IWpfTextViewMarginProvider))]
    [Name(ConnectionBannerMargin.MarginName)]
    [MarginContainer(PredefinedMarginNames.Top)]
    [ContentType("SQL")]
    [TextViewRole(PredefinedTextViewRoles.Document)]
    internal sealed class ConnectionBannerMarginProvider : IWpfTextViewMarginProvider
    {
        public IWpfTextViewMargin CreateMargin(IWpfTextViewHost wpfTextViewHost, IWpfTextViewMargin marginContainer) =>
            new ConnectionBannerMargin(wpfTextViewHost.TextView);
    }

    /// <summary>
    /// A banner on top of each query window with the server and database it is connected to, so a query is never run
    /// in the wrong window by mistake. SSMS only tells us the connection of the window that has focus, so each banner
    /// refreshes while its window is the focused one (on focus, then about once a second, which also catches USE and the database list).
    /// </summary>
    internal sealed class ConnectionBannerMargin : IWpfTextViewMargin
    {
        public const string MarginName = "SsmsSqlHelper.ConnectionBanner";

        private readonly IWpfTextView _view;
        private readonly ConnectionBanner _banner = new ConnectionBanner();
        private readonly DispatcherTimer _timer;
        private bool _disposed;

        public ConnectionBannerMargin(IWpfTextView view)
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            _view = view;
            _banner.Update(null, null, null, null);
            _banner.Visibility = Visibility.Collapsed;      // until this window has focus once and we know what it is connected to

            _view.GotAggregateFocus += OnGotFocus;
            _view.Closed += (s, e) => Dispose();

            _timer = new DispatcherTimer(DispatcherPriority.Background) { Interval = TimeSpan.FromSeconds(1) };
            _timer.Tick += (s, e) =>
            {
                ThreadHelper.ThrowIfNotOnUIThread();
                if (_view.HasAggregateFocus)
                    Refresh();
            };
            _timer.Start();
        }

        public FrameworkElement VisualElement
        {
            get
            {
                ThrowIfDisposed();
                return _banner;
            }
        }

        public double MarginSize
        {
            get
            {
                ThrowIfDisposed();
                return _banner.ActualHeight;
            }
        }

        public bool Enabled
        {
            get
            {
                ThrowIfDisposed();
                return SettingsStore.Instance.Current.ShowConnectionBanner;
            }
        }

        public ITextViewMargin GetTextViewMargin(string marginName) => marginName == MarginName ? this : null;

        public void Dispose()
        {
            if (_disposed)
                return;

            _disposed = true;
            _timer.Stop();
            _view.GotAggregateFocus -= OnGotFocus;
        }

        private void OnGotFocus(object sender, EventArgs e)
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            Refresh();
        }

        private void Refresh()
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            if (_disposed)
                return;

            var settings = SettingsStore.Instance.Current;
            if (!settings.ShowConnectionBanner)
            {
                _banner.Visibility = Visibility.Collapsed;
                return;
            }

            var connection = SsmsConnectionAdapter.GetActiveConnection();
            _banner.Visibility = Visibility.Visible;
            if (connection == null)
            {
                _banner.Update(null, null, null, null);
                return;
            }

            _banner.Update(connection.Server, connection.Database, connection.IsSqlAuth ? connection.UserName : "Windows",
                ConnectionColors.Resolve(settings.ConnectionColors, connection.Server, connection.Database));
        }

        private void ThrowIfDisposed()
        {
            if (_disposed)
                throw new ObjectDisposedException(MarginName);
        }
    }
}
