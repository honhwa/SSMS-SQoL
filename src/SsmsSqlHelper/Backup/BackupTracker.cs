using System;
using System.ComponentModel.Composition;
using System.Windows.Threading;
using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Text;
using Microsoft.VisualStudio.Text.Editor;
using Microsoft.VisualStudio.Utilities;
using SsmsSqlHelper.Diagnostics;
using SsmsSqlHelper.Settings;
using SsmsSqlHelper.Ssms;

namespace SsmsSqlHelper.Backup
{
    [Export(typeof(IWpfTextViewCreationListener))]
    [ContentType("SQL")]
    [TextViewRole(PredefinedTextViewRoles.Document)]
    internal sealed class BackupViewListener : IWpfTextViewCreationListener
    {
        [Import]
        internal ITextDocumentFactoryService DocumentFactory { get; set; }

        public void TextViewCreated(IWpfTextView textView)
        {
            ThreadHelper.ThrowIfNotOnUIThread();

            ITextDocument document = null;
            if (!DocumentFactory.TryGetTextDocument(textView.TextBuffer, out document))
                Log.Info("A query window has no text document; its unsaved changes are backed up, but a save is not noticed (copies of saved tabs are cleaned up at the next start)");

            textView.Properties[typeof(BackupTracker)] = new BackupTracker(textView, document);
        }
    }

    /// <summary>
    /// Keeps a copy of one query tab on disk while it has unsaved changes: a few seconds after the last keystroke, so typing never
    /// waits for the disk. The copy goes when the tab is saved or closed; what a crash or End Task leaves behind is what can be recovered.
    /// </summary>
    internal sealed class BackupTracker
    {
        private const int IdleSeconds = 3;
        private const int MaxCharacters = 20 * 1000 * 1000;

        private readonly IWpfTextView _view;
        private readonly ITextDocument _document;
        private readonly string _id = Guid.NewGuid().ToString("N");
        private readonly DispatcherTimer _timer;

        private bool _changed;
        private bool _hasCopy;
        private int _writtenVersion = -1;
        private string _server;
        private string _database;
        private bool _disposed;

        public BackupTracker(IWpfTextView view, ITextDocument document)
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            _view = view;
            _document = document;

            _timer = new DispatcherTimer(DispatcherPriority.Background) { Interval = TimeSpan.FromSeconds(IdleSeconds) };
            _timer.Tick += (s, e) => Flush();

            _view.TextBuffer.Changed += OnChanged;
            _view.GotAggregateFocus += OnGotFocus;
            _view.Closed += OnClosed;
            if (_document != null)
                _document.DirtyStateChanged += OnDirtyStateChanged;
        }

        private void OnChanged(object sender, TextContentChangedEventArgs e)
        {
            if (_disposed)
                return;

            ThreadHelper.ThrowIfNotOnUIThread();
            _changed = true;
            CaptureConnection();

            // Restarting the timer on every change makes it fire once typing stops
            _timer.Stop();
            _timer.Start();
        }

        private void OnGotFocus(object sender, EventArgs e)
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            CaptureConnection();
        }

        // SSMS tells us the connection of the window that has focus only, so it is read while this one does
        private void CaptureConnection()
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            if (!_view.HasAggregateFocus)
                return;

            var connection = SsmsConnectionAdapter.GetActiveConnection();
            if (connection != null)
            {
                _server = connection.Server;
                _database = connection.Database;
            }
        }

        private void OnDirtyStateChanged(object sender, EventArgs e)
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            if (_disposed || _document.IsDirty)
                return;

            // Saved: what is on disk is as good as the copy
            _timer.Stop();
            _changed = false;
            RemoveCopy();
        }

        private void Flush()
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            _timer.Stop();
            if (_disposed || !_changed)
                return;
            _changed = false;

            if (!SettingsStore.Instance.Current.BackupUnsavedTabs || (_document != null && !_document.IsDirty))
            {
                RemoveCopy();
                return;
            }

            var snapshot = _view.TextSnapshot;
            if (snapshot.Version.VersionNumber == _writtenVersion || snapshot.Length > MaxCharacters)
                return;

            var text = snapshot.GetText();
            if (string.IsNullOrWhiteSpace(text))
            {
                RemoveCopy();
                return;
            }

            _writtenVersion = snapshot.Version.VersionNumber;
            _hasCopy = true;
            BackupService.Instance.Save(_id, new BackupMeta
            {
                FilePath = _document?.FilePath,
                Server = _server,
                Database = _database,
                SavedAt = DateTime.Now.ToString("yyyy-MM-ddTHH:mm:ss"),
            }, text);
        }

        private void RemoveCopy()
        {
            if (!_hasCopy)
                return;

            _hasCopy = false;
            _writtenVersion = -1;
            BackupService.Instance.Remove(_id);
        }

        // The window was closed, which SSMS only does after asking about unsaved changes: whatever the answer, the user decided.
        // A crash never gets here, which is the point.
        private void OnClosed(object sender, EventArgs e)
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            if (_disposed)
                return;

            _disposed = true;
            _timer.Stop();
            _view.TextBuffer.Changed -= OnChanged;
            _view.GotAggregateFocus -= OnGotFocus;
            _view.Closed -= OnClosed;
            if (_document != null)
                _document.DirtyStateChanged -= OnDirtyStateChanged;

            RemoveCopy();
        }
    }
}
