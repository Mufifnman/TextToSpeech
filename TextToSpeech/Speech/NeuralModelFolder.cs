using System.IO;
using System.Text.Json;

namespace TextToSpeech.Speech
{
    public enum NeuralModelKind { Vits, Kokoro }

    /// <summary>
    /// A folder holding one sherpa-onnx TTS model, laid out the way the sherpa-onnx "tts-models" release archives unpack:
    /// <list type="bullet">
    /// <item>Piper / VITS: <c>*.onnx</c>, <c>tokens.txt</c>, <c>espeak-ng-data/</c> (and Piper's <c>*.onnx.json</c>)</item>
    /// <item>Kokoro: <c>model.onnx</c>, <c>voices.bin</c>, <c>tokens.txt</c>, <c>espeak-ng-data/</c></item>
    /// </list>
    /// </summary>
    public sealed class NeuralModelFolder
    {
        public required string Directory { get; init; }
        public required NeuralModelKind Kind { get; init; }
        public required string ModelPath { get; init; }
        public required string TokensPath { get; init; }
        public string? VoicesPath { get; init; }
        public string? DataDir { get; init; }
        public required string DisplayName { get; init; }
        public required IReadOnlyList<string> SpeakerNames { get; init; }

        public static NeuralModelFolder? TryRead(string dir)
        {
            string tokens = Path.Combine(dir, "tokens.txt");
            if (!File.Exists(tokens)) return null;

            string? onnx = System.IO.Directory.EnumerateFiles(dir, "*.onnx")
                .OrderByDescending(f => Path.GetFileName(f).Equals("model.onnx", StringComparison.OrdinalIgnoreCase))
                .FirstOrDefault();
            if (onnx == null) return null;

            string espeak = Path.Combine(dir, "espeak-ng-data");
            string? dataDir = System.IO.Directory.Exists(espeak) ? espeak : null;
            string folderName = Path.GetFileName(dir.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
            string voicesBin = Path.Combine(dir, "voices.bin");

            if (File.Exists(voicesBin))
            {
                return new NeuralModelFolder
                {
                    Directory = dir,
                    Kind = NeuralModelKind.Kokoro,
                    ModelPath = onnx,
                    TokensPath = tokens,
                    VoicesPath = voicesBin,
                    DataDir = dataDir,
                    DisplayName = "Kokoro",
                    SpeakerNames = ReadSpeakerList(dir) ?? KokoroSpeakers(folderName, voicesBin),
                };
            }

            return new NeuralModelFolder
            {
                Directory = dir,
                Kind = NeuralModelKind.Vits,
                ModelPath = onnx,
                TokensPath = tokens,
                DataDir = dataDir,
                DisplayName = PiperDisplayName(folderName),
                SpeakerNames = ReadSpeakerList(dir) ?? PiperSpeakers(onnx + ".json"),
            };
        }

        /// <summary>An optional <c>speakers.txt</c> (one name per line, in speaker id order) overrides any guessing.</summary>
        private static IReadOnlyList<string>? ReadSpeakerList(string dir)
        {
            string path = Path.Combine(dir, "speakers.txt");
            if (!File.Exists(path)) return null;
            var names = File.ReadAllLines(path).Select(l => l.Trim()).Where(l => l.Length > 0).ToList();
            return names.Count > 0 ? names : null;
        }

        /// <summary>"vits-piper-en_US-amy-low" becomes "Amy (en-US, low)".</summary>
        private static string PiperDisplayName(string folderName)
        {
            string name = folderName.StartsWith("vits-piper-", StringComparison.OrdinalIgnoreCase)
                ? folderName.Substring("vits-piper-".Length)
                : folderName;
            var parts = name.Split('-');
            if (parts.Length >= 3)
            {
                string voice = string.Join(' ', parts[1..^1]).Replace('_', ' ');
                voice = voice.Length > 0 ? char.ToUpperInvariant(voice[0]) + voice[1..] : voice;
                return $"{voice} ({parts[0].Replace('_', '-')}, {parts[^1]})";
            }
            return name;
        }

        /// <summary>Piper's .onnx.json lists multi-speaker names in speaker_id_map.</summary>
        private static IReadOnlyList<string> PiperSpeakers(string jsonPath)
        {
            try
            {
                if (File.Exists(jsonPath))
                {
                    using var doc = JsonDocument.Parse(File.ReadAllText(jsonPath));
                    var root = doc.RootElement;
                    if (root.TryGetProperty("speaker_id_map", out var map) && map.ValueKind == JsonValueKind.Object)
                    {
                        var byId = map.EnumerateObject()
                            .Where(p => p.Value.ValueKind == JsonValueKind.Number)
                            .OrderBy(p => p.Value.GetInt32())
                            .Select(p => p.Name)
                            .ToList();
                        if (byId.Count > 1) return byId;
                    }
                }
            }
            catch (Exception) { /* malformed json: treat as single speaker */ }
            return new[] { "" };
        }

        // The speaker order of the kokoro-en-v0_19 release, from the sherpa-onnx docs.
        /// <summary>The 11 voices of the kokoro-en-v0_19 release, in speaker id order.</summary>
        public static readonly string[] KokoroEnV019 =
        {
            "af", "af_bella", "af_nicole", "af_sarah", "af_sky",
            "am_adam", "am_michael", "bf_emma", "bf_isabella", "bm_george", "bm_lewis",
        };

        private static IReadOnlyList<string> KokoroSpeakers(string folderName, string voicesBin)
        {
            if (folderName.Contains("v0_19", StringComparison.OrdinalIgnoreCase)) return KokoroEnV019;

            // GUESS: voices.bin is float32 style vectors of shape [510, 1, 256] per speaker. Used only when
            // there is no speakers.txt and the release is not one we know.
            const long bytesPerSpeaker = 510L * 256 * sizeof(float);
            long count = Math.Max(1, new FileInfo(voicesBin).Length / bytesPerSpeaker);
            return Enumerable.Range(0, (int)count).Select(i => $"Speaker {i}").ToList();
        }
    }
}
