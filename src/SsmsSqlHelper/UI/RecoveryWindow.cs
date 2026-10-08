using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using Microsoft.VisualStudio.PlatformUI;
using Microsoft.VisualStudio.Shell;
using SsmsSqlHelper.Backup;
using SsmsSqlHelper.Diagnostics;

namespace SsmsSqlHelper.UI
{
    /// <summary>
    /// Lists the unsaved queries an earlier session left behind. A recovered query becomes a .sql file of its own that is opened
    /// in SSMS (not connected: pick the connection as for any file); a discarded one is deleted.
    /// </summary>
    internal sealed class RecoveryWindow : DialogWindow
    {
        private const int PreviewCharacters = 20000;

        private readonly IServiceProvider _services;
        private readonly List<BackupEntry> _entries;
        private readonly ListBox _list = new ListBox { SelectionMode = SelectionMode.Extended, HorizontalContentAlignment = HorizontalAlignment.Stretch };
        private readonly TextBox _preview = new TextBox
        {
            IsReadOnly = true, FontFamily = new System.Windows.Media.FontFamily("Consolas"), Padding = new Thickness(4, 3, 4, 3),
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Auto, TextWrapping = TextWrapping.NoWrap,
        };
        private readonly TextBlock _summary = new TextBlock { VerticalAlignment = VerticalAlignment.Center };
        private readonly Button _recover = new Button { Content = "Recover selected", IsDefault = true, MinWidth = 130 };
        private readonly Button _recoverAll = new Button { Content = "Recover all", MinWidth = 100, Margin = new Thickness(0, 0, 8, 0) };
        private readonly Button _discard = new Button { Content = "Discard selected", Margin = new Thickness(0, 0, 8, 0) };

        public RecoveryWindow(IServiceProvider services, IEnumerable<BackupEntry> entries)
        {
            _services = services;
            _entries = entries.ToList();

            ThemeStyles.Apply(this);
            Title = "Recover unsaved queries";
            Width = 900;
            Height = 560;
            MinWidth = 640;
            MinHeight = 380;
            WindowStartupLocation = WindowStartupLocation.CenterOwner;
            ShowInTaskbar = false;
            FontFamily = new System.Windows.Media.FontFamily("Segoe UI");
            FontSize = 12;

            var root = new Grid { Margin = new Thickness(14) };
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

            var help = new TextBlock
            {
                Text = "These tabs had unsaved changes when SSMS stopped. Recovering one saves it as a .sql file in " + BackupService.RecoveredFolder +
                       " and opens it; connect it to a server as you would any file.",
                TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 10),
            };
            Grid.SetRow(help, 0);
            root.Children.Add(help);

            var body = new Grid();
            body.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(360) });
            body.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(12) });
            body.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            Grid.SetColumn(_list, 0);
            Grid.SetColumn(_preview, 2);
            body.Children.Add(_list);
            body.Children.Add(_preview);
            Grid.SetRow(body, 1);
            root.Children.Add(body);

            var footer = new DockPanel { Margin = new Thickness(0, 12, 0, 0), LastChildFill = false };
            DockPanel.SetDock(_summary, Dock.Left);
            footer.Children.Add(_summary);
            var close = new Button { Content = "Close (decide later)", IsCancel = true };
            DockPanel.SetDock(close, Dock.Right);
            footer.Children.Add(close);
            _recover.Margin = new Thickness(0, 0, 8, 0);
            DockPanel.SetDock(_recover, Dock.Right);
            footer.Children.Add(_recover);
            DockPanel.SetDock(_recoverAll, Dock.Right);
            footer.Children.Add(_recoverAll);
            DockPanel.SetDock(_discard, Dock.Right);
            footer.Children.Add(_discard);
            Grid.SetRow(footer, 2);
            root.Children.Add(footer);

            Content = root;

            _list.SelectionChanged += (s, e) => ShowPreview();
            _recover.Click += (s, e) =>
            {
                ThreadHelper.ThrowIfNotOnUIThread();
                Recover(SelectedEntries());
            };
            _recoverAll.Click += (s, e) =>
            {
                ThreadHelper.ThrowIfNotOnUIThread();
                Recover(_entries.ToList());
            };
            _discard.Click += (s, e) => Discard(SelectedEntries());

            Fill();
            if (_list.Items.Count > 0)
                _list.SelectedIndex = 0;
        }

        private List<BackupEntry> SelectedEntries() => _list.SelectedItems.Cast<ListBoxItem>().Select(i => (BackupEntry)i.Tag).ToList();

        private void Fill()
        {
            _list.Items.Clear();
            foreach (var entry in _entries)
            {
                var where = string.IsNullOrWhiteSpace(entry.Meta?.Server)
                    ? "connection unknown"
                    : entry.Meta.Server + (string.IsNullOrWhiteSpace(entry.Meta.Database) ? "" : " / " + entry.Meta.Database);

                var row = new StackPanel { Margin = new Thickness(2, 4, 2, 4) };
                row.Children.Add(new TextBlock { Text = entry.Preview, FontWeight = FontWeights.SemiBold, TextTrimming = TextTrimming.CharacterEllipsis });
                row.Children.Add(new TextBlock
                {
                    Text = where + "  ·  " + entry.SavedAt.ToString("yyyy-MM-dd HH:mm") + "  ·  " + Size(entry.Bytes),
                    Opacity = 0.7, TextTrimming = TextTrimming.CharacterEllipsis,
                });
                _list.Items.Add(new ListBoxItem { Content = row, Tag = entry });
            }

            _summary.Text = _entries.Count == 1 ? "1 unsaved query" : _entries.Count + " unsaved queries";
            var any = _entries.Count > 0;
            _recover.IsEnabled = _recoverAll.IsEnabled = _discard.IsEnabled = any;
            if (!any)
                _preview.Text = "";
        }

        private void ShowPreview()
        {
            var selected = SelectedEntries();
            if (selected.Count == 0)
            {
                _preview.Text = "";
                return;
            }

            try
            {
                var text = File.ReadAllText(selected[0].TextPath);
                _preview.Text = text.Length > PreviewCharacters ? text.Substring(0, PreviewCharacters) + Environment.NewLine + "..." : text;
            }
            catch (IOException ex)
            {
                _preview.Text = "Could not read this copy: " + ex.Message;
            }
        }

        private void Recover(List<BackupEntry> chosen)
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            foreach (var entry in chosen)
            {
                try
                {
                    var path = BackupStore.Recover(entry, BackupService.RecoveredFolder);
                    _entries.Remove(entry);
                    VsShellUtilities.OpenDocument(_services, path);
                }
                catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException)
                {
                    Log.Error("Could not recover " + entry.TextPath, ex);
                    MessageBox.Show(this, "Could not recover this query:\n\n" + ex.Message, "SQL Helper", MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
            AfterChange();
        }

        private void Discard(List<BackupEntry> chosen)
        {
            if (chosen.Count == 0)
                return;

            var answer = MessageBox.Show(this,
                (chosen.Count == 1 ? "Delete this unsaved query for good?" : "Delete these " + chosen.Count + " unsaved queries for good?"),
                "SQL Helper", MessageBoxButton.YesNo, MessageBoxImage.Warning, MessageBoxResult.No);
            if (answer != MessageBoxResult.Yes)
                return;

            foreach (var entry in chosen)
            {
                BackupStore.Discard(entry);
                _entries.Remove(entry);
            }
            AfterChange();
        }

        private void AfterChange()
        {
            Fill();
            if (_entries.Count == 0)
            {
                DialogResult = true;
                return;
            }
            _list.SelectedIndex = 0;
        }

        private static string Size(long bytes) => bytes < 1024 ? bytes + " B" : bytes < 1024 * 1024 ? (bytes / 1024) + " KB" : (bytes / (1024 * 1024)) + " MB";
    }
}
