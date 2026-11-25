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
    /// One sentence of a <see cref="ReadingDocument"/>, with its words pre-tokenized
    /// so the UI and the speech engine agree on word boundaries.
    /// </summary>
    public sealed class Sentence
    {
        public Sentence(string text, string? owner = null)
        {
            Text = text ?? throw new ArgumentNullException(nameof(text));
            Words = Tokenize(text);
            Owner = owner;
        }

        public string Text { get; }

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
