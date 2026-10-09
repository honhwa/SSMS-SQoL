using System;
using System.Collections.Generic;
using System.ComponentModel.Composition;
using System.Windows.Media;
using Microsoft.VisualStudio.Text;
using Microsoft.VisualStudio.Text.Classification;
using Microsoft.VisualStudio.Text.Tagging;
using Microsoft.VisualStudio.Utilities;
using SsmsSqlHelper.Parsing;

namespace SsmsSqlHelper.Editor
{
    internal static class BracketColorNames
    {
        public const string One = "SQL Helper Bracket 1";
        public const string Two = "SQL Helper Bracket 2";
        public const string Three = "SQL Helper Bracket 3";
        public const string Four = "SQL Helper Bracket 4";
        public const string Five = "SQL Helper Bracket 5";
        public const string Six = "SQL Helper Bracket 6";
    }

    internal static class BracketClassificationTypes
    {
        [Export(typeof(ClassificationTypeDefinition))]
        [Name(BracketColorNames.One)]
        internal static ClassificationTypeDefinition BracketTypeOne = null;
        [Export(typeof(ClassificationTypeDefinition))]
        [Name(BracketColorNames.Two)]
        internal static ClassificationTypeDefinition BracketTypeTwo = null;
        [Export(typeof(ClassificationTypeDefinition))]
        [Name(BracketColorNames.Three)]
        internal static ClassificationTypeDefinition BracketTypeThree = null;
        [Export(typeof(ClassificationTypeDefinition))]
        [Name(BracketColorNames.Four)]
        internal static ClassificationTypeDefinition BracketTypeFour = null;
        [Export(typeof(ClassificationTypeDefinition))]
        [Name(BracketColorNames.Five)]
        internal static ClassificationTypeDefinition BracketTypeFive = null;
        [Export(typeof(ClassificationTypeDefinition))]
        [Name(BracketColorNames.Six)]
        internal static ClassificationTypeDefinition BracketTypeSix = null;
    }

    internal abstract class BracketFormat : ClassificationFormatDefinition
    {
        protected BracketFormat(byte red, byte green, byte blue)
        {
            ForegroundColor = Color.FromRgb(red, green, blue);
        }
    }

    [Export(typeof(EditorFormatDefinition))]
    [ClassificationType(ClassificationTypeNames = BracketColorNames.One)]
    [Name(BracketColorNames.One)]
    [UserVisible(true)]
    internal sealed class BracketFormatOne : BracketFormat { public BracketFormatOne() : base(190, 105, 0) { } }

    [Export(typeof(EditorFormatDefinition))]
    [ClassificationType(ClassificationTypeNames = BracketColorNames.Two)]
    [Name(BracketColorNames.Two)]
    [UserVisible(true)]
    internal sealed class BracketFormatTwo : BracketFormat { public BracketFormatTwo() : base(145, 75, 205) { } }

    [Export(typeof(EditorFormatDefinition))]
    [ClassificationType(ClassificationTypeNames = BracketColorNames.Three)]
    [Name(BracketColorNames.Three)]
    [UserVisible(true)]
    internal sealed class BracketFormatThree : BracketFormat { public BracketFormatThree() : base(0, 155, 180) { } }

    [Export(typeof(EditorFormatDefinition))]
    [ClassificationType(ClassificationTypeNames = BracketColorNames.Four)]
    [Name(BracketColorNames.Four)]
    [UserVisible(true)]
    internal sealed class BracketFormatFour : BracketFormat { public BracketFormatFour() : base(45, 165, 85) { } }

    [Export(typeof(EditorFormatDefinition))]
    [ClassificationType(ClassificationTypeNames = BracketColorNames.Five)]
    [Name(BracketColorNames.Five)]
    [UserVisible(true)]
    internal sealed class BracketFormatFive : BracketFormat { public BracketFormatFive() : base(215, 75, 120) { } }

    [Export(typeof(EditorFormatDefinition))]
    [ClassificationType(ClassificationTypeNames = BracketColorNames.Six)]
    [Name(BracketColorNames.Six)]
    [UserVisible(true)]
    internal sealed class BracketFormatSix : BracketFormat { public BracketFormatSix() : base(75, 125, 215) { } }

    [Export(typeof(ITaggerProvider))]
    [ContentType("SQL")]
    [TagType(typeof(ClassificationTag))]
    internal sealed class BracketColorTaggerProvider : ITaggerProvider
    {
        [Import]
        internal IClassificationTypeRegistryService Registry = null;

        public ITagger<T> CreateTagger<T>(ITextBuffer buffer) where T : ITag =>
            buffer.Properties.GetOrCreateSingletonProperty(() => new BracketColorTagger(buffer, Registry)) as ITagger<T>;
    }

    internal sealed class BracketColorTagger : ITagger<ClassificationTag>
    {
        private readonly ITextBuffer _buffer;
        private readonly ClassificationTag[] _tags;
        private readonly object _cacheLock = new object();
        private ITextSnapshot _cachedSnapshot;
        private IReadOnlyList<BracketColorSpan> _cachedBrackets;

        public BracketColorTagger(ITextBuffer buffer, IClassificationTypeRegistryService registry)
        {
            _buffer = buffer;
            var names = new[] { BracketColorNames.One, BracketColorNames.Two, BracketColorNames.Three,
                BracketColorNames.Four, BracketColorNames.Five, BracketColorNames.Six };
            _tags = new ClassificationTag[names.Length];
            for (var i = 0; i < names.Length; i++)
                _tags[i] = new ClassificationTag(registry.GetClassificationType(names[i]));
            _buffer.Changed += OnBufferChanged;
        }

        public event EventHandler<SnapshotSpanEventArgs> TagsChanged;

        private void OnBufferChanged(object sender, TextContentChangedEventArgs e)
        {
            lock (_cacheLock)
            {
                _cachedSnapshot = null;
                _cachedBrackets = null;
            }
            TagsChanged?.Invoke(this, new SnapshotSpanEventArgs(new SnapshotSpan(e.After, 0, e.After.Length)));
        }

        public IEnumerable<ITagSpan<ClassificationTag>> GetTags(NormalizedSnapshotSpanCollection spans)
        {
            if (spans.Count == 0) yield break;
            var snapshot = spans[0].Snapshot;
            IReadOnlyList<BracketColorSpan> brackets;
            lock (_cacheLock)
            {
                if (_cachedSnapshot != snapshot)
                {
                    _cachedBrackets = BracketColorizer.Find(snapshot.GetText());
                    _cachedSnapshot = snapshot;
                }
                brackets = _cachedBrackets;
            }

            var spanIndex = 0;
            foreach (var bracket in brackets)
            {
                while (spanIndex < spans.Count && spans[spanIndex].End.Position <= bracket.Start)
                    spanIndex++;
                if (spanIndex == spans.Count) yield break;
                if (spans[spanIndex].Start.Position <= bracket.Start)
                    yield return new TagSpan<ClassificationTag>(
                        new SnapshotSpan(snapshot, bracket.Start, 1), _tags[bracket.Level % _tags.Length]);
            }
        }
    }
}
