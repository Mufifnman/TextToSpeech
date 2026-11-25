using SherpaOnnx;

namespace TextToSpeech.Speech
{
    /// <summary>
    /// Local neural voices (Piper/VITS and Kokoro) run through sherpa-onnx, which uses ONNX Runtime and bundles
    /// espeak-ng for phonemes. Nothing leaves the machine. Models live in folders under <see cref="VoiceRoots"/>.
    /// </summary>
    public sealed class NeuralSpeechEngine : ISpeechEngine
    {
        private const string IdPrefix = "neural:";
        private readonly PcmPlayer player = new();
        private readonly object modelGate = new();
        private OfflineTts? loaded;
        private string? loadedDir;

        public string Name => "Neural";

        /// <summary>Where downloaded voices go.</summary>
        public static string UserVoicesFolder =>
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "TextToSpeech", "voices");

        /// <summary>Voices can also ship next to the exe in a "voices" folder.</summary>
        public static IReadOnlyList<string> VoiceRoots => new[]
        {
            UserVoicesFolder,
            Path.Combine(AppContext.BaseDirectory, "voices"),
        };

        public IReadOnlyList<VoiceInfo> GetVoices()
        {
            var voices = new List<VoiceInfo>();
            foreach (string root in VoiceRoots.Where(Directory.Exists))
            {
                foreach (string dir in Directory.EnumerateDirectories(root).OrderBy(d => d, StringComparer.OrdinalIgnoreCase))
                {
                    var model = NeuralModelFolder.TryRead(dir);
                    if (model == null) continue;
                    for (int sid = 0; sid < model.SpeakerNames.Count; sid++)
                    {
                        string speaker = model.SpeakerNames[sid];
                        string label = string.IsNullOrEmpty(speaker) ? model.DisplayName : $"{model.DisplayName}: {speaker}";
                        voices.Add(new VoiceInfo($"{IdPrefix}{sid}|{dir}", $"{label} ({Name})", Name, IsNeural: true));
                    }
                }
            }
            return voices;
        }

        public async Task SpeakAsync(string text, VoiceInfo? voice, int rate, Action<WordBoundary>? onWord, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (string.IsNullOrWhiteSpace(text)) return;
            if (voice == null || !TryParseId(voice.Id, out string dir, out int sid))
                throw new ArgumentException("Not a neural voice.", nameof(voice));

            float speed = RateToSpeed(rate);
            var (samples, sampleRate) = await Task.Run(() => Synthesize(dir, sid, text, speed), cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();
            if (samples.Length == 0) return;

            var words = WordTimingEstimator.Estimate(text, samples.Length / (double)sampleRate);
            await player.PlayAsync(samples, sampleRate, words, onWord, cancellationToken);
        }

        /// <summary>
        /// GUESS: maps the SAPI rate scale onto a speed multiplier so the existing slider feels similar on both engines.
        /// SAPI's +10 is roughly 3x and -10 roughly 1/3x, so speed = 3^(rate/10).
        /// </summary>
        public static float RateToSpeed(int rate) => (float)Math.Pow(3.0, Math.Clamp(rate, -10, 10) / 10.0);

        private (float[] Samples, int SampleRate) Synthesize(string dir, int sid, string text, float speed)
        {
            lock (modelGate)
            {
                var tts = Load(dir);
                var audio = tts.Generate(text, speed, sid);
                try { return (audio.Samples, audio.SampleRate); }
                finally { audio.Dispose(); }
            }
        }

        /// <summary>Keeps one model in memory; switching voices in another folder reloads.</summary>
        private OfflineTts Load(string dir)
        {
            if (loaded != null && loadedDir == dir) return loaded;

            var model = NeuralModelFolder.TryRead(dir)
                ?? throw new InvalidOperationException($"No usable voice model in {dir}.");

            var config = new OfflineTtsConfig();
            config.Model.NumThreads = Math.Clamp(Environment.ProcessorCount / 2, 1, 4);
            config.Model.Provider = "cpu";
            config.Model.Debug = 0;
            config.MaxNumSentences = 1;
            if (model.Kind == NeuralModelKind.Kokoro)
            {
                config.Model.Kokoro.Model = model.ModelPath;
                config.Model.Kokoro.Voices = model.VoicesPath!;
                config.Model.Kokoro.Tokens = model.TokensPath;
                config.Model.Kokoro.DataDir = model.DataDir ?? "";
            }
            else
            {
                config.Model.Vits.Model = model.ModelPath;
                config.Model.Vits.Tokens = model.TokensPath;
                config.Model.Vits.DataDir = model.DataDir ?? "";
            }

            loaded?.Dispose();
            loaded = null;
            loadedDir = null;
            loaded = new OfflineTts(config);
            loadedDir = dir;
            return loaded;
        }

        private static bool TryParseId(string id, out string dir, out int sid)
        {
            dir = "";
            sid = 0;
            if (!id.StartsWith(IdPrefix, StringComparison.Ordinal)) return false;
            string rest = id.Substring(IdPrefix.Length);
            int bar = rest.IndexOf('|');
            if (bar <= 0 || !int.TryParse(rest.AsSpan(0, bar), out sid)) return false;
            dir = rest.Substring(bar + 1);
            return true;
        }

        public bool IsPaused => player.IsPaused;
        public void Pause() => player.Pause();
        public void Resume() => player.Resume();
        public void SetRate(int rate) { /* applies from the next sentence, since audio is generated per sentence */ }

        public void Dispose()
        {
            lock (modelGate)
            {
                loaded?.Dispose();
                loaded = null;
                loadedDir = null;
            }
        }
    }
}
