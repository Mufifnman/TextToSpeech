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
