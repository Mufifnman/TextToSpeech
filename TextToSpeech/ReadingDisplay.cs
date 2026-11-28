using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using TextToSpeech.Core.Reading;

namespace TextToSpeech
{
    /// <summary>
    /// Shows a <see cref="ReadingDocument"/>: every word is its own run, so clicking any
    /// word jumps there. The sentence being read is tinted, and the word being spoken gets
    /// a strong colour, bold and an underline so it is easy to find at a glance.
    /// </summary>
    public sealed class ReadingDisplay : ScrollViewer
    {
        // Colors come from the theme (light or dark); the fallbacks only matter if a resource is missing.
        private Brush SentenceBrush => (Brush?)TryFindResource("ReadSentenceBrush") ?? Brushes.Khaki;
        private Brush WordBrush => (Brush?)TryFindResource("ReadWordBrush") ?? Brushes.Orange;
        private Brush HoverBrush => (Brush?)TryFindResource("ReadHoverBrush") ?? Brushes.LightSkyBlue;
        private Brush ReadBrush => (Brush?)TryFindResource("ReadReadBrush") ?? Brushes.Gray;
        private Brush TextBrush => (Brush?)TryFindResource("ThemeText") ?? Brushes.Black;

        private readonly TextBlock textBlock = new() { TextWrapping = TextWrapping.Wrap, Padding = new Thickness(6) };
        private readonly List<List<Run>> runs = new();
        private readonly Dictionary<int, Run> pauseMarks = new();   // sentence index -> the small marker after a pause point
        private ReadingDocument? document;
        private int styledSentence = -1;

        public ReadingDisplay()
        {
            Content = textBlock;
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto;
            SetResourceReference(BackgroundProperty, "ThemeSurface");
        }

        /// <summary>Font size of the reading text.</summary>
        public double TextSize
        {
            get => textBlock.FontSize;
            set
            {
                if (Math.Abs(textBlock.FontSize - value) < 0.01)
                {
                    return;
                }
                textBlock.FontSize = value;
                if (document != null)
                {
                    Rebuild();   // headings are sized relative to the text, so they are rebuilt
                }
            }
        }

        /// <summary>Raised when the user turns the mouse wheel with Ctrl held over the text: +1 to grow, -1 to shrink. Only set where zooming is wanted.</summary>
        public event Action<int>? ZoomRequested;

        protected override void OnPreviewMouseWheel(MouseWheelEventArgs e)
        {
            if (ZoomRequested != null && (Keyboard.Modifiers & ModifierKeys.Control) != 0)
            {
                ZoomRequested(e.Delta > 0 ? 1 : -1);
                e.Handled = true;
                return;
            }
            base.OnPreviewMouseWheel(e);
        }

        /// <summary>Raised when the user clicks a word. Arguments are the sentence and word index.</summary>
        public event Action<int, int>? WordClicked;

        public ReadingDocument? Document
        {
            get => document;
            set
            {
                if (document != null)
                {
                    document.ContentChanged -= OnContentChanged;
                    document.PositionChanged -= OnPositionChanged;
                }
                document = value;
                if (document != null)
                {
                    document.ContentChanged += OnContentChanged;
                    document.PositionChanged += OnPositionChanged;
                }
                Rebuild();
            }
        }

        /// <summary>Re-applies colors after the Windows theme changed.</summary>
        public void RefreshTheme()
        {
            if (document != null && runs.Count > 0)
            {
                RestyleAll();
            }
        }

        private void OnContentChanged(object? sender, EventArgs e) => Rebuild();

        private void OnPositionChanged(object? sender, EventArgs e) => RestyleChanged();

        private void Rebuild()
        {
            textBlock.Inlines.Clear();
            runs.Clear();
            pauseMarks.Clear();
            styledSentence = -1;
            if (document == null)
            {
                return;
            }

            // GUESS: rebuilding all runs on content change is fine for typical pages.
            // Position changes (every spoken word) only restyle two sentences, see RestyleChanged.
            SentenceFormat? previous = null;
            for (int s = 0; s < document.Count; s++)
            {
                var sentence = document.Sentences[s];
                var format = sentence.Format;
                if (format is { StartsBlock: true })
                {
                    StartBlock(format, previous);
                }
                previous = format ?? previous;

                double size = SizeFor(format);
                var list = new List<Run>(sentence.Words.Count);
                for (int w = 0; w < sentence.Words.Count; w++)
                {
                    var run = new Run(sentence.WordText(w)) { Tag = (s, w), Cursor = Cursors.Hand };
                    if (size != textBlock.FontSize)
                    {
                        run.FontSize = size;
                    }
                    if (format?.Kind == BlockKind.Code)
                    {
                        run.FontFamily = CodeFont;
                    }
                    run.MouseLeftButtonUp += OnRunClicked;
                    run.MouseEnter += (_, _) => run.Background = HoverBrush;
                    int sIdx = s, wIdx = w;
                    run.MouseLeave += (_, _) => ApplyStyle(run, sIdx, wIdx);
                    list.Add(run);
                    textBlock.Inlines.Add(run);
                    textBlock.Inlines.Add(new Run(" "));
                }
                runs.Add(list);
                if (sentence.Pause != null)
                {
                    var mark = new Run("\u23F8 ") { ToolTip = sentence.Pause.Label == null ? "Reading waits here" : "Reading waits here for your answer" };
                    pauseMarks[s] = mark;
                    textBlock.Inlines.Add(mark);
                }
            }
            RestyleAll();
        }

        private static readonly FontFamily CodeFont = new("Consolas");

        /// <summary>Starts a new line for a markdown block: a line break (two between different kinds of block), then the indent and marker of a list item.</summary>
        private void StartBlock(SentenceFormat format, SentenceFormat? previous)
        {
            if (textBlock.Inlines.Count > 0)
            {
                bool tight = previous != null && previous.Kind == format.Kind && format.Kind is BlockKind.ListItem or BlockKind.Code;
                textBlock.Inlines.Add(new LineBreak());
                if (!tight)
                {
                    textBlock.Inlines.Add(new LineBreak());
                }
            }
            if (format.Kind == BlockKind.ListItem)
            {
                string indent = new string('\u00A0', Math.Clamp(format.Level, 0, 8) * 4);
                textBlock.Inlines.Add(new Run(indent + (format.Marker ?? "\u2022") + "\u00A0") { Foreground = SubtleBrush });
            }
        }

        private Brush SubtleBrush => (Brush?)TryFindResource("ThemeSubtle") ?? Brushes.Gray;

        /// <summary>Headings are bigger than the text; everything else is the normal size.</summary>
        private double SizeFor(SentenceFormat? format)
        {
            if (format?.Kind != BlockKind.Heading)
            {
                return textBlock.FontSize;
            }
            double factor = format.Level switch { 1 => 1.5, 2 => 1.3, 3 => 1.15, _ => 1.05 };
            return textBlock.FontSize * factor;
        }

        private void OnRunClicked(object sender, MouseButtonEventArgs e)
        {
            if (sender is Run { Tag: ValueTuple<int, int> pos })
            {
                WordClicked?.Invoke(pos.Item1, pos.Item2);
                e.Handled = true;
            }
        }

        private void RestyleAll()
        {
            for (int s = 0; s < runs.Count; s++)
            {
                StyleSentence(s);
            }
            RememberStyled();
            ScrollCurrentIntoView();
        }

        private void RestyleChanged()
        {
            if (document == null)
            {
                return;
            }
            // Restyle everything between the old and new sentence (jumps can cross many),
            // plus one before, which flips from "current" to "read" colour.
            int now = Math.Min(document.Position.Sentence, runs.Count - 1);
            int from = Math.Max(0, Math.Min(styledSentence < 0 ? now : styledSentence, now) - 1);
            int to = Math.Min(runs.Count - 1, Math.Max(styledSentence, now));
            for (int s = from; s <= to; s++)
            {
                StyleSentence(s);
            }
            bool sentenceChanged = styledSentence != document.Position.Sentence;
            RememberStyled();
            if (sentenceChanged)
            {
                ScrollCurrentIntoView();
            }
        }

        private void RememberStyled()
        {
            styledSentence = document?.Position.Sentence ?? -1;
        }

        private void StyleSentence(int s)
        {
            for (int w = 0; w < runs[s].Count; w++)
            {
                ApplyStyle(runs[s][w], s, w);
            }
            if (pauseMarks.TryGetValue(s, out var mark) && document != null)
            {
                bool answered = document.Sentences[s].Pause?.Answered == true;
                mark.Text = answered ? "\u2713 " : "\u23F8 ";
                mark.Foreground = answered ? ReadBrush : WordBrush;
            }
        }

        private void ApplyStyle(Run run, int s, int w)
        {
            var doc = document!;
            int current = doc.Position.Sentence;
            bool isCurrentSentence = s == current;
            bool isCurrentWord = isCurrentSentence && w == doc.HighlightedWord;

            // GUESS: hover feedback is skipped on the word being spoken so it stays unmistakable.
            run.Background = isCurrentWord ? WordBrush : isCurrentSentence ? SentenceBrush : null;
            bool heading = doc.Sentences[s].Format?.Kind == BlockKind.Heading;
            run.FontWeight = isCurrentWord || heading ? FontWeights.Bold : FontWeights.Normal;
            run.TextDecorations = isCurrentWord ? TextDecorations.Underline : null;
            run.Foreground = s < current ? ReadBrush : TextBrush;
        }

        private void ScrollCurrentIntoView()
        {
            if (document == null || document.IsAtEnd || document.Position.Sentence >= runs.Count || runs[document.Position.Sentence].Count == 0)
            {
                return;
            }
            // GUESS: bring the sentence start into view as reading advances. Scrolling by hand
            // while reading is still overridden at the next sentence; revisit if that annoys.
            runs[document.Position.Sentence][0].BringIntoView();
        }
    }
}
