using System.Speech.Synthesis;

namespace TextToSpeech.Speech
{
    /// <summary>
    /// The classic Windows voices through System.Speech (SAPI). Always available on Windows, so it is the fallback engine.
    /// Word timings come straight from SAPI's SpeakProgress events.
    /// </summary>
    public sealed class SapiSpeechEngine : ISpeechEngine
    {
        private const string IdPrefix = "sapi:";
        private readonly object gate = new();
        private SpeechSynthesizer? current;
        private int volumePercent = 100;

        public string Name => "Windows";

        public IReadOnlyList<VoiceInfo> GetVoices()
        {
            using var synth = new SpeechSynthesizer();
            return synth.GetInstalledVoices()
                .Where(v => v.Enabled)
                .Select(v => new VoiceInfo(IdPrefix + v.VoiceInfo.Name, $"{v.VoiceInfo.Name} ({Name})", Name, IsNeural: false))
                .ToList();
        }

        public async Task SpeakAsync(string text, VoiceInfo? voice, int rate, Action<WordBoundary>? onWord, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var synth = new SpeechSynthesizer();
            if (voice != null && voice.Id.StartsWith(IdPrefix, StringComparison.Ordinal))
            {
                try { synth.SelectVoice(voice.Id.Substring(IdPrefix.Length)); }
                catch (ArgumentException) { /* voice was uninstalled; keep the default */ }
            }
            synth.Rate = Math.Clamp(rate, -10, 10);
            synth.Volume = Math.Clamp(volumePercent, 0, 100); // SAPI tops out at 100
            synth.SetOutputToDefaultAudioDevice();

            var done = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            synth.SpeakProgress += (_, e) => onWord?.Invoke(new WordBoundary(e.CharacterPosition, e.CharacterCount));
            synth.SpeakCompleted += (_, e) =>
            {
                if (e.Error != null) done.TrySetException(e.Error);
                else done.TrySetResult();
            };

            lock (gate) { current = synth; }
            try
            {
                using (cancellationToken.Register(() =>
                {
                    try { synth.SpeakAsyncCancelAll(); } catch (ObjectDisposedException) { }
                    done.TrySetCanceled(cancellationToken);
                }))
                {
                    synth.SpeakAsync(text);
                    await done.Task;
                }
            }
            finally
            {
                lock (gate) { current = null; }
                try { synth.Dispose(); } catch (Exception) { /* disposing while paused can throw; nothing to recover */ }
            }
        }

        public bool IsPaused
        {
            get { lock (gate) { return current?.State == SynthesizerState.Paused; } }
        }

        public void Pause()
        {
            lock (gate)
            {
                if (current?.State == SynthesizerState.Speaking) current.Pause();
            }
        }

        public void Resume()
        {
            lock (gate)
            {
                if (current?.State == SynthesizerState.Paused) current.Resume();
            }
        }

        public void SetRate(int rate)
        {
            lock (gate)
            {
                if (current != null) current.Rate = Math.Clamp(rate, -10, 10);
            }
        }

        /// <summary>SAPI's volume is 0 to 100, so a setting above 100 plays at 100 for Windows voices.</summary>
        public void SetVolume(int percent)
        {
            lock (gate)
            {
                volumePercent = percent;
                if (current != null) current.Volume = Math.Clamp(percent, 0, 100);
            }
        }

        public void Dispose() { }
    }
}
