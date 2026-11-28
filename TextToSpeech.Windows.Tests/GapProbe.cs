using System.Collections.Concurrent;
using System.Diagnostics;
using TextToSpeech.Core.Reading;
using TextToSpeech.Speech;
using Xunit;
using Xunit.Abstractions;

namespace TextToSpeech.Windows.Tests;

// Repeatable benchmark for sentence prewarming (not a regular test). It SPEAKS ALOUD: it reads test paragraphs through
// the real engines and ReadingController from this build, and logs when each sentence's audio starts and ends, plus the
// look-ahead statistics (how often audio was ready in time, how many guesses were used or wasted).
// Compare before and after a change, and with "ChunkLongSentences": false in settings.json to see what chunking buys.
// Run: dotnet test --filter "FullyQualifiedName~GapProbe" --logger "console;verbosity=detailed"
public class GapProbe
{
    private readonly ITestOutputHelper output;
    public GapProbe(ITestOutputHelper output) => this.output = output;

    private static string Words(int count, string seed, bool commas = false)
    {
        string[] pool = ("the quick brown fox jumps over a lazy dog while seven calm owls watch from old trees " +
                         "near quiet rivers and green hills under bright summer skies " + seed).Split(' ', StringSplitOptions.RemoveEmptyEntries);
        var words = Enumerable.Range(0, count).Select(i => pool[(i * 7 + seed.Length) % pool.Length]).ToArray();
        words[0] = char.ToUpperInvariant(words[0][0]) + words[0][1..];
        if (commas)
        {
            for (int i = 7; i < count - 6; i += 8) words[i] += ",";   // a comma every 8 words: places where a long sentence may be cut
        }
        return string.Join(" ", words) + ".";
    }

    /// <summary>Runs everything on one thread, like the app's UI thread.</summary>
    private sealed class OneThreadContext : SynchronizationContext, IDisposable
    {
        private readonly BlockingCollection<(SendOrPostCallback, object?)> queue = new();
        private readonly Thread thread;
        public OneThreadContext()
        {
            thread = new Thread(() =>
            {
                SetSynchronizationContext(this);
                foreach (var (cb, state) in queue.GetConsumingEnumerable()) cb(state);
            }) { IsBackground = true };
            thread.Start();
        }
        public override void Post(SendOrPostCallback d, object? state) => queue.Add((d, state));
        public void Run(Action action) => Post(_ => action(), null);
        public void Dispose() => queue.CompleteAdding();
    }

    [Fact]
    [Trait("Category", "LocalOnly")]
    public async Task MeasureSentenceGaps()
    {
        using var catalog = new VoiceCatalog();
        catalog.Refresh();
        var settings = AppSettings.Load();   // read only: the same voice and speed the app uses
        var voice = catalog.PickInitial(settings.VoiceId);
        if (voice == null || !voice.IsNeural) { output.WriteLine("No neural voice selected or installed: nothing to measure."); return; }
        output.WriteLine($"voice: {voice?.DisplayName}  rate {settings.Rate}  gap {settings.SentenceGapMs} ms  threads per engine as built");

        // Warm the model the way the app does at start, then wait for it.
        var warm = Stopwatch.StartNew();
        catalog.EngineFor(voice).Prewarm(voice);
        await Task.Delay(4000);

        var runs = new[]
        {
            ("run 1 (4, 6, 30, 5, 28, 8 words)", new[] { 4, 6, 30, 5, 28, 8 }),
            ("run 2 (4, 30, 30, 5, 28, 8 words)", new[] { 4, 30, 30, 5, 28, 8 }),
            ("run 3 with commas (4, 30, 30, 5, 28, 8 words)", new[] { 4, 30, 30, 5, 28, 8 }),
        };
        foreach (var (name, counts) in runs)
        {
            bool commas = name.Contains("commas");
            catalog.Neural.ResetLookAheadStats();
            string text = string.Join(" ", counts.Select((c, i) => Words(c, "s" + i + name.Length, commas)));
            await RunOnce(name, text, counts.Length, catalog, voice, settings);
            output.WriteLine("look-ahead: " + catalog.Neural.LookAheadStats);
            await Task.Delay(1500);
        }
    }

    private async Task RunOnce(string name, string text, int sentenceCount, VoiceCatalog catalog, VoiceInfo? voice, AppSettings settings)
    {
        var clock = Stopwatch.StartNew();
        var firstWord = new double[sentenceCount];
        var ended = new double[sentenceCount];
        var starting = new double[sentenceCount];
        Array.Fill(firstWord, double.NaN); Array.Fill(ended, double.NaN); Array.Fill(starting, double.NaN);
        var done = new TaskCompletionSource();
        using var ui = new OneThreadContext();
        double playAt = 0;

        ui.Run(() =>
        {
            var doc = new ReadingDocument();
            int lastSentence = 0;
            var controller = new ReadingController(doc, catalog.EngineFor, () => catalog.DefaultFallbackVoice, catalog.ClearUpcoming,
                post: action => ui.Run(action))
            {
                Voice = voice,
                Rate = settings.Rate,
                GapMs = settings.SentenceGapMs,
            };
            doc.PositionChanged += (_, _) =>
            {
                double t = clock.Elapsed.TotalMilliseconds;
                int s = doc.Position.Sentence;
                if (s != lastSentence && lastSentence < sentenceCount && double.IsNaN(ended[lastSentence])) ended[lastSentence] = t;
                lastSentence = s;
                if (s < sentenceCount && doc.SpokenWord >= 0 && double.IsNaN(firstWord[s])) firstWord[s] = t;
            };
            controller.SentenceStarting += () => { int s = doc.Position.Sentence; if (s < sentenceCount && double.IsNaN(starting[s])) starting[s] = clock.Elapsed.TotalMilliseconds; };
            controller.Finished += () => done.TrySetResult();
            doc.Append(text);
            playAt = clock.Elapsed.TotalMilliseconds;
            controller.Play();
        });

        await Task.WhenAny(done.Task, Task.Delay(180_000));
        output.WriteLine("");
        output.WriteLine(name + $"   (times in ms from Play; first audio = first word reported, which lags real sound by up to ~50 ms)");
        output.WriteLine("sent  asked-to-speak  first-audio  audio-ended  audio-ms  gap-before(ms)  gap-minus-setting(ms)");
        for (int i = 0; i < sentenceCount; i++)
        {
            double audio = ended[i] - firstWord[i];
            string gap = i == 0 ? $"first audio {firstWord[0] - playAt,6:F0}" : $"{firstWord[i] - ended[i - 1],6:F0}";
            string over = i == 0 ? "" : $"{firstWord[i] - ended[i - 1] - settings.SentenceGapMs,6:F0}";
            output.WriteLine($"{i + 1,4}  {starting[i],14:F0}  {firstWord[i],11:F0}  {ended[i],11:F0}  {audio,8:F0}  {gap,14}  {over,21}");
        }
    }
}
