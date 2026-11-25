using System.Diagnostics;
using System.IO;
using SherpaOnnx;
using TextToSpeech.Speech;
using Xunit;
using Xunit.Abstractions;

namespace TextToSpeech.Windows.Tests;

// Timing probe for the neural voices. Needs the voices downloaded on this PC, so it is tagged LocalOnly
// and does nothing where they are missing. Run: dotnet test --filter "Category=LocalOnly" --logger "console;verbosity=detailed"
public class NeuralBenchmark
{
    private readonly ITestOutputHelper output;
    public NeuralBenchmark(ITestOutputHelper output) => this.output = output;

    private static readonly string[] Sentences =
    {
        "The quick brown fox jumps over the lazy dog near the quiet river bank.",
        "She sells sea shells by the sea shore, and the shells she sells are surely sea shells.",
        "It was the best of times, it was the worst of times, it was the age of wisdom.",
        "Reading aloud makes long documents easier to follow when your eyes are tired.",
    };

    [Fact]
    [Trait("Category", "LocalOnly")]
    public void MeasureLoadAndGenerationTimes()
    {
        foreach (string root in NeuralSpeechEngine.VoiceRoots.Where(Directory.Exists))
        {
            foreach (string dir in Directory.EnumerateDirectories(root))
            {
                var model = NeuralModelFolder.TryRead(dir);
                if (model == null) continue;
                foreach (int threads in new[] { 2, 4, 8, 12 })
                {
                    var config = new OfflineTtsConfig();
                    config.Model.NumThreads = threads;
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

                    var sw = Stopwatch.StartNew();
                    using var tts = new OfflineTts(config);
                    double load = sw.Elapsed.TotalMilliseconds;

                    var times = new List<double>();
                    double audioSeconds = 0;
                    foreach (string sentence in Sentences)
                    {
                        sw.Restart();
                        var audio = tts.Generate(sentence, 1.0f, 0);
                        times.Add(sw.Elapsed.TotalMilliseconds);
                        audioSeconds += audio.Samples.Length / (double)audio.SampleRate;
                        audio.Dispose();
                    }
                    output.WriteLine($"{Path.GetFileName(dir),-28} threads={threads,2} load={load,6:F0} ms  " +
                        $"first={times[0],6:F0} ms  later={times.Skip(1).Average(),6:F0} ms avg  " +
                        $"({audioSeconds / Sentences.Length:F1} s of audio per sentence)");
                }
            }
        }
    }
}
