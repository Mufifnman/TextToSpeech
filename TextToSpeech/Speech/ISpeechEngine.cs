namespace TextToSpeech.Speech
{
    /// <summary>
    /// A voice offered by one of the speech engines. <see cref="Id"/> is stable across runs so it can be saved in settings.
    /// </summary>
    public sealed record VoiceInfo(string Id, string DisplayName, string EngineName, bool IsNeural)
    {
        public override string ToString() => DisplayName;
    }

    /// <summary>
    /// The word currently being spoken, as a character range inside the text passed to <see cref="ISpeechEngine.SpeakAsync"/>.
    /// </summary>
    public readonly record struct WordBoundary(int CharOffset, int CharLength);

    /// <summary>
    /// Something that can turn text into audible speech and report which word is being spoken.
    /// One utterance is spoken at a time.
    /// </summary>
    public interface ISpeechEngine : IDisposable
    {
        /// <summary>Shown next to each voice in the picker.</summary>
        string Name { get; }

        IReadOnlyList<VoiceInfo> GetVoices();

        /// <summary>
        /// Speaks <paramref name="text"/> and completes when it has finished playing.
        /// Throws <see cref="OperationCanceledException"/> if <paramref name="cancellationToken"/> fires.
        /// </summary>
        /// <param name="voice">Voice to use; null means the engine's default voice.</param>
        /// <param name="rate">Speaking rate on the SAPI scale, -10 (slowest) to 10 (fastest), 0 is normal.</param>
        /// <param name="onWord">Called as each word starts. May be called on any thread.</param>
        Task SpeakAsync(string text, VoiceInfo? voice, int rate, Action<WordBoundary>? onWord, CancellationToken cancellationToken);

        bool IsPaused { get; }
        void Pause();
        void Resume();

        /// <summary>Changes the rate. Some engines can only apply it from the next utterance.</summary>
        void SetRate(int rate);
    }
}
