namespace TextToSpeech.Core.Reading
{
    /// <summary>A place in a <see cref="ReadingDocument"/>: a sentence and a word inside it.</summary>
    public readonly record struct ReadingPosition(int Sentence, int Word);

    /// <summary>Where the caller wants reading to stop: after the block of the text that contains offset <paramref name="At"/>.</summary>
    public sealed record PauseSpec(string Id, string? Label, int At);

    /// <summary>Spoken form of list numbers.</summary>
    public static class NumberWords
    {
        private static readonly string[] Words =
        {
            "zero", "one", "two", "three", "four", "five", "six", "seven", "eight", "nine", "ten",
            "eleven", "twelve", "thirteen", "fourteen", "fifteen", "sixteen", "seventeen", "eighteen", "nineteen", "twenty",
        };

        /// <summary>"One" for 1, up to twenty; larger numbers stay digits (the voice reads them).</summary>
        public static string Spoken(int n)
        {
            string w = n >= 0 && n < Words.Length ? Words[n] : n.ToString(System.Globalization.CultureInfo.InvariantCulture);
            return char.ToUpperInvariant(w[0]) + w.Substring(1);
        }
    }

    /// <summary>
    /// One owner's text divided into what has been read and what has not. <see cref="FirstIndex"/> is the document
    /// index of the first unread sentence (-1 when everything was read), <see cref="Word"/> the word in it the
    /// remainder starts at, <see cref="CharOffset"/> the start of the remainder inside the owner's full text.
    /// </summary>
    public readonly record struct OwnerSplit(string ReadText, string Remainder, int FirstIndex, int Word, int CharOffset);

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
        public int Append(string? text, Func<string, bool>? keepSentence = null, string? owner = null) =>
            InsertAt(sentences.Count, text, keepSentence, owner);

        /// <summary>
        /// Splits <paramref name="text"/> and inserts the sentences at <paramref name="index"/> (clamped to the end).
        /// Inserting before the reader shifts its position along; inserting exactly at the reader puts the new
        /// text next to be read (the reader starts at the first new sentence, at its first word).
        /// Returns how many sentences were added.
        /// </summary>
        public int InsertAt(int index, string? text, Func<string, bool>? keepSentence = null, string? owner = null)
        {
            var added = new List<Sentence>();
            foreach (var s in SentenceSplitter.Split(text))
            {
                if (keepSentence != null && !keepSentence(s))
                {
                    continue;
                }
                added.Add(new Sentence(s, owner));
            }
            return InsertSentences(index, added);
        }

        /// <summary>Like <see cref="Append"/> for text that may be markdown and may carry pause points; see <see cref="InsertMarkdownAt"/>.</summary>
        public int AppendMarkdown(string? text, bool markdown = true, IReadOnlyList<PauseSpec>? pauses = null,
            Func<string, bool>? keepSentence = null, string? owner = null) =>
            InsertMarkdownAt(sentences.Count, text, markdown, pauses, keepSentence, owner);

        /// <summary>
        /// Inserts text as laid-out sentences. With <paramref name="markdown"/> the markup is removed (it is never
        /// spoken or shown), list items become separate lines (numbered items are spoken with their number and
        /// followed by a longer pause), headings and code are marked for display. Without it the text is only split
        /// into paragraphs and sentences as plain text.
        /// Each <see cref="PauseSpec"/> puts a <see cref="PausePoint"/> after the last sentence of the block (paragraph,
        /// list item, heading or code line) that contains its <see cref="PauseSpec.At"/> offset in <paramref name="text"/>.
        /// A second pause in the same block is ignored. Returns how many sentences were added.
        /// </summary>
        public int InsertMarkdownAt(int index, string? text, bool markdown = true, IReadOnlyList<PauseSpec>? pauses = null,
            Func<string, bool>? keepSentence = null, string? owner = null)
        {
            var blocks = MarkdownReader.Parse(text, markdown);
            // Text with nothing but plain paragraphs has no layout to show: keep it as flowing text, as plain text always was.
            bool laidOut = markdown && blocks.Any(x => x.Kind != BlockKind.Paragraph);
            var added = new List<Sentence>();
            var lastOfBlock = new int[blocks.Count];   // index in added of each block's last sentence, -1 when none
            for (int b = 0; b < blocks.Count; b++)
            {
                var block = blocks[b];
                var parts = new List<string>();
                IEnumerable<string> raw = block.Kind == BlockKind.Code ? new[] { block.Text } : SentenceSplitter.Split(block.Text);
                foreach (var part in raw)
                {
                    if (keepSentence == null || keepSentence(part))
                    {
                        parts.Add(part);
                    }
                }
                lastOfBlock[b] = -1;
                for (int k = 0; k < parts.Count; k++)
                {
                    bool first = k == 0;
                    bool last = k == parts.Count - 1;
                    SentenceFormat? format = null;
                    if (laidOut)
                    {
                        string prefix = first && block.Ordinal is int n ? NumberWords.Spoken(n) + ". " : "";
                        int factor = last && block.Kind == BlockKind.ListItem ? 2 : 1;
                        format = new SentenceFormat(block.Kind, block.Level, first ? block.Marker : null, first, prefix, factor);
                    }
                    added.Add(new Sentence(parts[k], owner, format));
                    lastOfBlock[b] = added.Count - 1;
                }
            }

            if (pauses != null)
            {
                foreach (var spec in pauses)
                {
                    int block = blocks.Count == 0 ? -1 : blocks.ToList().FindLastIndex(x => x.SourceStart <= spec.At);
                    if (block < 0 && blocks.Count > 0)
                    {
                        block = 0;
                    }
                    int target = -1;
                    for (int j = block; j >= 0 && target < 0; j--)
                    {
                        target = lastOfBlock[j];
                    }
                    for (int j = block + 1; j < blocks.Count && target < 0; j++)
                    {
                        target = lastOfBlock[j];
                    }
                    if (target >= 0 && added[target].Pause == null)
                    {
                        added[target].Pause = new PausePoint(spec.Id, spec.Label);
                    }
                }
            }
            return InsertSentences(index, added);
        }

        private int InsertSentences(int index, List<Sentence> added)
        {
            index = Math.Clamp(index, 0, sentences.Count);
            if (added.Count == 0)
            {
                return 0;
            }
            sentences.InsertRange(index, added);
            if (index < Position.Sentence)
            {
                Position = new ReadingPosition(Position.Sentence + added.Count, Position.Word);
            }
            else if (index == Position.Sentence && index < sentences.Count - added.Count)
            {
                // New text goes in front of a sentence the reader was on (maybe part way through): start the new text.
                Position = new ReadingPosition(index, 0);
                SpokenWord = -1;
            }
            ContentChanged?.Invoke(this, EventArgs.Empty);
            // When the reader was at the end, Position now points at the first new sentence.
            PositionChanged?.Invoke(this, EventArgs.Empty);
            return added.Count;
        }

        /// <summary>
        /// The pause points not yet answered, in reading order, with the document index of the sentence each follows.
        /// <paramref name="owner"/> limits them to one API session.
        /// </summary>
        public IReadOnlyList<(int Index, Sentence Sentence)> Pauses(string? owner = null, bool pendingOnly = true)
        {
            var result = new List<(int, Sentence)>();
            for (int i = 0; i < sentences.Count; i++)
            {
                var s = sentences[i];
                if (s.Pause != null && (owner == null || s.Owner == owner) && (!pendingOnly || !s.Pause.Answered))
                {
                    result.Add((i, s));
                }
            }
            return result;
        }

        /// <summary>The full text of one owner's sentences, joined with single spaces.</summary>
        public string OwnerText(string owner) =>
            string.Join(" ", sentences.Where(s => s.Owner == owner).Select(s => s.Text));

        /// <summary>How many sentences belong to <paramref name="owner"/>.</summary>
        public int OwnerSentenceCount(string owner) => sentences.Count(s => s.Owner == owner);

        /// <summary>Owner of the sentence the reader is on (null at the end, or for text not added by the API).</summary>
        public string? CurrentOwner => CurrentSentence?.Owner;

        /// <summary>
        /// Where one owner's unread text starts, without changing anything. <see cref="OwnerSplit.ReadText"/> plus
        /// <see cref="OwnerSplit.Remainder"/> is always <see cref="OwnerText"/>. With
        /// <paramref name="fromSentenceStart"/> the remainder begins at the start of its first unread sentence;
        /// otherwise at the word the reader is on, when that sentence is the current one.
        /// </summary>
        public OwnerSplit PeekOwner(string owner, bool fromSentenceStart = false)
        {
            var mine = new List<(int Index, Sentence Sentence)>();
            for (int i = 0; i < sentences.Count; i++)
            {
                if (sentences[i].Owner == owner)
                {
                    mine.Add((i, sentences[i]));
                }
            }
            string full = string.Join(" ", mine.Select(m => m.Sentence.Text));

            int first = mine.FindIndex(m => m.Index >= Position.Sentence);
            if (first < 0)
            {
                return new OwnerSplit(full, "", -1, 0, full.Length);
            }

            int before = 0;
            for (int k = 0; k < first; k++)
            {
                before += mine[k].Sentence.Text.Length + 1;
            }

            var (index, sentence) = mine[first];
            int word = 0;
            if (!fromSentenceStart && index == Position.Sentence && Position.Word > 0 && Position.Word < sentence.Words.Count)
            {
                word = Position.Word;
            }
            int readChars = before + (word > 0 ? sentence.Words[word].Start : 0);
            return new OwnerSplit(full.Substring(0, readChars), full.Substring(readChars), index, word, readChars);
        }

        /// <summary>
        /// Removes every sentence of <paramref name="owner"/> from document index <paramref name="index"/> on (used to
        /// replace the not yet read tail of an API session). Keeps the reader on the same sentence, or on whatever
        /// follows when its own sentence was removed. Returns how many were removed.
        /// </summary>
        public int RemoveOwnerFrom(string owner, int index)
        {
            index = Math.Max(0, index);
            bool readerRemoved = Position.Sentence >= index && Position.Sentence < sentences.Count
                                 && sentences[Position.Sentence].Owner == owner;
            int removed = 0, removedBeforeReader = 0;
            for (int i = sentences.Count - 1; i >= index; i--)
            {
                if (sentences[i].Owner == owner)
                {
                    if (i < Position.Sentence)
                    {
                        removedBeforeReader++;
                    }
                    sentences.RemoveAt(i);
                    removed++;
                }
            }
            if (removed > 0)
            {
                Position = new ReadingPosition(Math.Min(Position.Sentence - removedBeforeReader, sentences.Count), readerRemoved ? 0 : Position.Word);
                if (readerRemoved)
                {
                    SpokenWord = -1;
                }
                ContentChanged?.Invoke(this, EventArgs.Empty);
                PositionChanged?.Invoke(this, EventArgs.Empty);
            }
            return removed;
        }

        /// <summary>
        /// Takes one owner's unread text out of the document and returns the split (see <see cref="PeekOwner"/>).
        /// A sentence that was started keeps its read words as a shorter finished sentence. If the reader was on
        /// that owner's text it now points at whatever follows; the caller decides whether to keep reading it.
        /// </summary>
        public OwnerSplit TakeOwnerRemainder(string owner, bool fromSentenceStart = false)
        {
            var split = PeekOwner(owner, fromSentenceStart);
            if (split.Remainder.Length == 0 || split.FirstIndex < 0)
            {
                return split;
            }

            int firstIndex = split.FirstIndex;
            bool readerOnIt = Position.Sentence == firstIndex;
            var kept = new List<Sentence>();
            for (int i = 0; i < sentences.Count; i++)
            {
                var s = sentences[i];
                if (s.Owner != owner || i < firstIndex)
                {
                    kept.Add(s);
                }
                else if (i == firstIndex && split.Word > 0)
                {
                    string head = s.Text.Substring(0, s.Words[split.Word].Start).TrimEnd();
                    kept.Add(new Sentence(head, owner, s.Format));
                }
            }

            sentences.Clear();
            sentences.AddRange(kept);
            if (readerOnIt)
            {
                Position = new ReadingPosition(Math.Min(firstIndex + (split.Word > 0 ? 1 : 0), sentences.Count), 0);
            }
            else if (Position.Sentence > sentences.Count)
            {
                Position = new ReadingPosition(sentences.Count, 0);
            }
            SpokenWord = -1;
            ContentChanged?.Invoke(this, EventArgs.Empty);
            PositionChanged?.Invoke(this, EventArgs.Empty);
            return split;
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

        /// <summary>The full text of up to <paramref name="count"/> sentences after the current one, in reading order.</summary>
        public IReadOnlyList<string> PeekUpcomingTexts(int count)
        {
            var result = new List<string>();
            for (int i = Position.Sentence + 1; i < sentences.Count && result.Count < count; i++)
            {
                result.Add(sentences[i].SpokenText);
            }
            return result;
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
                return s.Text.Substring(offset);
            }
            // Whole sentence: any spoken prefix (a list number) comes first. The prefix is not part of the words, so the
            // offset is negative and engine positions inside the prefix map to before the first word.
            offset = -(s.SpokenText.Length - s.Text.Length);
            return s.SpokenText;
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
