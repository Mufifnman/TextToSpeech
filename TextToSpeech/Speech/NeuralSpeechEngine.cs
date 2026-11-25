using System.IO;
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

        // Audio for the next sentences, generated while the current one plays.
        private readonly LookAheadCache<(float[] Samples, int SampleRate)> lookAhead = new(maxEntries: 4);
        private readonly object upcomingGate = new();
        private IReadOnlyList<string> upcomingTexts = Array.Empty<string>();
        private VoiceInfo? upcomingVoice;
        private int upcomingRate;
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
                        if (model.Kind == NeuralModelKind.Kokoro) speaker = KokoroNames.Friendly(speaker);
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
            string key = CacheKey(dir, sid, speed, text);
            float[] samples;
            int sampleRate;
            try
            {
                // Often already finished: the previous sentence's playback generated this one.
                (samples, sampleRate) = await lookAhead.GetOrStart(key, () => Synthesize(dir, sid, text, speed)).WaitAsync(cancellationToken);
            }
            catch (Exception) when (!cancellationToken.IsCancellationRequested)
            {
                lookAhead.Remove(key);   // do not keep a failed attempt
                throw;
            }
            cancellationToken.ThrowIfCancellationRequested();
            lookAhead.Remove(key);       // used up
            if (samples.Length == 0) return;

            StartLookAhead();            // generate the next sentences while this one plays

            var words = WordTimingEstimator.Estimate(text, samples.Length / (double)sampleRate);
            await player.PlayAsync(samples, sampleRate, words, onWord, cancellationToken);
        }

        private static string CacheKey(string dir, int sid, float speed, string text) => $"{dir}|{sid}|{speed:F3}|{text}";

        public void SetUpcoming(IReadOnlyList<string> texts, VoiceInfo? voice, int rate)
        {
            lock (upcomingGate)
            {
                upcomingTexts = texts;
                upcomingVoice = voice;
                upcomingRate = rate;
            }
        }

        public void ClearUpcoming()
        {
            lock (upcomingGate)
            {
                upcomingTexts = Array.Empty<string>();
            }
            lookAhead.Clear();
        }

        /// <summary>Starts generating the sentences handed to <see cref="SetUpcoming"/>. Called once the current sentence's audio exists, so it never competes with it.</summary>
        private void StartLookAhead()
        {
            IReadOnlyList<string> texts;
            VoiceInfo? voice;
            int rate;
            lock (upcomingGate)
            {
                texts = upcomingTexts;
                voice = upcomingVoice;
                rate = upcomingRate;
            }
            if (voice == null || !TryParseId(voice.Id, out string dir, out int sid)) return;

            float speed = RateToSpeed(rate);
            foreach (string next in texts)
            {
                if (string.IsNullOrWhiteSpace(next)) continue;
                string text = next;
                lookAhead.GetOrStart(CacheKey(dir, sid, speed, text), () => Synthesize(dir, sid, text, speed));
            }
        }

        /// <summary>Loads the model and runs a tiny synthesis in the background so the first real sentence is not slowed by loading.</summary>
        public void Prewarm(VoiceInfo? voice)
        {
            if (voice == null || !TryParseId(voice.Id, out string dir, out int sid)) return;
            _ = Task.Run(() =>
            {
                try
                {
                    Synthesize(dir, sid, "Ready.", 1.0f);
                }
                catch (Exception)
                {
                    // Nothing to do: the real read reports a problem with this voice.
                }
            });
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
            // Measured on a 20-core PC: Kokoro went from 1.49 s (4 threads) to 1.30 s (8) per sentence; more gains little.
            config.Model.NumThreads = Math.Clamp(Environment.ProcessorCount / 2, 2, 8);
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
        /// <summary>Gain with a soft limiter, so 100 to 120 percent is real extra loudness without harsh clipping. Applies live.</summary>
        public void SetVolume(int percent) => player.Gain = Math.Clamp(percent, 0, 120) / 100f;

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
