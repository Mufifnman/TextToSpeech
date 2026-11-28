using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using TextToSpeech.Core.Reading;
using TextToSpeech.Core.Shell;

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
        private bool following = true;
        private bool scrollQueued;
        private FollowButtonAdorner? followButton;

        public ReadingDisplay()
        {
            Content = textBlock;
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto;
            SetResourceReference(BackgroundProperty, "ThemeSurface");
            Loaded += (_, _) => UpdateFollowButton();
            Unloaded += (_, _) => RemoveFollowButton();
        }

        /// <summary>
        /// Whether the view follows the text being read. Scrolling by hand (wheel, scroll bar, touch) turns it off and a
        /// "Follow" button appears to turn it back on; a new reading, a click on a word or that button turn it on again.
        /// </summary>
        public bool Following
        {
            get => following;
            private set
            {
                if (following == value)
                {
                    return;
                }
                following = value;
                UpdateFollowButton();
                if (following)
                {
                    QueueFollow();
                }
            }
        }

        protected override void OnPreviewMouseDown(MouseButtonEventArgs e)
        {
            // Dragging or clicking the scroll bar is a manual scroll.
            if (e.OriginalSource is DependencyObject source && IsInScrollBar(source))
            {
                Following = false;
            }
            base.OnPreviewMouseDown(e);
        }

        protected override void OnManipulationStarting(ManipulationStartingEventArgs e)
        {
            Following = false;
            base.OnManipulationStarting(e);
        }

        private static bool IsInScrollBar(DependencyObject node)
        {
            for (var d = node; d != null; d = VisualTreeHelper.GetParent(d))
            {
                if (d is System.Windows.Controls.Primitives.ScrollBar)
                {
                    return true;
                }
            }
            return false;
        }

        private void UpdateFollowButton()
        {
            if (!IsLoaded)
            {
                return;
            }
            if (following)
            {
                RemoveFollowButton();
                return;
            }
            var layer = AdornerLayer.GetAdornerLayer(this);
            if (layer == null || followButton != null)
            {
                return;
            }
            followButton = new FollowButtonAdorner(this, () => Following = true);
            layer.Add(followButton);
        }

        private void RemoveFollowButton()
        {
            if (followButton != null)
            {
                AdornerLayer.GetAdornerLayer(this)?.Remove(followButton);
                followButton = null;
            }
        }

        /// <summary>The small "Follow" button shown at the bottom right of the view while following is off.</summary>
        private sealed class FollowButtonAdorner : Adorner
        {
            private readonly Button button;

            public FollowButtonAdorner(UIElement host, Action relock) : base(host)
            {
                button = new Button
                {
                    Content = "\u2193 Follow reading",
                    Padding = new Thickness(10, 3, 10, 3),
                    Focusable = false,
                    Cursor = Cursors.Hand,
                    ToolTip = "Scroll back to the text being read and keep following it",
                };
                System.Windows.Automation.AutomationProperties.SetName(button, "Follow the text being read");
                button.Click += (_, _) => relock();
                AddVisualChild(button);
            }

            protected override int VisualChildrenCount => 1;

            protected override Visual GetVisualChild(int index) => button;

            protected override Size MeasureOverride(Size constraint)
            {
                button.Measure(constraint);
                return constraint;
            }

            protected override Size ArrangeOverride(Size finalSize)
            {
                var size = button.DesiredSize;
                // Left of the scroll bar and clear of the text's edge.
                button.Arrange(new Rect(Math.Max(0, finalSize.Width - size.Width - 24), Math.Max(0, finalSize.Height - size.Height - 12), size.Width, size.Height));
                return finalSize;
            }
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
            if (ScrollableHeight > 0)
            {
                Following = false;   // scrolling by hand: stop following until the Follow button is pressed
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
            if (runs.Count == 0 && document is { Count: > 0 })
            {
                Following = true;   // a new reading starts out following
            }
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
                Following = true;   // choosing where to read from means following again
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
            if (following)
            {
                QueueFollow();
            }
        }

        /// <summary>Scrolls once layout is up to date (several changes in a row cost one scroll).</summary>
        private void QueueFollow()
        {
            if (scrollQueued)
            {
                return;
            }
            scrollQueued = true;
            Dispatcher.BeginInvoke(new Action(() =>
            {
                scrollQueued = false;
                FollowCurrent();
            }), System.Windows.Threading.DispatcherPriority.Loaded);
        }

        private void FollowCurrent()
        {
            if (!following || document == null || document.IsAtEnd || document.Position.Sentence >= runs.Count || runs[document.Position.Sentence].Count == 0)
            {
                return;
            }
            var words = runs[document.Position.Sentence];
            try
            {
                UpdateLayout();
                var first = words[0].ContentStart.GetCharacterRect(LogicalDirection.Forward);
                var last = words[^1].ContentEnd.GetCharacterRect(LogicalDirection.Backward);
                if (first.IsEmpty || last.IsEmpty)
                {
                    return;
                }
                // The text block is the scrolled content, so its coordinates are content coordinates.
                var target = FollowScroll.TargetOffset(first.Top, Math.Max(first.Bottom, last.Bottom), VerticalOffset, ViewportHeight);
                if (target is double y)
                {
                    ScrollToVerticalOffset(y);
                }
            }
            catch (InvalidOperationException)
            {
                // The runs were replaced while this waited; the next change scrolls again.
            }
        }
    }
}
