using TextToSpeech.Core.Reading;

namespace TextToSpeech.Core.Tests
{
    public class SentenceSplitterTests
    {
        [Fact]
        public void SplitsOnPeriodQuestionAndExclamation_KeepingPunctuation()
        {
            var result = SentenceSplitter.Split("Hello there. How are you? Great!");
            Assert.Equal(new[] { "Hello there.", "How are you?", "Great!" }, result);
        }

        [Theory]
        [InlineData("Dr. Smith went home. He slept.", new[] { "Dr. Smith went home.", "He slept." })]
        [InlineData("Use tools, e.g. Hammers. Then rest.", new[] { "Use tools, e.g. Hammers.", "Then rest." })]
        [InlineData("J. R. R. Tolkien wrote books. They sold.", new[] { "J. R. R. Tolkien wrote books.", "They sold." })]
        [InlineData("Pi is 3.14 roughly. Yes.", new[] { "Pi is 3.14 roughly.", "Yes." })]
        [InlineData("Apples, pears, etc. They are fruit.", new[] { "Apples, pears, etc.", "They are fruit." })]
        [InlineData("It was late. and dark. Then dawn.", new[] { "It was late. and dark.", "Then dawn." })]
        public void HandlesAbbreviationsInitialsDecimalsAndLowercase(string input, string[] expected)
        {
            Assert.Equal(expected, SentenceSplitter.Split(input));
        }

        [Fact]
        public void ClosingQuotesStayWithTheSentence()
        {
            var result = SentenceSplitter.Split("He said \"Stop.\" Then he left.");
            Assert.Equal(new[] { "He said \"Stop.\"", "Then he left." }, result);
        }

        [Fact]
        public void EllipsisAndInterrobangEndSentencesBeforeCapitals()
        {
            var result = SentenceSplitter.Split("Wait... What?! No… Fine.");
            Assert.Equal(new[] { "Wait...", "What?!", "No…", "Fine." }, result);
        }

        [Fact]
        public void BlankLineIsABreak_SingleNewlineIsASpace()
        {
            var result = SentenceSplitter.Split("A heading\n\nThe body text\ncontinues here");
            Assert.Equal(new[] { "A heading", "The body text continues here" }, result);
        }

        [Fact]
        public void DropsEmptyAndPunctuationOnlyPieces()
        {
            Assert.Empty(SentenceSplitter.Split("  ...  \n\n  "));
            Assert.Empty(SentenceSplitter.Split(null));
        }

        [Fact]
        public void BreaksVeryLongSentencesAtClauses()
        {
            string clause = string.Join(" ", Enumerable.Repeat("word", 20));
            string longText = string.Join(", ", Enumerable.Repeat(clause, 6)) + ".";
            var result = SentenceSplitter.Split(longText, maxSentenceLength: 250);

            Assert.True(result.Count > 1);
            Assert.All(result, s => Assert.True(s.Length <= 250, s));
            Assert.Equal(longText.Replace(" ", ""), string.Concat(result).Replace(" ", ""));
        }

        [Fact]
        public void CollapsesWhitespace()
        {
            Assert.Equal(new[] { "Too many spaces." }, SentenceSplitter.Split("Too   many \t spaces."));
        }
    }
}
