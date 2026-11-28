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

        // Audio for the next sentences, generated one at a time in reading order while the current one plays.
        // At most about 12 seconds of finished audio waits (memory and CPU stay bounded).
        private readonly SynthesisQueue<(float[] Samples, int SampleRate)> lookAhead =
            new(budgetSeconds: 12, measureSeconds: a => a.SampleRate > 0 ? a.Samples.Length / (double)a.SampleRate : 0);
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
            var waited = System.Diagnostics.Stopwatch.StartNew();
            try
            {
                // Often already finished (or running): the queue shares the job and puts it first.
                (samples, sampleRate) = await lookAhead.Demand(key, () => Synthesize(dir, sid, text, speed)).WaitAsync(cancellationToken);
            }
            catch (Exception) when (!cancellationToken.IsCancellationRequested)
            {
                lookAhead.Take(key);   // do not keep a failed attempt
                throw;
            }
            cancellationToken.ThrowIfCancellationRequested();
            System.Diagnostics.Debug.WriteLine($"[prewarm] waited {waited.ElapsedMilliseconds} ms for {samples.Length / (double)Math.Max(1, sampleRate):F1} s of audio; {lookAhead.Stats}");
            lookAhead.Take(key);       // used up; frees room for the next sentence
            lookAhead.SetWanted(2, Array.Empty<(string, Func<(float[], int)>)>());   // a guess for this spot, if any, is no longer needed
            if (samples.Length == 0) return;

            var words = WordTimingEstimator.Estimate(text, samples.Length / (double)sampleRate);
            await player.PlayAsync(samples, sampleRate, words, onWord, cancellationToken);
        }

        private static string CacheKey(string dir, int sid, float speed, string text) => $"{dir}|{sid}|{speed:F3}|{text}";

        /// <summary>Queues the next sentences right away (they run after the sentence being spoken, in order).</summary>
        public void SetUpcoming(IReadOnlyList<string> texts, VoiceInfo? voice, int rate)
        {
            if (voice == null || !TryParseId(voice.Id, out string dir, out int sid)) return;
            float speed = RateToSpeed(rate);
            lookAhead.SetWanted(1, Wanted(texts, dir, sid, speed));
        }

        /// <summary>
        /// A guess: the rest of the sentence after a pause is probably what is read next, so make it while idle.
        /// Costs one sentence of CPU, once, and is dropped as soon as something else is read.
        /// </summary>
        public void PrepareResume(string text, VoiceInfo? voice, int rate)
        {
            if (voice == null || !TryParseId(voice.Id, out string dir, out int sid)) return;
            float speed = RateToSpeed(rate);
            lookAhead.SetWanted(2, Wanted(new[] { text }, dir, sid, speed));
        }

        private (string Key, Func<(float[], int)> Work)[] Wanted(IReadOnlyList<string> texts, string dir, int sid, float speed) =>
            texts.Where(t => !string.IsNullOrWhiteSpace(t))
                 .Distinct()
                 .Select(t => (CacheKey(dir, sid, speed, t), (Func<(float[], int)>)(() => Synthesize(dir, sid, t, speed))))
                 .ToArray();

        public void ClearUpcoming() => lookAhead.Clear();

        /// <summary>Hits and misses of look-ahead since start (or <see cref="ResetLookAheadStats"/>).</summary>
        public SynthesisStats LookAheadStats => lookAhead.Stats;

        public void ResetLookAheadStats() => lookAhead.ResetStats();

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
