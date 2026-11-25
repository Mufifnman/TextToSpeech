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
        // GUESS: colours chosen for contrast on a light theme; easy to change here.
        private static readonly Brush SentenceBrush = new SolidColorBrush(Color.FromRgb(0xFF, 0xF3, 0xB0));
        private static readonly Brush WordBrush = new SolidColorBrush(Color.FromRgb(0xFF, 0xA0, 0x00));
        private static readonly Brush HoverBrush = new SolidColorBrush(Color.FromRgb(0xCF, 0xE8, 0xFF));
        private static readonly Brush ReadBrush = new SolidColorBrush(Color.FromRgb(0x70, 0x70, 0x70));

        private readonly TextBlock textBlock = new() { TextWrapping = TextWrapping.Wrap, Padding = new Thickness(6) };
        private readonly List<List<Run>> runs = new();
        private ReadingDocument? document;
        private int styledSentence = -1;

        public ReadingDisplay()
        {
            Content = textBlock;
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto;
            Background = Brushes.WhiteSmoke;
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

        private void OnContentChanged(object? sender, EventArgs e) => Rebuild();

        private void OnPositionChanged(object? sender, EventArgs e) => RestyleChanged();

        private void Rebuild()
        {
            textBlock.Inlines.Clear();
            runs.Clear();
            styledSentence = -1;
            if (document == null)
            {
                return;
            }

            // GUESS: rebuilding all runs on content change is fine for typical pages.
            // Position changes (every spoken word) only restyle two sentences, see RestyleChanged.
            for (int s = 0; s < document.Count; s++)
            {
                var sentence = document.Sentences[s];
                var list = new List<Run>(sentence.Words.Count);
                for (int w = 0; w < sentence.Words.Count; w++)
                {
                    var run = new Run(sentence.WordText(w)) { Tag = (s, w), Cursor = Cursors.Hand };
                    run.MouseLeftButtonUp += OnRunClicked;
                    run.MouseEnter += (_, _) => run.Background = HoverBrush;
                    int sIdx = s, wIdx = w;
                    run.MouseLeave += (_, _) => ApplyStyle(run, sIdx, wIdx);
                    list.Add(run);
                    textBlock.Inlines.Add(run);
                    textBlock.Inlines.Add(new Run(" "));
                }
                runs.Add(list);
            }
            RestyleAll();
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
        }

        private void ApplyStyle(Run run, int s, int w)
        {
            var doc = document!;
            int current = doc.Position.Sentence;
            bool isCurrentSentence = s == current;
            bool isCurrentWord = isCurrentSentence && w == doc.HighlightedWord;

            // GUESS: hover feedback is skipped on the word being spoken so it stays unmistakable.
            run.Background = isCurrentWord ? WordBrush : isCurrentSentence ? SentenceBrush : null;
            run.FontWeight = isCurrentWord ? FontWeights.Bold : FontWeights.Normal;
            run.TextDecorations = isCurrentWord ? TextDecorations.Underline : null;
            run.Foreground = s < current ? ReadBrush : Brushes.Black;
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
