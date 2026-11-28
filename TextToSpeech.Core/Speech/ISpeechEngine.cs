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

        /// <summary>
        /// Reading volume, 0 to 120 percent. Engines apply it from the next sentence. Above 100 only works
        /// where the engine produces its own audio (see each engine).
        /// </summary>
        void SetVolume(int percent);

        /// <summary>Gets a voice ready in the background (loads the model) so the first sentence starts quickly. No-op where nothing needs loading.</summary>
        void Prewarm(VoiceInfo? voice);

        /// <summary>
        /// The sentences that will be spoken next, in order. Engines that generate audio can prepare them
        /// while the current one plays. No-op where speech is produced live.
        /// </summary>
        void SetUpcoming(IReadOnlyList<string> texts, VoiceInfo? voice, int rate);

        /// <summary>
        /// A guess about what will be read next (the rest of a sentence after a pause). Engines that generate audio
        /// may prepare it while idle and drop it when something else is read. No-op by default.
        /// </summary>
        void PrepareResume(string text, VoiceInfo? voice, int rate) { }

        /// <summary>
        /// A caller (the local API) says this text will probably be read soon, under <paramref name="key"/>. Engines that
        /// generate audio may prepare it while idle. Calling again with the same key replaces the earlier hint; an empty
        /// list withdraws it. No-op by default.
        /// </summary>
        void PrepareHint(string key, IReadOnlyList<string> texts, VoiceInfo? voice, int rate) { }

        /// <summary>How well look-ahead is working, for engines that have it; null otherwise.</summary>
        SynthesisStats? LookAheadStats => null;

        /// <summary>Forget prepared audio (the reading list was cleared).</summary>
        void ClearUpcoming();

        /// <summary>Changes the rate. Some engines can only apply it from the next utterance.</summary>
        void SetRate(int rate);
    }
}
