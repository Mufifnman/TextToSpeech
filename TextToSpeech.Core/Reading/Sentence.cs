namespace TextToSpeech.Core.Reading
{
    /// <summary>
    /// A word inside a <see cref="Sentence"/>, as a character range of the sentence text.
    /// Leading and trailing punctuation stay attached to the word ("(hello," is one word),
    /// so every visible character belongs to something clickable.
    /// </summary>
    public readonly record struct WordSpan(int Start, int Length)
    {
        public int End => Start + Length;

        public bool Contains(int charIndex) => charIndex >= Start && charIndex < End;
    }

    /// <summary>
    /// A place where reading stops and waits (a question to answer, or a plain marker). Set by the caller of the
    /// API; the app never decides by itself where one goes. <see cref="Answered"/> flips when the user resumes
    /// past it, skips it, or the API says it was answered.
    /// </summary>
    public sealed class PausePoint
    {
        public PausePoint(string id, string? label)
        {
            Id = id;
            Label = label;
        }

        public string Id { get; }

        /// <summary>The question (or a name) shown to whoever asks what reading is waiting on. Null for a plain marker.</summary>
        public string? Label { get; }

        public bool Answered { get; set; }
    }

    /// <summary>
    /// How a sentence is laid out when its text came from markdown. Only the first sentence of a block has a
    /// <see cref="Marker"/> and a <see cref="SpokenPrefix"/>.
    /// </summary>
    /// <param name="Kind">Paragraph, heading, list item or code.</param>
    /// <param name="Level">Heading level (1 to 6) or list depth (0 is top level).</param>
    /// <param name="Marker">Drawn in front of a list item ("•", "1.", a checkbox); not part of the words.</param>
    /// <param name="StartsBlock">The sentence starts a new line in the popup.</param>
    /// <param name="SpokenPrefix">Spoken before the sentence but not shown and not clickable ("One. " for item 1).</param>
    /// <param name="GapFactor">Multiplies the pause after this sentence (2 after a list item, so items stand apart).</param>
    public sealed record SentenceFormat(BlockKind Kind, int Level, string? Marker, bool StartsBlock, string SpokenPrefix, int GapFactor = 1);

    /// <summary>
    /// One sentence of a <see cref="ReadingDocument"/>, with its words pre-tokenized
    /// so the UI and the speech engine agree on word boundaries.
    /// </summary>
    public sealed class Sentence
    {
        public Sentence(string text, string? owner = null, SentenceFormat? format = null, PausePoint? pause = null)
        {
            Text = text ?? throw new ArgumentNullException(nameof(text));
            Words = Tokenize(text);
            Owner = owner;
            Format = format;
            Pause = pause;
        }

        public string Text { get; }

        /// <summary>Layout from markdown (null for plain text).</summary>
        public SentenceFormat? Format { get; }

        /// <summary>Reading stops after this sentence until the user (or the API) continues, unless play-all is on.</summary>
        public PausePoint? Pause { get; internal set; }

        /// <summary>What is handed to the speech engine when the whole sentence is read: the spoken prefix, then the text.</summary>
        public string SpokenText => string.IsNullOrEmpty(Format?.SpokenPrefix) ? Text : Format!.SpokenPrefix + Text;

        /// <summary>
        /// Which API session added this sentence (null for text typed, pasted or captured in the window).
        /// Lets the local API find and hand back the unread part of one client's text.
        /// </summary>
        public string? Owner { get; }

        public IReadOnlyList<WordSpan> Words { get; }

        public string WordText(int wordIndex)
        {
            var w = Words[wordIndex];
            return Text.Substring(w.Start, w.Length);
        }

        /// <summary>
        /// Maps a character offset (for example from a speech engine's word-boundary
        /// event) to a word index. Offsets that fall in whitespace map to the word
        /// before them; offsets before the first word map to 0.
        /// Returns -1 only when the sentence has no words.
        /// </summary>
        public int WordIndexAt(int charIndex)
        {
            if (Words.Count == 0)
            {
                return -1;
            }

            int found = 0;
            for (int i = 0; i < Words.Count; i++)
            {
                if (Words[i].Start <= charIndex)
                {
                    found = i;
                }
                else
                {
                    break;
                }
            }
            return found;
        }

        /// <summary>True when the sentence ends with terminal punctuation (optionally followed by quotes/brackets).</summary>
        public bool EndsComplete
        {
            get
            {
                for (int i = Text.Length - 1; i >= 0; i--)
                {
                    char c = Text[i];
                    if (SentenceSplitter.IsClosingChar(c))
                    {
                        continue;
                    }
                    return SentenceSplitter.IsTerminalChar(c);
                }
                return false;
            }
        }

        public override string ToString() => Text;

        private static IReadOnlyList<WordSpan> Tokenize(string text)
        {
            var words = new List<WordSpan>();
            int i = 0;
            while (i < text.Length)
            {
                while (i < text.Length && char.IsWhiteSpace(text[i]))
                {
                    i++;
                }
                int start = i;
                while (i < text.Length && !char.IsWhiteSpace(text[i]))
                {
                    i++;
                }
                if (i > start)
                {
                    words.Add(new WordSpan(start, i - start));
                }
            }
            return words;
        }
    }
}
