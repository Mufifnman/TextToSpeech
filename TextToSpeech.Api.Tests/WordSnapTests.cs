using TextToSpeech.Api;
using Xunit;

namespace TextToSpeech.Api.Tests
{
    public class WordSnapTests
    {
        [Theory]
        [InlineData("one two three", 0, 0)]
        [InlineData("one two three", 2, 0)]
        [InlineData("one two three", 4, 4)]
        [InlineData("one two three", 6, 4)]
        [InlineData("one two three", 3, 4)]   // inside whitespace: next word
        [InlineData("one two three", 13, 13)] // end
        [InlineData("one two three", 99, 13)] // clamped
        [InlineData("one two three", -5, 0)]
        [InlineData("", 0, 0)]
        public void Down_rounds_to_word_start(string text, int offset, int expected) =>
            Assert.Equal(expected, WordSnap.Down(text, offset));
    }
}
