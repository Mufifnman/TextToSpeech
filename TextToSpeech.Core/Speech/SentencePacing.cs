namespace TextToSpeech
{
    /// <summary>The silence between sentences. Absolute milliseconds: it does not change with the speaking speed.</summary>
    public static class SentencePacing
    {
        public const int MinGapMs = 0, MaxGapMs = 2000, StepMs = 50, DefaultGapMs = 300;

        /// <summary>How long to wait after a sentence. Nothing after the last one, so finishing is never delayed.</summary>
        /// <param name="factor">Multiplies the pause (2 after a list item so the items stand apart); the factor can go past <see cref="MaxGapMs"/>.</param>
        public static TimeSpan GapAfter(bool wasLastSentence, int configuredMs, int factor = 1) =>
            wasLastSentence ? TimeSpan.Zero : TimeSpan.FromMilliseconds(Math.Clamp(configuredMs, MinGapMs, MaxGapMs) * Math.Max(1, factor));
    }
}
