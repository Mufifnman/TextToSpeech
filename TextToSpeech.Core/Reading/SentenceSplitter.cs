using System.Text;

namespace TextToSpeech.Core.Reading
{
    /// <summary>
    /// Splits text into sentences for reading one at a time.
    ///
    /// Compared to the old <c>Split('\n', '.')</c> this:
    ///  - also ends sentences on '!', '?' and '…' (the old TODO),
    ///  - keeps the punctuation, so the voice gets the right intonation,
    ///  - does not split on common abbreviations ("Dr.", "e.g."), initials ("J. R. R."),
    ///    decimals ("3.14") or when the next word starts lowercase,
    ///  - treats a blank line as a sentence break but a single line break as a space,
    ///  - breaks very long runs (OCR text with no punctuation) at clause punctuation
    ///    so rewind and skip still move in useful steps.
    /// </summary>
    public static class SentenceSplitter
    {
        /// <summary>
        /// GUESS: 250 characters is roughly 40 to 50 words. Long enough that normal
        /// sentences are never cut, short enough that a page of unpunctuated OCR text
        /// does not become one giant "sentence".
        /// </summary>
        public const int DefaultMaxSentenceLength = 250;

        // Lowercase, without the trailing period. Multi-dot forms are written with their inner dots.
        private static readonly HashSet<string> Abbreviations = new(StringComparer.OrdinalIgnoreCase)
        {
            "mr", "mrs", "ms", "dr", "prof", "sr", "jr", "st", "mt", "ft",
            "vs", "e.g", "i.e", "etc", "cf", "al", "approx", "dept", "fig", "figs",
            "no", "nos", "vol", "vols", "pp", "p", "ch", "sec", "eq", "ed", "eds",
            "inc", "ltd", "co", "corp", "gov", "gen", "col", "lt", "sgt", "capt", "rev",
            "jan", "feb", "mar", "apr", "jun", "jul", "aug", "sep", "sept", "oct", "nov", "dec",
            "u.s", "u.k", "a.m", "p.m", "ph.d",
        };

        // Abbreviations that commonly end a sentence too; these still split when the next word is capitalized.
        private static readonly HashSet<string> CanEndSentence = new(StringComparer.OrdinalIgnoreCase)
        {
            "etc", "al", "inc", "ltd", "co", "corp", "u.s", "u.k", "a.m", "p.m",
        };

        public static bool IsTerminalChar(char c) => c is '.' or '!' or '?' or '…';

        public static bool IsClosingChar(char c) => c is '"' or '\'' or '”' or '’' or ')' or ']' or '}' or '»';

        public static IReadOnlyList<string> Split(string? text, int maxSentenceLength = DefaultMaxSentenceLength)
        {
            var result = new List<string>();
            if (string.IsNullOrWhiteSpace(text))
            {
                return result;
            }

            string normalized = text.Replace("\r\n", "\n").Replace('\r', '\n');
            var current = new StringBuilder();
            int i = 0;
            while (i < normalized.Length)
            {
                char c = normalized[i];

                if (c == '\n')
                {
                    // Blank line (paragraph break) ends a sentence; a single newline is just a space.
                    int j = i;
                    int newlines = 0;
                    while (j < normalized.Length && char.IsWhiteSpace(normalized[j]))
                    {
                        if (normalized[j] == '\n')
                        {
                            newlines++;
                        }
                        j++;
                    }
                    if (newlines >= 2)
                    {
                        Flush(current, result, maxSentenceLength);
                    }
                    else
                    {
                        current.Append(' ');
                    }
                    i = j;
                    continue;
                }

                if (IsTerminalChar(c))
                {
                    int end = i;
                    while (end < normalized.Length && IsTerminalChar(normalized[end]))
                    {
                        end++;
                    }
                    while (end < normalized.Length && IsClosingChar(normalized[end]))
                    {
                        end++;
                    }

                    current.Append(normalized, i, end - i);

                    if (IsBoundary(normalized, i, end))
                    {
                        Flush(current, result, maxSentenceLength);
                    }
                    i = end;
                    continue;
                }

                current.Append(c);
                i++;
            }
            Flush(current, result, maxSentenceLength);
            return result;
        }

        /// <param name="text">Whole text.</param>
        /// <param name="punctStart">Index of the first terminal punctuation character.</param>
        /// <param name="punctEnd">Index just past the punctuation and any closing quotes/brackets.</param>
        private static bool IsBoundary(string text, int punctStart, int punctEnd)
        {
            // Must be followed by whitespace or the end of the text ("3.14", "e.g.x", "file.txt" are not breaks).
            if (punctEnd < text.Length && !char.IsWhiteSpace(text[punctEnd]))
            {
                return false;
            }

            int next = punctEnd;
            while (next < text.Length && char.IsWhiteSpace(text[next]) && text[next] != '\n')
            {
                next++;
            }
            if (next >= text.Length || text[next] == '\n')
            {
                return true;
            }
            char nextChar = text[next];

            // "the book... and then" / "Dr. smith" style: lowercase continuation is not a new sentence.
            if (char.IsLower(nextChar))
            {
                return false;
            }

            bool onlyPeriods = true;
            for (int k = punctStart; k < punctEnd; k++)
            {
                if (IsTerminalChar(text[k]) && text[k] != '.')
                {
                    onlyPeriods = false;
                }
            }
            int periodCount = 0;
            for (int k = punctStart; k < punctEnd && text[k] == '.'; k++)
            {
                periodCount++;
            }
            if (!onlyPeriods || periodCount != 1)
            {
                // "!", "?", "?!", "..." followed by a capital: sentence break.
                return true;
            }

            string word = WordBefore(text, punctStart);
            if (word.Length == 0)
            {
                return true;
            }

            // Initials: "J. R. R. Tolkien", "John F. Kennedy".
            if (word.Length == 1 && char.IsUpper(word[0]))
            {
                return false;
            }

            if (Abbreviations.Contains(word))
            {
                // "etc. The next..." splits; "Dr. Smith" does not. "Acme Inc. Smith said" will split (rare, acceptable).
                return CanEndSentence.Contains(word);
            }

            return true;
        }

        /// <summary>The token right before <paramref name="index"/> (letters, digits and inner dots), without opening brackets/quotes.</summary>
        private static string WordBefore(string text, int index)
        {
            int start = index;
            while (start > 0 && (char.IsLetterOrDigit(text[start - 1]) || text[start - 1] == '.'))
            {
                start--;
            }
            return text.Substring(start, index - start).Trim('.');
        }

        private static void Flush(StringBuilder current, List<string> result, int maxSentenceLength)
        {
            string sentence = CollapseSpaces(current.ToString());
            current.Clear();
            if (!sentence.Any(char.IsLetterOrDigit))
            {
                return;
            }

            foreach (var piece in BreakLong(sentence, maxSentenceLength))
            {
                result.Add(piece);
            }
        }

        private static IEnumerable<string> BreakLong(string sentence, int max)
        {
            while (max > 0 && sentence.Length > max)
            {
                int cut = FindCut(sentence, max);
                yield return sentence.Substring(0, cut).Trim();
                sentence = sentence.Substring(cut).Trim();
            }
            if (sentence.Length > 0)
            {
                yield return sentence;
            }
        }

        /// <summary>Prefer ";" or ":" then "," then any space, looking backwards from the limit but not before a third of it.</summary>
        private static int FindCut(string s, int max)
        {
            int min = max / 3;
            foreach (var set in new[] { new[] { ';', ':', '—', '–' }, new[] { ',' } })
            {
                for (int i = Math.Min(max, s.Length - 1); i >= min; i--)
                {
                    if (Array.IndexOf(set, s[i]) >= 0 && i + 1 < s.Length && char.IsWhiteSpace(s[i + 1]))
                    {
                        return i + 1;
                    }
                }
            }
            for (int i = Math.Min(max, s.Length - 1); i >= min; i--)
            {
                if (char.IsWhiteSpace(s[i]))
                {
                    return i;
                }
            }
            return max;
        }

        private static string CollapseSpaces(string s)
        {
            var sb = new StringBuilder(s.Length);
            bool lastSpace = false;
            foreach (char c in s)
            {
                if (char.IsWhiteSpace(c))
                {
                    if (!lastSpace)
                    {
                        sb.Append(' ');
                    }
                    lastSpace = true;
                }
                else
                {
                    sb.Append(c);
                    lastSpace = false;
                }
            }
            return sb.ToString().Trim();
        }
    }
}
