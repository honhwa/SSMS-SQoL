using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;
using Microsoft.VisualStudio.Shell;

namespace SsmsSqlHelper.UI
{
    /// <summary>
    /// The strip above a query window: <c>SERVER ▸ DATABASE</c> in large letters, the login small on the right.
    /// Neutral in the colours of the current SSMS theme, or filled with the colour a connection rule chose.
    /// </summary>
    internal sealed class ConnectionBanner : Border
    {
        private readonly TextBlock _server = new TextBlock { FontWeight = FontWeights.Bold };
        private readonly TextBlock _arrow = new TextBlock { Text = "  ▸  ", Opacity = 0.65 };
        private readonly TextBlock _database = new TextBlock { FontWeight = FontWeights.Bold };
        private readonly TextBlock _login = new TextBlock { FontSize = 11, Opacity = 0.8, VerticalAlignment = VerticalAlignment.Center };

        private string _lastState;

        public ConnectionBanner()
        {
            Padding = new Thickness(12, 5, 12, 5);
            TextElement.SetFontFamily(this, new FontFamily("Segoe UI"));
            TextElement.SetFontSize(this, 16);
            Focusable = false;
            IsHitTestVisible = false;

            var names = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
            names.Children.Add(_server);
            names.Children.Add(_arrow);
            names.Children.Add(_database);

            var row = new DockPanel { LastChildFill = true };
            DockPanel.SetDock(_login, Dock.Right);
            row.Children.Add(_login);
            row.Children.Add(names);
            Child = row;

            UseNeutralColors();
        }

        /// <param name="server">Null when the window is not connected.</param>
        /// <param name="login">Shown small on the right, e.g. the SQL login or "Windows".</param>
        /// <param name="colorText">A WPF colour (#RRGGBB, name) from a connection rule, or null for the neutral banner.</param>
        public void Update(string server, string database, string login, string colorText)
        {
            ThreadHelper.ThrowIfNotOnUIThread();

            // Called every second or so: do nothing unless something changed
            var state = server + "\u0001" + database + "\u0001" + login + "\u0001" + colorText;
            if (state == _lastState)
                return;
            _lastState = state;

            if (server == null)
            {
                _server.Text = "Not connected";
                _arrow.Visibility = _database.Visibility = Visibility.Collapsed;
                _login.Text = "";
                UseNeutralColors();
                return;
            }

            _server.Text = server;
            _database.Text = string.IsNullOrEmpty(database) ? "(default database)" : database;
            _arrow.Visibility = _database.Visibility = Visibility.Visible;
            _login.Text = string.IsNullOrEmpty(login) ? "" : "as " + login;

            if (TryParseColor(colorText, out var color))
                UseColor(color);
            else
                UseNeutralColors();
        }

        public static bool TryParseColor(string text, out Color color)
        {
            color = default(Color);
            if (string.IsNullOrWhiteSpace(text))
                return false;

            try
            {
                var converted = ColorConverter.ConvertFromString(text.Trim());
                if (!(converted is Color c))
                    return false;
                color = c;
                return true;
            }
            catch (FormatException)
            {
                return false;
            }
        }

        private void UseNeutralColors()
        {
            SetResourceReference(BackgroundProperty, VsBrushes.ToolWindowBackgroundKey);
            SetResourceReference(TextElement.ForegroundProperty, VsBrushes.ToolWindowTextKey);
        }

        // White on dark colours, black on light ones
        private void UseColor(Color color)
        {
            Background = new SolidColorBrush(Color.FromRgb(color.R, color.G, color.B));
            var luminance = (0.299 * color.R + 0.587 * color.G + 0.114 * color.B) / 255;
            TextElement.SetForeground(this, luminance > 0.6 ? Brushes.Black : Brushes.White);
        }
    }
}
