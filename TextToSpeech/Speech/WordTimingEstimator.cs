using System.Text.RegularExpressions;

namespace TextToSpeech.Speech
{
    public readonly record struct TimedWord(double StartSeconds, WordBoundary Boundary);

    /// <summary>
    /// Neural engines here return audio without word timings, so we estimate them by spreading the clip's
    /// duration across the words in proportion to their length, with extra time after punctuation.
    /// GUESS: the weights below are hand-tuned and untested against real audio. A later version could use
    /// the phoneme durations from the model or a forced aligner for exact timings.
    /// </summary>
    public static class WordTimingEstimator
    {
        private static readonly Regex Word = new(@"\S+", RegexOptions.Compiled);

        private const double LeadingSilenceSeconds = 0.05; // GUESS: small pause most VITS/Kokoro clips start with
        private const double WeightPerWord = 1.5;          // every word costs something, even "a"
        private const double PauseAfterComma = 3.0;        // ,;:
        private const double PauseAfterStop = 5.0;         // .!?

        public static IReadOnlyList<TimedWord> Estimate(string text, double durationSeconds)
        {
            var matches = Word.Matches(text);
            if (matches.Count == 0 || durationSeconds <= 0) return Array.Empty<TimedWord>();

            var weights = new double[matches.Count];
            double total = 0;
            for (int i = 0; i < matches.Count; i++)
            {
                string w = matches[i].Value;
                double weight = WeightPerWord + w.Count(char.IsLetterOrDigit);
                char last = w[^1];
                if (last is ',' or ';' or ':') weight += PauseAfterComma;
                else if (last is '.' or '!' or '?') weight += PauseAfterStop;
                weights[i] = weight;
                total += weight;
            }

            double lead = Math.Min(LeadingSilenceSeconds, durationSeconds * 0.1);
            double speaking = durationSeconds - lead;
            var result = new TimedWord[matches.Count];
            double t = lead;
            for (int i = 0; i < matches.Count; i++)
            {
                result[i] = new TimedWord(t, new WordBoundary(matches[i].Index, matches[i].Length));
                t += speaking * weights[i] / total;
            }
            return result;
        }
    }
}
