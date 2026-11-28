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

        /// <summary>
        /// Cut long sentences into clause-sized pieces so speech starts sooner and the next piece is made while
        /// the first plays. Short sentences are never cut. Turn off to generate each sentence in one go.
        /// </summary>
        public bool ChunkLongSentences { get; set; } = true;

        private long lastPlaybackEndTicks;   // Environment.TickCount64 when audio last stopped; 0 = never

        /// <summary>True when nothing has played for a few seconds: a new reading, a jump, or a resume after a pause.</summary>
        private bool IsStartingFresh() =>
            lastPlaybackEndTicks == 0 || Environment.TickCount64 - Volatile.Read(ref lastPlaybackEndTicks) > 3000;

        private IReadOnlyList<TextChunk> Plan(string text, bool eager) =>
            ChunkLongSentences
                ? ClauseChunker.Split(text, eager ? ChunkOptions.Eager : ChunkOptions.Normal)
                : new[] { new TextChunk(0, text.Length) };

        /// <summary>
        /// Chooses how to cut a sentence about to be spoken: the cut a guess already prepared if there is one,
        /// else the eager cut when starting fresh, else the normal cut (what the look-ahead prepared).
        /// </summary>
        private IReadOnlyList<TextChunk> ChoosePlan(string dir, int sid, float speed, string text)
        {
            var normal = Plan(text, eager: false);
            var eager = Plan(text, eager: true);
            string KeyOf(TextChunk c) => CacheKey(dir, sid, speed, text.Substring(c.Start, c.Length));
            if (lookAhead.Contains(KeyOf(normal[0]))) return normal;
            if (lookAhead.Contains(KeyOf(eager[0]))) return eager;
            return IsStartingFresh() ? eager : normal;
        }

        public async Task SpeakAsync(string text, VoiceInfo? voice, int rate, Action<WordBoundary>? onWord, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (string.IsNullOrWhiteSpace(text)) return;
            if (voice == null || !TryParseId(voice.Id, out string dir, out int sid))
                throw new ArgumentException("Not a neural voice.", nameof(voice));

            float speed = RateToSpeed(rate);
            var plan = ChoosePlan(dir, sid, speed, text);
            var chunkTexts = plan.Select(c => text.Substring(c.Start, c.Length)).ToArray();
            var keys = chunkTexts.Select(t => CacheKey(dir, sid, speed, t)).ToArray();

            // Ask for every piece now, in order: they run before anything prepared for later sentences.
            var tasks = new Task<(float[] Samples, int SampleRate)>[keys.Length];
            for (int i = 0; i < keys.Length; i++)
            {
                string piece = chunkTexts[i];
                tasks[i] = lookAhead.Demand(keys[i], () => Synthesize(dir, sid, piece, speed));
            }

            int sampleRate = 0;
            var waited = System.Diagnostics.Stopwatch.StartNew();
            try
            {
                async Task<float[]> GetChunk(int i)
                {
                    var (samples, rateOfClip) = await tasks[i].WaitAsync(cancellationToken).ConfigureAwait(false);
                    if (i == 0)
                    {
                        System.Diagnostics.Debug.WriteLine($"[prewarm] first audio after {waited.ElapsedMilliseconds} ms ({keys.Length} piece(s)); {lookAhead.Stats}");
                    }
                    sampleRate = rateOfClip;
                    lookAhead.Take(keys[i]);   // used up; frees room for the next sentence
                    return samples;
                }

                // The rate is only known once the first piece exists.
                var first = await tasks[0].WaitAsync(cancellationToken);
                sampleRate = first.SampleRate;
                if (first.Samples.Length == 0 && keys.Length == 1)
                {
                    lookAhead.Take(keys[0]);
                    return;
                }
                lookAhead.SetWanted(2, Array.Empty<(string, Func<(float[], int)>)>());   // a guess for this spot, if any, is no longer needed

                IReadOnlyList<TimedWord> WordsFor(int i, float[] samples)
                {
                    var estimated = WordTimingEstimator.Estimate(chunkTexts[i], samples.Length / (double)sampleRate);
                    if (plan[i].Start == 0) return estimated;
                    return estimated.Select(w => new TimedWord(w.StartSeconds, new WordBoundary(w.Boundary.CharOffset + plan[i].Start, w.Boundary.CharLength))).ToList();
                }

                await player.PlayAsync(sampleRate, keys.Length, GetChunk, WordsFor, onWord, cancellationToken);
            }
            catch (Exception) when (!cancellationToken.IsCancellationRequested)
            {
                foreach (var key in keys) lookAhead.Take(key);   // do not keep a failed attempt
                throw;
            }
            finally
            {
                // Pieces not used (cancelled or failed) are dropped, so stale work never blocks the next sentence.
                foreach (var key in keys) lookAhead.Take(key);
                Volatile.Write(ref lastPlaybackEndTicks, Environment.TickCount64);
            }
        }

        private static string CacheKey(string dir, int sid, float speed, string text) => $"{dir}|{sid}|{speed:F3}|{text}";

        /// <summary>Queues the next sentences right away (they run after the sentence being spoken, in order).</summary>
        public void SetUpcoming(IReadOnlyList<string> texts, VoiceInfo? voice, int rate)
        {
            if (voice == null || !TryParseId(voice.Id, out string dir, out int sid)) return;
            float speed = RateToSpeed(rate);
            lookAhead.SetWanted(1, Wanted(texts, dir, sid, speed, eager: false));
        }

        /// <summary>
        /// A guess: the rest of the sentence after a pause is probably what is read next, so make it while idle.
        /// Costs one sentence of CPU, once, and is dropped as soon as something else is read.
        /// </summary>
        public void PrepareResume(string text, VoiceInfo? voice, int rate)
        {
            if (voice == null || !TryParseId(voice.Id, out string dir, out int sid)) return;
            float speed = RateToSpeed(rate);
            lookAhead.SetWanted(2, Wanted(new[] { text }, dir, sid, speed, eager: true));
        }

        private (string Key, Func<(float[], int)> Work)[] Wanted(IReadOnlyList<string> texts, string dir, int sid, float speed, bool eager)
        {
            var result = new List<(string, Func<(float[], int)>)>();
            var seen = new HashSet<string>();
            foreach (string sentence in texts.Where(t => !string.IsNullOrWhiteSpace(t)))
            {
                foreach (var chunk in Plan(sentence, eager))
                {
                    string piece = sentence.Substring(chunk.Start, chunk.Length);
                    string key = CacheKey(dir, sid, speed, piece);
                    if (seen.Add(key)) result.Add((key, () => Synthesize(dir, sid, piece, speed)));
                }
            }
            return result.ToArray();
        }

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
            lookAhead.Clear();
            player.Dispose();
            lock (modelGate)
            {
                loaded?.Dispose();
                loaded = null;
                loadedDir = null;
            }
        }
    }
}
