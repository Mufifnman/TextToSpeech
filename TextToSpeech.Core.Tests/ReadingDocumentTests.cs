using TextToSpeech.Core.Reading;

namespace TextToSpeech.Core.Tests
{
    public class ReadingDocumentTests
    {
        private static ReadingDocument Make(string text = "One two three. Four five. Six.")
        {
            var doc = new ReadingDocument();
            doc.Append(text);
            return doc;
        }

        [Fact]
        public void StartsAtFirstSentence()
        {
            var doc = Make();
            Assert.Equal(3, doc.Count);
            Assert.Equal(new ReadingPosition(0, 0), doc.Position);
            Assert.Equal("One two three.", doc.GetTextToSpeak(out int offset));
            Assert.Equal(0, offset);
        }

        [Fact]
        public void CompletingSentencesWalksToTheEndWithoutRemovingAnything()
        {
            var doc = Make();
            doc.CompleteSentence();
            doc.CompleteSentence();
            doc.CompleteSentence();
            Assert.True(doc.IsAtEnd);
            Assert.Equal(3, doc.Count);
            Assert.Null(doc.GetTextToSpeak(out _));
        }

        [Fact]
        public void AppendWhileAtEndContinuesWithNewText()
        {
            var doc = Make("Done.");
            doc.CompleteSentence();
            doc.Append("More text.");
            Assert.False(doc.IsAtEnd);
            Assert.Equal("More text.", doc.CurrentSentence!.Text);
        }

        [Fact]
        public void SkipMovesForwardAndStopsAtEnd()
        {
            var doc = Make();
            Assert.True(doc.Skip());
            Assert.Equal(1, doc.Position.Sentence);
            doc.Skip();
            doc.Skip();
            Assert.True(doc.IsAtEnd);
            Assert.False(doc.Skip());
        }

        [Fact]
        public void RewindRestartsCurrentSentenceWhenPartWayIn_ThenGoesBack()
        {
            var doc = Make();
            doc.Skip(); // "Four five."
            doc.ReportSpokenCharacter(5); // "five."
            Assert.Equal(1, doc.SpokenWord);

            doc.Rewind();
            Assert.Equal(new ReadingPosition(1, 0), doc.Position);
            Assert.Equal(-1, doc.SpokenWord);

            doc.Rewind();
            Assert.Equal(new ReadingPosition(0, 0), doc.Position);

            doc.Rewind();
            Assert.Equal(new ReadingPosition(0, 0), doc.Position);
        }

        [Fact]
        public void RewindFromEndGoesToLastSentence()
        {
            var doc = Make();
            doc.Skip(); doc.Skip(); doc.Skip();
            doc.Rewind();
            Assert.Equal(2, doc.Position.Sentence);
        }

        [Fact]
        public void JumpToWordSpeaksFromThatWordAndOffsetsProgress()
        {
            var doc = Make();
            doc.JumpTo(0, 2);
            Assert.Equal("three.", doc.GetTextToSpeak(out int offset));
            Assert.Equal(8, offset);
            Assert.Equal(2, doc.HighlightedWord);

            // Engine reports position 0 of the spoken substring -> sentence char 8 -> word 2.
            doc.ReportSpokenCharacter(offset + 0);
            Assert.Equal(2, doc.SpokenWord);
        }

        [Fact]
        public void StoppingMidSentenceResumesFromTheSpokenWord()
        {
            var doc = Make();
            doc.ReportSpokenCharacter(4); // "two"
            doc.MarkStoppedAtSpokenWord();
            Assert.Equal(new ReadingPosition(0, 1), doc.Position);
            Assert.Equal("two three.", doc.GetTextToSpeak(out int offset));
            Assert.Equal(4, offset);
            Assert.Equal(1, doc.HighlightedWord);
        }

        [Fact]
        public void JumpToClampsWordAndRejectsBadSentence()
        {
            var doc = Make();
            doc.JumpTo(1, 99);
            Assert.Equal(new ReadingPosition(1, 1), doc.Position);
            Assert.Throws<ArgumentOutOfRangeException>(() => doc.JumpTo(5));
        }

        [Fact]
        public void PositionChangedFiresOnlyWhenTheWordChanges()
        {
            var doc = Make();
            int fired = 0;
            doc.PositionChanged += (_, _) => fired++;
            doc.ReportSpokenCharacter(0);
            doc.ReportSpokenCharacter(1); // still word 0
            doc.ReportSpokenCharacter(4); // word 1
            Assert.Equal(2, fired);
        }

        [Fact]
        public void ClearResetsEverything()
        {
            var doc = Make();
            doc.Skip();
            doc.Clear();
            Assert.Equal(0, doc.Count);
            Assert.True(doc.IsAtEnd);
            Assert.Equal(new ReadingPosition(0, 0), doc.Position);
        }

        [Fact]
        public void FilterDropsSentences()
        {
            var doc = new ReadingDocument();
            doc.Append("Keep this. Read more at example.com today. Keep that.",
                s => !s.Contains("read more at", StringComparison.OrdinalIgnoreCase));
            Assert.Equal(new[] { "Keep this.", "Keep that." }, doc.Sentences.Select(s => s.Text));
        }

        [Fact]
        public void TakeIncompleteTailOnlyOnLastSentenceAndCutOff()
        {
            var doc = Make("Full sentence. Cut off at the bottom of the");
            Assert.Null(doc.TakeIncompleteTail()); // still on the first sentence

            doc.Skip(); // now on the last (cut off) sentence
            Assert.Equal("Cut off at the bottom of the", doc.TakeIncompleteTail());
            Assert.Equal(1, doc.Count);
            Assert.True(doc.IsAtEnd);

            Assert.Null(doc.TakeIncompleteTail()); // remaining last sentence is complete
        }

        [Fact]
        public void ContainsFindsFragments()
        {
            var doc = Make();
            Assert.True(doc.Contains("Four five"));
            Assert.False(doc.Contains("seven"));
            Assert.False(doc.Contains("  "));
        }
    }

    public class ReadingDocumentClipboardTests
    {
        [Fact]
        public void ContainsUnread_IgnoresSentencesAlreadyRead()
        {
            var doc = new ReadingDocument();
            doc.Append("One two three. Four five. Six.");

            Assert.True(doc.ContainsUnread("One two"));   // nothing read yet
            doc.CompleteSentence();
            Assert.False(doc.ContainsUnread("One two"));  // already read: can be pasted again
            Assert.True(doc.Contains("One two"));         // still there for rewind
            Assert.True(doc.ContainsUnread("Four five"));
        }

        [Fact]
        public void ContainsUnread_IsFalseOnceEverythingIsRead_AndForBlankText()
        {
            var doc = new ReadingDocument();
            doc.Append("One. Two.");
            doc.CompleteSentence();
            doc.CompleteSentence();
            Assert.False(doc.ContainsUnread("Two"));
            Assert.False(doc.ContainsUnread("  "));
            Assert.False(doc.ContainsUnread(null));
        }
    }

    public class ReadingDocumentTakeBackTests
    {
        [Fact]
        public void TakeUnread_ReturnsCurrentAndLaterSentences_AndKeepsTheReadOnes()
        {
            var doc = new ReadingDocument();
            doc.Append("One two. Three four. Five six.");
            doc.CompleteSentence(); // "One two." is read

            Assert.Equal("Three four. Five six.", doc.TakeUnread());

            Assert.Equal(1, doc.Count);
            Assert.Equal("One two.", doc.Sentences[0].Text);
            Assert.True(doc.IsAtEnd);
            Assert.Equal("", doc.TakeUnread()); // nothing left
        }

        [Fact]
        public void TakeAll_ReturnsEverythingAndEmptiesTheDocument()
        {
            var doc = new ReadingDocument();
            doc.Append("One two. Three four.");
            doc.CompleteSentence();

            Assert.Equal("One two. Three four.", doc.TakeAll());
            Assert.Equal(0, doc.Count);
            Assert.True(doc.IsAtEnd);
        }

        [Fact]
        public void TakenText_CanBeAppendedAgainAsTheSameSentences()
        {
            var doc = new ReadingDocument();
            doc.Append("One two. Three four. Five six.");
            string back = doc.TakeAll();

            doc.Append(back);
            Assert.Equal(new[] { "One two.", "Three four.", "Five six." }, doc.Sentences.Select(s => s.Text));
        }
    }

    public class SentenceTests
    {
        [Fact]
        public void TokenizesWordsWithAttachedPunctuation()
        {
            var s = new Sentence("\"Hello,\" she said (quietly).");
            Assert.Equal(new[] { "\"Hello,\"", "she", "said", "(quietly)." },
                Enumerable.Range(0, s.Words.Count).Select(s.WordText));
        }

        [Theory]
        [InlineData(0, 0)]
        [InlineData(8, 0)]  // the space after the first word
        [InlineData(9, 1)]
        [InlineData(100, 3)]
        public void WordIndexAtMapsCharacters(int ch, int expected)
        {
            var s = new Sentence("\"Hello,\" she said (quietly).");
            Assert.Equal(expected, s.WordIndexAt(ch));
        }

        [Theory]
        [InlineData("Done.", true)]
        [InlineData("Really?\"", true)]
        [InlineData("cut off", false)]
        public void EndsComplete(string text, bool expected)
        {
            Assert.Equal(expected, new Sentence(text).EndsComplete);
        }
    }
}
