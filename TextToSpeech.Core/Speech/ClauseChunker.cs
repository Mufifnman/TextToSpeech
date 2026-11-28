namespace TextToSpeech.Speech
{
    /// <summary>A piece of a sentence: <see cref="Start"/> and <see cref="Length"/> are character positions inside the sentence's text.</summary>
    public readonly record struct TextChunk(int Start, int Length);

    /// <summary>How eagerly a sentence is cut into chunks.</summary>
    public sealed record ChunkOptions(int WholeMaxWords, int FirstTargetWords, int TargetWords, int MinWords)
    {
        /// <summary>Normal reading: only long sentences (over 20 words) are cut. The first piece ends at the first clause break after 6 words, the rest are about 16 words.</summary>
        public static ChunkOptions Normal { get; } = new(WholeMaxWords: 20, FirstTargetWords: 6, TargetWords: 16, MinWords: 6);

        /// <summary>Very first audio after a start, jump or resume: the first piece is small so speech begins sooner.</summary>
        public static ChunkOptions Eager { get; } = new(WholeMaxWords: 12, FirstTargetWords: 5, TargetWords: 16, MinWords: 5);
    }

    /// <summary>
    /// Cuts a long sentence into pieces a speech model can generate quickly, so the first piece can start playing
    /// while the rest is still being made. Cuts only at clause punctuation (comma, semicolon, colon, dash), never in
    /// the middle of a clause or a number, and never leaves a piece shorter than the minimum, so the join sounds like
    /// the natural pause the punctuation asks for. Short sentences are left whole.
    /// </summary>
    public static class ClauseChunker
    {
        /// <summary>The pieces of <paramref name="text"/> in order. They do not overlap and leave out only the whitespace at the cuts.</summary>
        public static IReadOnlyList<TextChunk> Split(string text, ChunkOptions options)
        {
            var whole = new[] { new TextChunk(0, text.Length) };
            if (string.IsNullOrWhiteSpace(text)) return whole;

            // Words (runs of non-space), and which of them end at a clause break we may cut at.
            var wordStarts = new List<int>();
            var wordEnds = new List<int>();
            for (int i = 0; i < text.Length; i++)
            {
                if (char.IsWhiteSpace(text[i])) continue;
                int start = i;
                while (i < text.Length && !char.IsWhiteSpace(text[i])) i++;
                wordStarts.Add(start);
                wordEnds.Add(i);
            }
            int words = wordStarts.Count;
            if (words <= options.WholeMaxWords) return whole;

            var chunks = new List<TextChunk>();
            int chunkFirstWord = 0;
            for (int w = 0; w < words - 1; w++)
            {
                int inChunk = w - chunkFirstWord + 1;
                int target = chunks.Count == 0 ? options.FirstTargetWords : options.TargetWords;
                int remaining = words - (w + 1);
                if (inChunk < options.MinWords || remaining < options.MinWords) continue;
                if (!EndsAtClauseBreak(text, wordStarts[w], wordEnds[w], wordStarts[w + 1])) continue;
                if (inChunk < target) continue;

                chunks.Add(new TextChunk(wordStarts[chunkFirstWord], wordEnds[w] - wordStarts[chunkFirstWord]));
                chunkFirstWord = w + 1;
            }
            if (chunks.Count == 0) return whole;

            chunks.Add(new TextChunk(wordStarts[chunkFirstWord], wordEnds[words - 1] - wordStarts[chunkFirstWord]));
            return chunks;
        }

        /// <summary>The word ends with , ; : or a dash and is followed by a space (so "1,000" and "a:b" are not cut), or is a lone dash.</summary>
        private static bool EndsAtClauseBreak(string text, int wordStart, int wordEnd, int nextWordStart)
        {
            if (nextWordStart <= wordEnd) return false;
            char last = text[wordEnd - 1];
            if (last is ',' or ';' or ':' or '\u2014' or '\u2013') return true;
            string word = text.Substring(wordStart, wordEnd - wordStart);
            return word is "-" or "--";
        }
    }
}
