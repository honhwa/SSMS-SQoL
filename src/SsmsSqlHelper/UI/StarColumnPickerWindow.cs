using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Microsoft.VisualStudio.PlatformUI;
using SsmsSqlHelper.Generation;

namespace SsmsSqlHelper.UI
{
    /// <summary>Lets the user choose the columns that replace a SELECT star. Nothing is selected initially.</summary>
    internal sealed class StarColumnPickerWindow : DialogWindow
    {
        private readonly IReadOnlyList<ContextExpander.StarColumn> _columns;
        private readonly HashSet<ContextExpander.StarColumn> _selected = new HashSet<ContextExpander.StarColumn>();
        private readonly TextBox _filter = new TextBox { Padding = new Thickness(5, 4, 5, 4) };
        private readonly ListBox _list = new ListBox();
        private readonly TextBlock _count = new TextBlock { VerticalAlignment = VerticalAlignment.Center };
        private readonly Button _insert = new Button { Content = "Insert columns", IsDefault = true, IsEnabled = false, MinWidth = 120 };
        private readonly Button _selectAll = new Button { Content = "Select all", Margin = new Thickness(0, 0, 8, 0), ToolTip = "Check every column in the list (with a filter typed, only the ones shown). Ctrl+A does the same." };
        private readonly List<ContextExpander.StarColumn> _visible = new List<ContextExpander.StarColumn>();

        public StarColumnPickerWindow(IReadOnlyList<ContextExpander.StarColumn> columns)
        {
            _columns = columns ?? throw new ArgumentNullException(nameof(columns));
            ThemeStyles.Apply(this);
            Title = "Select columns for *";
            Width = 630;
            Height = 580;
            MinWidth = 420;
            MinHeight = 340;
            WindowStartupLocation = WindowStartupLocation.CenterOwner;
            ShowInTaskbar = false;
            FontFamily = new System.Windows.Media.FontFamily("Segoe UI");
            FontSize = 12;

            var root = new Grid { Margin = new Thickness(14) };
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

            var help = new TextBlock { Text = "Choose the columns to insert. All columns start unchecked.", Margin = new Thickness(0, 0, 0, 8) };
            Grid.SetRow(help, 0);
            root.Children.Add(help);

            _filter.Margin = new Thickness(0, 0, 0, 8);
            _filter.ToolTip = "Filter by table, alias, column or type";
            Grid.SetRow(_filter, 1);
            root.Children.Add(_filter);

            Grid.SetRow(_list, 2);
            root.Children.Add(_list);

            var footer = new DockPanel { Margin = new Thickness(0, 10, 0, 0), LastChildFill = false };
            _selectAll.Click += (s, e) => SelectAllVisible();
            DockPanel.SetDock(_selectAll, Dock.Left);
            footer.Children.Add(_selectAll);

            var clear = new Button { Content = "Clear selection", Margin = new Thickness(0, 0, 8, 0) };
            clear.Click += (s, e) => { _selected.Clear(); Refresh(); };
            DockPanel.SetDock(clear, Dock.Left);
            footer.Children.Add(clear);

            _count.Margin = new Thickness(0, 0, 8, 0);
            DockPanel.SetDock(_count, Dock.Left);
            footer.Children.Add(_count);

            var cancel = new Button { Content = "Cancel", IsCancel = true, MinWidth = 80 };
            DockPanel.SetDock(cancel, Dock.Right);
            footer.Children.Add(cancel);

            _insert.Margin = new Thickness(0, 0, 8, 0);
            _insert.Click += (s, e) => { if (_selected.Count > 0) DialogResult = true; };
            DockPanel.SetDock(_insert, Dock.Right);
            footer.Children.Add(_insert);

            Grid.SetRow(footer, 3);
            root.Children.Add(footer);
            Content = root;

            _filter.TextChanged += (s, e) => Refresh();
            PreviewKeyDown += (s, e) =>
            {
                if (e.Key == Key.Escape)
                {
                    DialogResult = false;
                    e.Handled = true;
                }
                else if (e.Key == Key.A && Keyboard.Modifiers == ModifierKeys.Control && !(e.OriginalSource is TextBox))
                {
                    // In the filter box Ctrl+A still selects the text typed there
                    SelectAllVisible();
                    e.Handled = true;
                }
            };
            Loaded += (s, e) => _filter.Focus();
            Refresh();
        }

        public IReadOnlyList<ContextExpander.StarColumn> SelectedColumns =>
            _columns.Where(_selected.Contains).ToList();

        // Checks everything the list currently shows, so a typed filter narrows what "all" means
        private void SelectAllVisible()
        {
            foreach (var column in _visible)
                _selected.Add(column);
            Refresh();
        }

        private void Refresh()
        {
            var filter = _filter.Text.Trim();
            _list.Items.Clear();
            _visible.Clear();
            foreach (var column in _columns)
            {
                var label = column.SqlName + "    " + column.Column.DisplayType + "    (" + column.Table.Schema + "." + column.Table.Name + ")";
                if (filter.Length > 0 && label.IndexOf(filter, StringComparison.OrdinalIgnoreCase) < 0)
                    continue;

                _visible.Add(column);
                var check = new CheckBox { Content = label, IsChecked = _selected.Contains(column), Padding = new Thickness(2, 3, 2, 3), Tag = column };
                check.Checked += (s, e) => { _selected.Add((ContextExpander.StarColumn)((CheckBox)s).Tag); UpdateCount(); };
                check.Unchecked += (s, e) => { _selected.Remove((ContextExpander.StarColumn)((CheckBox)s).Tag); UpdateCount(); };
                _list.Items.Add(check);
            }
            UpdateCount();
        }

        private void UpdateCount()
        {
            _count.Text = _selected.Count + " of " + _columns.Count + " selected";
            _insert.IsEnabled = _selected.Count > 0;
            _selectAll.IsEnabled = _visible.Any(c => !_selected.Contains(c));
        }
    }
}
