using System;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using SsmsSqlHelper.Snippets;

namespace SsmsSqlHelper.UI
{
    /// <summary>One snippet as edited in the window. The body uses the text box's own line breaks; <see cref="ToSnippet"/> converts back.</summary>
    internal sealed class SnippetItem : INotifyPropertyChanged
    {
        private string _shortcut = "";
        private string _description = "";
        private string _body = "";
        private string _problem = "";

        public event PropertyChangedEventHandler PropertyChanged;

        public string Shortcut
        {
            get => _shortcut;
            set => Set(ref _shortcut, value);
        }

        public string Description
        {
            get => _description;
            set => Set(ref _description, value);
        }

        public string Body
        {
            get => _body;
            set => Set(ref _body, value);
        }

        /// <summary>What is wrong with this snippet, one message per line; empty when fine.</summary>
        public string Problem
        {
            get => _problem;
            set
            {
                if (Set(ref _problem, value))
                    OnPropertyChanged(nameof(ProblemMark));
            }
        }

        public string ProblemMark => string.IsNullOrEmpty(_problem) ? "" : "⚠";

        public static SnippetItem From(Snippet snippet) => new SnippetItem
        {
            Shortcut = snippet.Shortcut ?? "",
            Description = snippet.Description ?? "",
            Body = (snippet.Body ?? "").Replace("\r\n", "\n").Replace("\n", Environment.NewLine),
        };

        public Snippet ToSnippet() => new Snippet
        {
            Shortcut = (Shortcut ?? "").Trim(),
            Description = Description ?? "",
            Body = (Body ?? "").Replace("\r\n", "\n"),
        };

        private bool Set(ref string field, string value, [CallerMemberName] string name = null)
        {
            if (field == value)
                return false;

            field = value;
            OnPropertyChanged(name);
            return true;
        }

        private void OnPropertyChanged(string name) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }
}
