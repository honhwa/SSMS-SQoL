using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Microsoft.VisualStudio.PlatformUI;
using SsmsSqlHelper.Snippets;

namespace SsmsSqlHelper.UI
{
    /// <summary>A small filterable list to pick one snippet from. Enter or double-click picks, Esc cancels.</summary>
    internal sealed class SnippetPickerWindow : DialogWindow
    {
        private readonly IReadOnlyList<Snippet> _all;
        private readonly TextBox _filter = new TextBox { Padding = new Thickness(4, 3, 4, 3) };
        private readonly ListBox _list = new ListBox { HorizontalContentAlignment = HorizontalAlignment.Stretch };

        public SnippetPickerWindow(string title, string hint, IReadOnlyList<Snippet> snippets)
        {
            _all = snippets;

            ThemeStyles.Apply(this);
            Title = title;
            Width = 480;
            Height = 380;
            MinWidth = 360;
            MinHeight = 260;
            WindowStartupLocation = WindowStartupLocation.CenterOwner;
            ShowInTaskbar = false;
            FontFamily = new System.Windows.Media.FontFamily("Segoe UI");
            FontSize = 12;

            var root = new Grid { Margin = new Thickness(14) };
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

            _filter.Margin = new Thickness(0, 0, 0, 8);
            Grid.SetRow(_filter, 0);
            root.Children.Add(_filter);

            Grid.SetRow(_list, 1);
            root.Children.Add(_list);

            var help = new TextBlock { Text = hint, Opacity = 0.7, Margin = new Thickness(0, 8, 0, 0), TextWrapping = TextWrapping.Wrap };
            Grid.SetRow(help, 2);
            root.Children.Add(help);

            Content = root;

            _filter.TextChanged += (s, e) => Refresh();
            _filter.PreviewKeyDown += Filter_PreviewKeyDown;
            _list.MouseDoubleClick += (s, e) =>
            {
                if (_list.SelectedItem != null)
                    Choose();
            };
            _list.KeyDown += (s, e) =>
            {
                if (e.Key == Key.Enter)
                    Choose();
            };
            Loaded += (s, e) => _filter.Focus();

            Refresh();
        }

        /// <summary>The snippet picked, or null if the window was cancelled.</summary>
        public Snippet Chosen { get; private set; }

        private void Refresh()
        {
            var text = _filter.Text.Trim();
            var matches = _all.Where(s => text.Length == 0 ||
                                          s.Shortcut.IndexOf(text, StringComparison.OrdinalIgnoreCase) >= 0 ||
                                          (s.Description ?? "").IndexOf(text, StringComparison.OrdinalIgnoreCase) >= 0).ToList();

            _list.Items.Clear();
            foreach (var s in matches)
            {
                var row = new StackPanel { Margin = new Thickness(2, 3, 2, 3), Tag = s };
                row.Children.Add(new TextBlock { Text = s.Shortcut, FontWeight = FontWeights.SemiBold });
                row.Children.Add(new TextBlock { Text = s.Description ?? "", Opacity = 0.7, TextTrimming = TextTrimming.CharacterEllipsis });
                _list.Items.Add(row);
            }
            _list.SelectedIndex = _list.Items.Count > 0 ? 0 : -1;
        }

        private void Filter_PreviewKeyDown(object sender, KeyEventArgs e)
        {
            switch (e.Key)
            {
                case Key.Down:
                    _list.SelectedIndex = Math.Min(_list.Items.Count - 1, _list.SelectedIndex + 1);
                    e.Handled = true;
                    break;
                case Key.Up:
                    _list.SelectedIndex = Math.Max(0, _list.SelectedIndex - 1);
                    e.Handled = true;
                    break;
                case Key.Enter:
                    Choose();
                    e.Handled = true;
                    break;
                case Key.Escape:
                    DialogResult = false;
                    e.Handled = true;
                    break;
            }
        }

        private void Choose()
        {
            if (!(_list.SelectedItem is StackPanel row))
                return;

            Chosen = (Snippet)row.Tag;
            DialogResult = true;
        }
    }
}
