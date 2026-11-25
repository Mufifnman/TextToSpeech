namespace TextToSpeech.Core.Reading
{
    /// <summary>A place in a <see cref="ReadingDocument"/>: a sentence and a word inside it.</summary>
    public readonly record struct ReadingPosition(int Sentence, int Word);

    /// <summary>
    /// The text being read, as a list of sentences, plus where the reader is.
    ///
    /// Replaces the old <c>Queue&lt;string&gt;</c> in MainWindow. Nothing is removed as it is
    /// read, so the user can rewind, skip or jump to any word. It knows nothing about
    /// WPF or about which speech engine is used: the window asks it what to speak next
    /// (<see cref="GetTextToSpeak"/>) and reports progress back
    /// (<see cref="ReportSpokenCharacter"/>, <see cref="CompleteSentence"/>).
    ///
    /// Not thread safe: call it from one thread (the UI thread).
    /// </summary>
    public sealed class ReadingDocument
    {
        private readonly List<Sentence> sentences = new();

        /// <summary>Raised after sentences are added or the document is cleared.</summary>
        public event EventHandler? ContentChanged;

        /// <summary>Raised whenever <see cref="Position"/> or <see cref="SpokenWord"/> changes.</summary>
        public event EventHandler? PositionChanged;

        public IReadOnlyList<Sentence> Sentences => sentences;

        public int Count => sentences.Count;

        /// <summary>
        /// The current sentence, and the word speaking starts from.
        /// <c>Position.Sentence == Count</c> means everything has been read.
        /// </summary>
        public ReadingPosition Position { get; private set; }

        /// <summary>
        /// The word the speech engine most recently reported, or -1 when nothing in the
        /// current sentence has been spoken yet. Used for the word highlight.
        /// </summary>
        public int SpokenWord { get; private set; } = -1;

        public bool IsAtEnd => Position.Sentence >= sentences.Count;

        public Sentence? CurrentSentence => IsAtEnd ? null : sentences[Position.Sentence];

        /// <summary>The word to highlight: the spoken word if known, otherwise where speaking will start.</summary>
        public int HighlightedWord => SpokenWord >= 0 ? SpokenWord : Position.Word;

        /// <summary>Splits <paramref name="text"/> into sentences and adds them at the end. Returns how many were added.</summary>
        public int Append(string? text, Func<string, bool>? keepSentence = null)
        {
            int added = 0;
            foreach (var s in SentenceSplitter.Split(text))
            {
                if (keepSentence != null && !keepSentence(s))
                {
                    continue;
                }
                sentences.Add(new Sentence(s));
                added++;
            }
            if (added > 0)
            {
                ContentChanged?.Invoke(this, EventArgs.Empty);
                // When the reader was at the end, Position now points at the first new sentence.
                PositionChanged?.Invoke(this, EventArgs.Empty);
            }
            return added;
        }

        public void Clear()
        {
            sentences.Clear();
            Position = new ReadingPosition(0, 0);
            SpokenWord = -1;
            ContentChanged?.Invoke(this, EventArgs.Empty);
            PositionChanged?.Invoke(this, EventArgs.Empty);
        }

        /// <summary>True when any sentence contains <paramref name="fragment"/> (used to avoid re-adding clipboard text).</summary>
        public bool Contains(string? fragment)
        {
            if (string.IsNullOrWhiteSpace(fragment))
            {
                return false;
            }
            string f = fragment.Trim();
            return sentences.Any(s => s.Text.Contains(f, StringComparison.Ordinal));
        }

        /// <summary>
        /// Removes every sentence from the current one onward (what is left to read) and returns its text,
        /// so it can be handed back to the editable box. Sentences already read stay. The whole current
        /// sentence comes back, even if it was started.
        /// </summary>
        public string TakeUnread()
        {
            int from = Math.Min(Position.Sentence, sentences.Count);
            string text = string.Join(" ", sentences.Skip(from).Select(s => s.Text));
            if (from < sentences.Count)
            {
                sentences.RemoveRange(from, sentences.Count - from);
                SpokenWord = -1;
                Position = new ReadingPosition(sentences.Count, 0);
                ContentChanged?.Invoke(this, EventArgs.Empty);
                PositionChanged?.Invoke(this, EventArgs.Empty);
            }
            return text;
        }

        /// <summary>Returns all the text (read and unread) and empties the document.</summary>
        public string TakeAll()
        {
            string text = string.Join(" ", sentences.Select(s => s.Text));
            Clear();
            return text;
        }

        /// <summary>
        /// Like <see cref="Contains"/> but only looks at sentences not yet finished (the current one onward).
        /// The old queue dropped sentences as they were read, so text that was already read could be
        /// copied and picked up again; this keeps that behavior now that read sentences stay for rewind.
        /// </summary>
        public bool ContainsUnread(string? fragment)
        {
            if (string.IsNullOrWhiteSpace(fragment))
            {
                return false;
            }
            string f = fragment.Trim();
            for (int i = Position.Sentence; i < sentences.Count; i++)
            {
                if (sentences[i].Text.Contains(f, StringComparison.Ordinal))
                {
                    return true;
                }
            }
            return false;
        }

        /// <summary>
        /// The text to hand to the speech engine for the current sentence, starting at
        /// <see cref="Position"/>.Word. <paramref name="offset"/> is where that text starts
        /// inside the sentence; add it to engine character positions before calling
        /// <see cref="ReportSpokenCharacter"/>. Returns null at the end of the document.
        /// </summary>
        public string? GetTextToSpeak(out int offset)
        {
            offset = 0;
            var s = CurrentSentence;
            if (s == null)
            {
                return null;
            }
            if (Position.Word > 0 && Position.Word < s.Words.Count)
            {
                offset = s.Words[Position.Word].Start;
            }
            return s.Text.Substring(offset);
        }

        /// <summary>Speech engine reached <paramref name="charIndexInSentence"/> of the current sentence.</summary>
        public void ReportSpokenCharacter(int charIndexInSentence)
        {
            var s = CurrentSentence;
            if (s == null)
            {
                return;
            }
            int word = s.WordIndexAt(charIndexInSentence);
            if (word != SpokenWord)
            {
                SpokenWord = word;
                PositionChanged?.Invoke(this, EventArgs.Empty);
            }
        }

        /// <summary>
        /// Call when speech is stopped part way through a sentence (pause/stop): moves the
        /// start point to the word that was being spoken, so resuming continues from there
        /// instead of repeating the start of the sentence.
        /// </summary>
        public void MarkStoppedAtSpokenWord()
        {
            if (!IsAtEnd && SpokenWord >= 0)
            {
                Position = new ReadingPosition(Position.Sentence, SpokenWord);
                SpokenWord = -1;
                PositionChanged?.Invoke(this, EventArgs.Empty);
            }
        }

        /// <summary>The current sentence finished speaking: move to the start of the next one.</summary>
        public void CompleteSentence() => MoveTo(Position.Sentence + 1, 0);

        /// <summary>Skip forward to the start of the next sentence. Returns false if already past the last one.</summary>
        public bool Skip()
        {
            if (IsAtEnd)
            {
                return false;
            }
            MoveTo(Position.Sentence + 1, 0);
            return true;
        }

        /// <summary>
        /// Go back. Like a music player: if speaking has got past the first word of the
        /// current sentence, restart that sentence; otherwise go to the previous sentence.
        /// GUESS: restarting the current sentence first is the most useful for a reading aid
        /// (re-hear what you just missed). Pressing again before the second word goes back a sentence.
        /// </summary>
        public void Rewind()
        {
            if (sentences.Count == 0)
            {
                return;
            }
            bool intoSentence = !IsAtEnd && (SpokenWord > 0 || Position.Word > 0);
            if (intoSentence)
            {
                MoveTo(Position.Sentence, 0);
            }
            else
            {
                MoveTo(Math.Max(0, Math.Min(Position.Sentence, sentences.Count) - 1), 0);
            }
        }

        /// <summary>Jump to a specific word (click-to-jump).</summary>
        public void JumpTo(int sentence, int word = 0)
        {
            if (sentence < 0 || sentence >= sentences.Count)
            {
                throw new ArgumentOutOfRangeException(nameof(sentence));
            }
            int words = sentences[sentence].Words.Count;
            MoveTo(sentence, Math.Clamp(word, 0, Math.Max(0, words - 1)));
        }

        /// <summary>
        /// If the reader is on the last sentence (or past it) and that sentence has no closing
        /// punctuation, removes and returns it. Used when reading screen captures page by
        /// page: a sentence cut off at the bottom of one capture is joined to the next, so
        /// the caller appends "tail + new text" and the cut sentence is read whole.
        /// </summary>
        public string? TakeIncompleteTail()
        {
            if (sentences.Count == 0 || Position.Sentence < sentences.Count - 1 || sentences[^1].EndsComplete)
            {
                return null;
            }
            string text = sentences[^1].Text;
            sentences.RemoveAt(sentences.Count - 1);
            Position = new ReadingPosition(sentences.Count, 0);
            SpokenWord = -1;
            ContentChanged?.Invoke(this, EventArgs.Empty);
            PositionChanged?.Invoke(this, EventArgs.Empty);
            return text;
        }

        private void MoveTo(int sentence, int word)
        {
            sentence = Math.Clamp(sentence, 0, sentences.Count);
            Position = new ReadingPosition(sentence, sentence >= sentences.Count ? 0 : word);
            SpokenWord = -1;
            PositionChanged?.Invoke(this, EventArgs.Empty);
        }
    }
}
