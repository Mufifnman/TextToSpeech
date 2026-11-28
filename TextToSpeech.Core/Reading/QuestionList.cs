using System.Text;
using System.Text.RegularExpressions;

namespace TextToSpeech.Core.Reading
{
    /// <summary>
    /// Builds the text and pause points for "text plus a list of questions". A question the text already says
    /// (same words, ignoring case, punctuation and markup) is not added again: reading waits right there. Questions
    /// the text does not contain go in their own section at the end (a numbered list after a blank line),
    /// each followed by a pause. So a question is shown and read once. The app never works out what is a question;
    /// the caller hands the list in.
    /// </summary>
    public static class QuestionList
    {
        /// <returns>The full text and one pause point per question (ids q1, q2, ...), anchored at that question's line.</returns>
        public static (string Text, List<PauseSpec> Pauses) Append(string? text, IReadOnlyList<string> questions)
        {
            string body = (text ?? "").TrimEnd();
            var sb = new StringBuilder(body);
            var pauses = new List<PauseSpec>();
            var section = new List<(int Id, string Question)>();
            int id = 0;
            var blocks = MarkdownReader.Parse(body, true);
            var usedBlocks = new HashSet<int>();
            foreach (var raw in questions)
            {
                string q = string.Join(" ", (raw ?? "").Split('\n', '\r').Select(x => x.Trim()).Where(x => x.Length > 0));
                if (q.Length == 0)
                {
                    continue;
                }
                id++;
                int at = FindIn(body, q);
                if (at >= 0 && !usedBlocks.Add(BlockOf(blocks, at)))
                {
                    at = -1;   // that block already holds a wait point (a block holds one): ask this one in the list instead
                }
                if (at >= 0)
                {
                    pauses.Add(new PauseSpec("q" + id, q, at));   // already said in the text: wait there
                }
                else
                {
                    section.Add((id, q));
                }
            }

            if (section.Count > 0)
            {
                if (sb.Length > 0)
                {
                    sb.Append("\n\n");
                }
                int number = 0;
                foreach (var (qid, q) in section)
                {
                    number++;
                    int start = sb.Length;
                    sb.Append(number).Append(". ").Append(q).Append('\n');
                    pauses.Add(new PauseSpec("q" + qid, q, start));
                }
            }
            pauses.Sort((a, b) => a.At.CompareTo(b.At));
            return (sb.ToString(), pauses);
        }

        /// <summary>The block (as the reading list will split the text) that holds source offset <paramref name="at"/>.</summary>
        private static int BlockOf(IReadOnlyList<MarkdownBlock> blocks, int at)
        {
            for (int i = blocks.Count - 1; i >= 0; i--)
            {
                if (blocks[i].SourceStart <= at)
                {
                    return i;
                }
            }
            return 0;
        }

        /// <summary>
        /// Where <paramref name="question"/> occurs in <paramref name="body"/> (whole words in order, any punctuation or markup
        /// between), or -1. A question that ends with "?" only counts where the text asks it with a "?" too, so a statement that
        /// merely uses the same words ("here is why it fails" for "Why?") is not taken for the question.
        /// </summary>
        private static int FindIn(string body, string question)
        {
            var words = Regex.Matches(question, @"[\p{L}\p{N}]+").Select(m => Regex.Escape(m.Value)).ToList();
            if (words.Count == 0 || body.Length == 0)
            {
                return -1;
            }
            string pattern = @"(?<![\p{L}\p{N}])" + string.Join(@"[\W_]+", words) + @"(?![\p{L}\p{N}])";
            if (question.TrimEnd().EndsWith('?'))
            {
                pattern += @"(?=[\s*_`""')\]]*\?)";
            }
            var match = Regex.Match(body, pattern, RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, TimeSpan.FromSeconds(2));
            return match.Success ? match.Index : -1;
        }
    }
}
