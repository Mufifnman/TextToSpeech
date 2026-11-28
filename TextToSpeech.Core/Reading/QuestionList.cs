using System.Text;

namespace TextToSpeech.Core.Reading
{
    /// <summary>
    /// Builds the text and pause points for "text plus a list of questions": the questions are added at the end as a
    /// numbered list (so they show as a list and are read with their numbers), each followed by a pause. The app
    /// never works out what is a question; the caller hands the list in.
    /// </summary>
    public static class QuestionList
    {
        /// <returns>The full text and one pause point per question (ids q1, q2, ...), anchored at that question's line.</returns>
        public static (string Text, List<PauseSpec> Pauses) Append(string? text, IReadOnlyList<string> questions)
        {
            var sb = new StringBuilder((text ?? "").TrimEnd());
            var pauses = new List<PauseSpec>();
            int number = 0;
            foreach (var raw in questions)
            {
                string q = string.Join(" ", (raw ?? "").Split('\n', '\r').Select(x => x.Trim()).Where(x => x.Length > 0));
                if (q.Length == 0)
                {
                    continue;
                }
                if (number == 0 && sb.Length > 0)
                {
                    sb.Append("\n\n");
                }
                number++;
                int start = sb.Length;
                sb.Append(number).Append(". ").Append(q).Append('\n');
                pauses.Add(new PauseSpec("q" + number, q, start));
            }
            return (sb.ToString(), pauses);
        }
    }
}
