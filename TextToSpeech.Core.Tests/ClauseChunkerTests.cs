using TextToSpeech.Speech;

namespace TextToSpeech.Core.Tests
{
    public class ClauseChunkerTests
    {
        private static string[] Pieces(string text, ChunkOptions options) =>
            ClauseChunker.Split(text, options).Select(c => text.Substring(c.Start, c.Length)).ToArray();

        private const string Long =
            "When the committee finally met on Tuesday, after weeks of delay and a great deal of argument, " +
            "it decided that the old plan, which nobody liked, should be replaced by a simpler one, " +
            "and that the new schedule would start in the spring.";

        [Fact]
        public void ShortSentences_AreLeftWhole()
        {
            Assert.Equal(new[] { "This one is short, so it stays in one piece." }, Pieces("This one is short, so it stays in one piece.", ChunkOptions.Normal));
        }

        [Fact]
        public void LongSentence_IsCutAtCommas_AndNothingIsLost()
        {
            var pieces = Pieces(Long, ChunkOptions.Normal);

            Assert.True(pieces.Length >= 2);
            Assert.Equal(Long, string.Join(" ", pieces));          // only the spaces at the cuts are left out
            Assert.All(pieces.Take(pieces.Length - 1), p => Assert.EndsWith(",", p));
        }

        [Fact]
        public void NoPieceIsShorterThanTheMinimum()
        {
            foreach (var options in new[] { ChunkOptions.Normal, ChunkOptions.Eager })
            {
                var pieces = Pieces(Long, options);
                Assert.All(pieces, p => Assert.True(p.Split(' ', StringSplitOptions.RemoveEmptyEntries).Length >= options.MinWords, p));
            }
        }

        [Fact]
        public void EagerMode_MakesASmallerFirstPiece_AndCutsShorterSentences()
        {
            const string medium = "After the long meeting ended, everyone went home and slept very well that night.";   // 14 words
            Assert.Single(Pieces(medium, ChunkOptions.Normal));
            var eager = Pieces(medium, ChunkOptions.Eager);
            Assert.Equal(2, eager.Length);
            Assert.Equal("After the long meeting ended,", eager[0]);
        }

        [Fact]
        public void ThereIsNoCutWithoutClausePunctuation_OrInsideNumbers()
        {
            string noCommas = string.Join(" ", Enumerable.Repeat("word", 40));
            Assert.Single(Pieces(noCommas, ChunkOptions.Normal));

            string numbers = "The total reached 1,000,000 units across all the regions we sell in during the whole of last year and then some more units besides.";
            Assert.Single(Pieces(numbers, ChunkOptions.Normal));
        }

        [Fact]
        public void ChunkPositions_PointIntoTheSentence()
        {
            foreach (var c in ClauseChunker.Split(Long, ChunkOptions.Normal))
            {
                Assert.False(char.IsWhiteSpace(Long[c.Start]));
                Assert.False(char.IsWhiteSpace(Long[c.Start + c.Length - 1]));
            }
        }

        [Fact]
        public void Deterministic_SoPreparedAudioIsFoundAgain()
        {
            Assert.Equal(ClauseChunker.Split(Long, ChunkOptions.Normal), ClauseChunker.Split(Long, ChunkOptions.Normal));
        }
    }
}
