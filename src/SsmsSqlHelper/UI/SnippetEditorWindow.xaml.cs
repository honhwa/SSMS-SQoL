using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using Microsoft.VisualStudio.PlatformUI;
using Microsoft.VisualStudio.Shell;
using SsmsSqlHelper.Diagnostics;
using SsmsSqlHelper.Settings;
using SsmsSqlHelper.Snippets;

namespace SsmsSqlHelper.UI
{
    /// <summary>Edits the snippet list. Saves to snippets.json (so the JSON file stays the source of truth) and to settings.json.</summary>
    public partial class SnippetEditorWindow : DialogWindow
    {
        private readonly ObservableCollection<SnippetItem> _items = new ObservableCollection<SnippetItem>();
        private bool _loading = true;
        private bool _dirty;

        internal SnippetEditorWindow(IEnumerable<Snippet> snippets, UserSettings settings)
        {
            ThemeStyles.Apply(this);
            InitializeComponent();

            SnippetList.ItemsSource = _items;
            HintsCheck.IsChecked = settings.ShowSnippetHints;
            ColumnsCheck.IsChecked = settings.ShowColumnHints;
            KeywordsCheck.IsChecked = settings.ShowKeywordHints;
            AliasCheck.IsChecked = settings.AutoAlias;
            WarnCheck.IsChecked = settings.WarnMissingWhere;
            BackupCheck.IsChecked = settings.BackupUnsavedTabs;
            BannerCheck.IsChecked = settings.ShowConnectionBanner;
            BannerRulesBox.Text = ConnectionColors.Format(settings.ConnectionColors);
            Load(snippets);

            Closing += OnClosing;
            _loading = false;
        }

        /// <summary>True when the user asked to leave the window to edit the JSON file instead.</summary>
        internal bool OpenJsonRequested { get; private set; }

        private void Load(IEnumerable<Snippet> snippets)
        {
            foreach (var item in _items)
                item.PropertyChanged -= Item_PropertyChanged;
            _items.Clear();

            foreach (var snippet in snippets)
            {
                var item = SnippetItem.From(snippet);
                item.PropertyChanged += Item_PropertyChanged;
                _items.Add(item);
            }

            SnippetList.SelectedIndex = _items.Count > 0 ? 0 : -1;
            Revalidate();
        }

        // ---- list actions ----

        private void Add_Click(object sender, RoutedEventArgs e) =>
            AddItem(new SnippetItem { Body = SnippetExpander.CursorMarker }, SnippetList.SelectedIndex + 1);

        private void Duplicate_Click(object sender, RoutedEventArgs e)
        {
            if (!(SnippetList.SelectedItem is SnippetItem source))
                return;

            AddItem(new SnippetItem { Shortcut = source.Shortcut + "2", Description = source.Description, Body = source.Body },
                SnippetList.SelectedIndex + 1);
        }

        private void Delete_Click(object sender, RoutedEventArgs e)
        {
            var index = SnippetList.SelectedIndex;
            if (index < 0)
                return;

            _items[index].PropertyChanged -= Item_PropertyChanged;
            _items.RemoveAt(index);
            _dirty = true;
            SnippetList.SelectedIndex = Math.Min(index, _items.Count - 1);
            Revalidate();
            SnippetList.Focus();
        }

        private void AddItem(SnippetItem item, int index)
        {
            item.PropertyChanged += Item_PropertyChanged;
            _items.Insert(Math.Max(0, Math.Min(index, _items.Count)), item);
            _dirty = true;
            SnippetList.SelectedItem = item;
            SnippetList.ScrollIntoView(item);
            Revalidate();
            ShortcutBox.Focus();
            ShortcutBox.SelectAll();
        }

        private void SnippetList_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            var selected = SnippetList.SelectedItem as SnippetItem;
            EditPanel.DataContext = selected;
            EditPanel.IsEnabled = selected != null;
            DuplicateButton.IsEnabled = DeleteButton.IsEnabled = selected != null;
        }

        private void InsertCursor_Click(object sender, RoutedEventArgs e) => InsertIntoBody(SnippetExpander.CursorMarker);

        private void InsertSelected_Click(object sender, RoutedEventArgs e) => InsertIntoBody("$SELECTED$");

        // The next free number, with the word "text" selected so typing replaces it
        private void InsertStop_Click(object sender, RoutedEventArgs e)
        {
            var used = Regex.Matches(BodyBox.Text, @"\$\{?(\d+)").Cast<Match>().Select(m => int.Parse(m.Groups[1].Value)).DefaultIfEmpty(0).Max();
            var stop = "${" + (used + 1) + ":text}";
            var at = BodyBox.SelectionStart;
            InsertIntoBody(stop);
            BodyBox.Select(at + stop.IndexOf(':') + 1, 4);
        }

        private void InsertIntoBody(string text)
        {
            BodyBox.SelectedText = text;
            BodyBox.CaretIndex = BodyBox.SelectionStart + BodyBox.SelectionLength;
            BodyBox.SelectionLength = 0;
            BodyBox.Focus();
        }

        // ---- validation ----

        private void Item_PropertyChanged(object sender, PropertyChangedEventArgs e)
        {
            // Problem / ProblemMark are outputs of validation, not edits
            if (e.PropertyName == nameof(SnippetItem.Problem) || e.PropertyName == nameof(SnippetItem.ProblemMark))
                return;

            if (!_loading)
                _dirty = true;
            Revalidate();
        }

        private void Option_Changed(object sender, RoutedEventArgs e)
        {
            if (!_loading)
                _dirty = true;
        }

        private void BannerRules_TextChanged(object sender, TextChangedEventArgs e)
        {
            if (!_loading)
                _dirty = true;
            if (IsLoaded || SaveButton != null)
                Revalidate();
        }

        private void Revalidate()
        {
            var snippets = _items.Select(i => i.ToSnippet()).ToList();
            var issues = SnippetFileFormat.Validate(snippets);

            for (var i = 0; i < _items.Count; i++)
            {
                var index = i;
                _items[i].Problem = string.Join(Environment.NewLine, issues.Where(x => x.Index == index).Select(x => x.Message));
            }

            var errors = issues.Count(x => x.IsError);
            var warnings = issues.Count - errors;

            // The banner colour rules are part of what Save has to accept
            ParseBannerRules(out var bannerProblems);
            BannerProblemText.Text = string.Join(Environment.NewLine, bannerProblems);
            errors += bannerProblems.Count;
            SaveButton.IsEnabled = errors == 0;
            SummaryText.Text = errors > 0
                ? $"{errors} problem{(errors == 1 ? "" : "s")} to fix before saving"
                : warnings > 0 ? $"{warnings} warning{(warnings == 1 ? "" : "s")}" : "";
        }

        private List<ConnectionColorRule> ParseBannerRules(out List<string> problems) =>
            ConnectionColors.Parse(BannerRulesBox.Text, color => ConnectionBanner.TryParseColor(color, out _), out problems);

        // ---- bottom buttons ----

        private void Save_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                SnippetStore.Instance.Save(_items.Select(i => i.ToSnippet()));
                SettingsStore.Instance.Save(new UserSettings
                {
                    ShowSnippetHints = HintsCheck.IsChecked == true,
                    ShowColumnHints = ColumnsCheck.IsChecked == true,
                    ShowKeywordHints = KeywordsCheck.IsChecked == true,
                    AutoAlias = AliasCheck.IsChecked == true,
                    WarnMissingWhere = WarnCheck.IsChecked == true,
                    BackupUnsavedTabs = BackupCheck.IsChecked == true,
                    ShowConnectionBanner = BannerCheck.IsChecked == true,
                    ConnectionColors = ParseBannerRules(out _),
                });
            }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException)
            {
                Log.Error("Failed to save snippets", ex);
                MessageBox.Show(this, "Could not save:\n\n" + ex.Message, "SQL Helper", MessageBoxButton.OK, MessageBoxImage.Error);
                return;
            }

            _dirty = false;
            DialogResult = true;
        }

        private void Reset_Click(object sender, RoutedEventArgs e)
        {
            var answer = MessageBox.Show(this,
                "Replace all snippets in this window with the built-in defaults?\n\nNothing is saved until you click Save.",
                "SQL Helper", MessageBoxButton.OKCancel, MessageBoxImage.Question);
            if (answer != MessageBoxResult.OK)
                return;

            _loading = true;
            Load(SnippetStore.ReadDefaults());
            _loading = false;
            _dirty = true;
        }

        private void OpenJson_Click(object sender, RoutedEventArgs e)
        {
            OpenJsonRequested = true;
            Close();
        }

        private void OnClosing(object sender, CancelEventArgs e)
        {
            if (DialogResult == true || !_dirty)
                return;

            var answer = MessageBox.Show(this, "Discard your changes?", "SQL Helper", MessageBoxButton.YesNo, MessageBoxImage.Question, MessageBoxResult.No);
            if (answer != MessageBoxResult.Yes)
            {
                e.Cancel = true;
                OpenJsonRequested = false;
            }
        }
    }
}
