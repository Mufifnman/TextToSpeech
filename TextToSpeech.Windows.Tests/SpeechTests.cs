using TextToSpeech.Speech;
using Xunit;

namespace TextToSpeech.Windows.Tests;

public class WordTimingEstimatorTests
{
    [Fact]
    public void OneBoundaryPerWord_WithCorrectOffsets()
    {
        var words = WordTimingEstimator.Estimate("Hello big world.", 3.0);
        Assert.Equal(3, words.Count);
        Assert.Equal(new WordBoundary(0, 5), words[0].Boundary);
        Assert.Equal(new WordBoundary(6, 3), words[1].Boundary);
        Assert.Equal(new WordBoundary(10, 6), words[2].Boundary);
    }

    [Fact]
    public void Times_AreIncreasingAndInsideTheClip()
    {
        var words = WordTimingEstimator.Estimate("One, two three. Four five six seven.", 5.0);
        for (int i = 1; i < words.Count; i++)
            Assert.True(words[i].StartSeconds > words[i - 1].StartSeconds);
        Assert.All(words, w => Assert.InRange(w.StartSeconds, 0, 5.0));
    }

    [Fact]
    public void LongerWordsGetMoreTime()
    {
        var words = WordTimingEstimator.Estimate("a extraordinarily b", 6.0);
        double first = words[1].StartSeconds - words[0].StartSeconds;
        double second = words[2].StartSeconds - words[1].StartSeconds;
        Assert.True(second > first);
    }

    [Theory]
    [InlineData("", 2.0)]
    [InlineData("   ", 2.0)]
    [InlineData("text", 0.0)]
    public void NothingToTime_ReturnsEmpty(string text, double seconds)
    {
        Assert.Empty(WordTimingEstimator.Estimate(text, seconds));
    }
}

public class AudioGainTests
{
    [Theory]
    [InlineData(0.5f, 1.0f, 0.5f)]
    [InlineData(-0.5f, 1.0f, -0.5f)]
    [InlineData(0.5f, 0.5f, 0.25f)]
    [InlineData(0.5f, 0.0f, 0.0f)]
    public void AtOrBelowFullGain_IsAPlainMultiply(float sample, float gain, float expected)
    {
        Assert.Equal(expected, AudioGain.Apply(sample, gain), 5);
    }

    [Fact]
    public void FullGain_DoesNotAlterLoudOriginalAudio()
    {
        Assert.Equal(0.95f, AudioGain.Apply(0.95f, 1.0f), 5);
    }

    [Fact]
    public void BoostedQuietAudio_IsAPlainMultiply()
    {
        Assert.Equal(0.6f, AudioGain.Apply(0.5f, 1.2f), 5); // 0.6 is under the soft knee
    }

    [Fact]
    public void BoostedLoudAudio_NeverClips_AndKeepsOrder()
    {
        float previous = float.MinValue;
        for (float sample = -1f; sample <= 1f; sample += 0.05f)
        {
            float result = AudioGain.Apply(sample, 1.2f);
            Assert.InRange(result, -1f, 1f);
            Assert.True(result >= previous);   // louder input is never quieter out
            previous = result;
        }
        Assert.InRange(AudioGain.Apply(1f, 1.2f), 0.9f, 1f);
        Assert.Equal(-AudioGain.Apply(0.9f, 1.2f), AudioGain.Apply(-0.9f, 1.2f), 6); // symmetric
    }
}


public class GainSampleProviderTests
{
    private static float[] ReadAll(GainSampleProvider provider, int chunk = 64)
    {
        var all = new List<float>();
        var buffer = new float[chunk];
        int n;
        while ((n = provider.Read(buffer, 0, buffer.Length)) > 0)
        {
            all.AddRange(buffer.Take(n));
        }
        return all.ToArray();
    }

    [Fact]
    public void SteadyGain_MatchesTheGainFunction_FromTheFirstSample()
    {
        var samples = Enumerable.Range(0, 500).Select(i => (float)Math.Sin(i / 20.0) * 0.5f).ToArray();
        var output = ReadAll(new GainSampleProvider(samples, 22050, () => 0.5f));

        Assert.Equal(samples.Length, output.Length);
        for (int i = 0; i < samples.Length; i++)
        {
            Assert.Equal(AudioGain.Apply(samples[i], 0.5f), output[i], 5);
        }
    }

    [Fact]
    public void ChangingTheGainWhilePlaying_RampsInsteadOfJumping_AndSettlesOnTheTarget()
    {
        var samples = Enumerable.Repeat(0.5f, 5000).ToArray();   // constant signal: output level = gain * 0.5
        float gain = 1.0f;
        var provider = new GainSampleProvider(samples, 22050, () => gain);
        var first = new float[1000];
        provider.Read(first, 0, first.Length);
        Assert.Equal(0.5f, first[^1], 3);

        gain = 0.2f;                                              // the user drags the slider down
        var rest = new float[4000];
        int n = provider.Read(rest, 0, rest.Length);
        Assert.Equal(4000, n);

        float biggestStep = 0;
        for (int i = 1; i < n; i++) biggestStep = Math.Max(biggestStep, Math.Abs(rest[i] - rest[i - 1]));
        Assert.True(biggestStep < 0.01f, "gain jumped instead of ramping: " + biggestStep); // no click
        Assert.True(rest[0] > 0.3f);                              // still close to the old level at the start
        Assert.Equal(0.1f, rest[^1], 2);                          // and settled on the new one
    }

    [Fact]
    public void EndOfClip_ReturnsZero()
    {
        var provider = new GainSampleProvider(new float[10], 22050, () => 1f);
        var buffer = new float[32];
        Assert.Equal(10, provider.Read(buffer, 0, 32));
        Assert.Equal(0, provider.Read(buffer, 0, 32));
    }
}

public class KokoroNamesTests
{
    [Theory]
    [InlineData("af_bella", "Bella (US, female)")]
    [InlineData("bm_george", "George (UK, male)")]
    [InlineData("bf_emma", "Emma (UK, female)")]
    [InlineData("am_adam", "Adam (US, male)")]
    [InlineData("af", "Default (US, female)")]
    [InlineData("Custom Voice", "Custom Voice")]   // names from a speakers.txt are left alone
    [InlineData("", "")]
    public void FriendlyNames(string id, string expected)
    {
        Assert.Equal(expected, KokoroNames.Friendly(id));
    }

    [Fact]
    public void EveryVoiceOfTheV019Release_GetsAReadableName()
    {
        Assert.Equal(11, NeuralModelFolder.KokoroEnV019.Length);
        Assert.All(NeuralModelFolder.KokoroEnV019, id =>
        {
            string friendly = KokoroNames.Friendly(id);
            Assert.NotEqual(id, friendly);
            Assert.Contains("(", friendly);
        });
        Assert.Equal(11, NeuralModelFolder.KokoroEnV019.Distinct().Count());
    }
}


public class LookAheadCacheTests
{
    [Fact]
    public async Task SameKey_StartsTheWorkOnlyOnce_AndSharesTheResult()
    {
        var cache = new LookAheadCache<int>(4);
        int runs = 0;
        var first = cache.GetOrStart("a", () => { Interlocked.Increment(ref runs); return 41; });
        var second = cache.GetOrStart("a", () => { Interlocked.Increment(ref runs); return 99; });

        Assert.Equal(41, await first);
        Assert.Equal(41, await second);
        Assert.Equal(1, runs);
    }

    [Fact]
    public async Task OnlyTheNewestEntriesAreKept()
    {
        var cache = new LookAheadCache<int>(2);
        await cache.GetOrStart("a", () => 1);
        await cache.GetOrStart("b", () => 2);
        await cache.GetOrStart("c", () => 3);

        Assert.Equal(2, cache.Count);
        Assert.False(cache.Contains("a"));
        Assert.True(cache.Contains("b"));
        Assert.True(cache.Contains("c"));
    }

    [Fact]
    public async Task RemoveAndClear_ForgetEntries_SoTheyRunAgain()
    {
        var cache = new LookAheadCache<int>(4);
        int runs = 0;
        await cache.GetOrStart("a", () => ++runs);
        cache.Remove("a");
        await cache.GetOrStart("a", () => ++runs);
        Assert.Equal(2, runs);

        cache.Clear();
        Assert.Equal(0, cache.Count);
    }
}
