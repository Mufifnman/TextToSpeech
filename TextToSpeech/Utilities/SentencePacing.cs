namespace TextToSpeech
{
    /// <summary>The silence between sentences. Absolute milliseconds: it does not change with the speaking speed.</summary>
    public static class SentencePacing
    {
        public const int MinGapMs = 0, MaxGapMs = 2000, StepMs = 50, DefaultGapMs = 300;

        /// <summary>How long to wait after a sentence. Nothing after the last one, so finishing is never delayed.</summary>
        public static TimeSpan GapAfter(bool wasLastSentence, int configuredMs) =>
            wasLastSentence ? TimeSpan.Zero : TimeSpan.FromMilliseconds(Math.Clamp(configuredMs, MinGapMs, MaxGapMs));
    }
}
