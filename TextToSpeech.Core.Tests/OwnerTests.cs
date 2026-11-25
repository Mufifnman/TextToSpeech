using TextToSpeech.Core.Reading;

namespace TextToSpeech.Core.Tests
{
    /// <summary>Per-owner text in the reading document: what the local API's take / replace is built on.</summary>
    public class OwnerTests
    {
        private static ReadingDocument Doc(params (string Text, string? Owner)[] parts)
        {
            var d = new ReadingDocument();
            foreach (var (text, owner) in parts)
            {
                d.Append(text, owner: owner);
            }
            return d;
        }

        [Fact]
        public void Append_tags_sentences_with_the_owner()
        {
            var d = Doc(("One. Two.", "a"), ("Three.", null));
            Assert.Equal("a", d.Sentences[0].Owner);
            Assert.Equal("a", d.Sentences[1].Owner);
            Assert.Null(d.Sentences[2].Owner);
            Assert.Equal("One. Two.", d.OwnerText("a"));
            Assert.Equal(2, d.OwnerSentenceCount("a"));
        }

        [Fact]
        public void Append_still_works_with_two_arguments_and_does_not_move_the_reader()
        {
            var d = new ReadingDocument();
            d.Append("One.");
            d.Append("Two.", s => true);
            Assert.Equal(2, d.Count);
            Assert.Equal(0, d.Position.Sentence);
        }

        [Fact]
        public void InsertAt_in_front_of_the_reader_starts_the_new_text()
        {
            var d = Doc(("One. Two.", "a"));
            d.JumpTo(1, 0);
            d.InsertAt(1, "New.", owner: "b");
            Assert.Equal(new[] { "One.", "New.", "Two." }, d.Sentences.Select(s => s.Text));
            Assert.Equal(new ReadingPosition(1, 0), d.Position);
        }

        [Fact]
        public void InsertAt_after_the_reader_does_not_move_it()
        {
            var d = Doc(("One. Two.", "a"));
            d.InsertAt(2, "New.", owner: "b");
            Assert.Equal(new ReadingPosition(0, 0), d.Position);
            Assert.Equal(3, d.Count);
        }

        [Fact]
        public void InsertAt_before_the_reader_shifts_the_position()
        {
            var d = Doc(("One. Two. Three.", "a"));
            d.JumpTo(2, 0);
            d.InsertAt(0, "Early.", owner: "b");
            Assert.Equal(new ReadingPosition(3, 0), d.Position);
            Assert.Equal("Three.", d.CurrentSentence!.Text);
        }

        [Fact]
        public void InsertAt_at_the_end_when_the_reader_is_done_makes_the_new_text_current()
        {
            var d = Doc(("One.", "a"));
            d.CompleteSentence();
            Assert.True(d.IsAtEnd);
            d.Append("Fresh.", owner: "b");
            Assert.Equal("Fresh.", d.CurrentSentence!.Text);
        }

        [Fact]
        public void Peek_with_nothing_read_returns_everything_as_remainder()
        {
            var d = Doc(("One. Two.", "a"));
            var split = d.PeekOwner("a");
            Assert.Equal("", split.ReadText);
            Assert.Equal("One. Two.", split.Remainder);
            Assert.Equal(0, split.CharOffset);
        }

        [Fact]
        public void Peek_splits_at_the_start_of_the_current_sentence_and_read_plus_remainder_is_the_text()
        {
            var d = Doc(("Build finished. Three tests failed. Parser broke.", "a"));
            d.JumpTo(1, 0);
            var split = d.PeekOwner("a");
            Assert.Equal("Build finished. ", split.ReadText);
            Assert.Equal("Three tests failed. Parser broke.", split.Remainder);
            Assert.Equal(d.OwnerText("a"), split.ReadText + split.Remainder);
        }

        [Fact]
        public void Peek_splits_inside_a_sentence_at_the_reader_word()
        {
            var d = Doc(("Build finished. Three tests failed today.", "a"));
            d.JumpTo(1, 2); // "failed"
            var split = d.PeekOwner("a");
            Assert.Equal("Build finished. Three tests ", split.ReadText);
            Assert.Equal("failed today.", split.Remainder);
            Assert.Equal(2, split.Word);
            Assert.Equal(d.OwnerText("a"), split.ReadText + split.Remainder);
        }

        [Fact]
        public void Peek_from_sentence_start_ignores_the_word()
        {
            var d = Doc(("Build finished. Three tests failed today.", "a"));
            d.JumpTo(1, 2);
            var split = d.PeekOwner("a", fromSentenceStart: true);
            Assert.Equal("Build finished. ", split.ReadText);
            Assert.Equal("Three tests failed today.", split.Remainder);
        }

        [Fact]
        public void Peek_when_everything_was_read_has_an_empty_remainder()
        {
            var d = Doc(("One. Two.", "a"));
            d.CompleteSentence();
            d.CompleteSentence();
            var split = d.PeekOwner("a");
            Assert.Equal("", split.Remainder);
            Assert.Equal("One. Two.", split.ReadText);
            Assert.Equal(-1, split.FirstIndex);
        }

        [Fact]
        public void Peek_only_counts_the_owners_own_sentences_when_others_are_mixed_in()
        {
            var d = Doc(("A one. A two.", "a"), ("B one.", "b"), ("A three.", "a"));
            d.JumpTo(1, 0); // reader is on "A two."
            var split = d.PeekOwner("a");
            Assert.Equal("A one. ", split.ReadText);
            Assert.Equal("A two. A three.", split.Remainder);
        }

        [Fact]
        public void Take_removes_the_remainder_keeps_the_read_part_and_other_owners()
        {
            var d = Doc(("A one. A two.", "a"), ("B one.", "b"), ("A three.", "a"));
            d.JumpTo(1, 0);
            var split = d.TakeOwnerRemainder("a");
            Assert.Equal("A two. A three.", split.Remainder);
            Assert.Equal(new[] { "A one.", "B one." }, d.Sentences.Select(s => s.Text));
            Assert.Equal("B one.", d.CurrentSentence!.Text);   // reader moved on to what follows
            Assert.Equal(0, d.Position.Word);
        }

        [Fact]
        public void Take_mid_sentence_keeps_the_read_words_as_a_short_sentence()
        {
            var d = Doc(("Build finished. Three tests failed today.", "a"));
            d.JumpTo(1, 2);
            var split = d.TakeOwnerRemainder("a");
            Assert.Equal("failed today.", split.Remainder);
            Assert.Equal(new[] { "Build finished.", "Three tests" }, d.Sentences.Select(s => s.Text));
            Assert.True(d.IsAtEnd);
        }

        [Fact]
        public void Take_leaves_the_reader_alone_when_it_is_on_someone_elses_sentence()
        {
            var d = Doc(("B one.", "b"), ("A one. A two.", "a"));
            var split = d.TakeOwnerRemainder("a");
            Assert.Equal("A one. A two.", split.Remainder);
            Assert.Equal(1, d.Count);
            Assert.Equal(new ReadingPosition(0, 0), d.Position);
        }

        [Fact]
        public void Take_with_nothing_unread_changes_nothing()
        {
            var d = Doc(("One.", "a"), ("Two.", "b"));
            d.CompleteSentence();
            var before = d.Sentences.Select(s => s.Text).ToArray();
            var split = d.TakeOwnerRemainder("a");
            Assert.Equal("", split.Remainder);
            Assert.Equal(before, d.Sentences.Select(s => s.Text));
        }

        [Fact]
        public void Take_then_insert_the_edited_text_reads_the_new_text_next()
        {
            var d = Doc(("Build finished. Three tests failed.", "a"));
            d.JumpTo(1, 0);
            d.TakeOwnerRemainder("a");
            Assert.True(d.IsAtEnd);
            d.InsertAt(d.Position.Sentence, "Two tests failed.", owner: "a");
            Assert.Equal("Two tests failed.", d.CurrentSentence!.Text);
            Assert.Equal("Build finished. Two tests failed.", d.OwnerText("a"));
        }

        [Fact]
        public void RemoveOwnerFrom_removes_only_that_owners_later_sentences_and_keeps_the_reader_on_its_sentence()
        {
            var d = Doc(("A one. A two. A three.", "a"), ("B one.", "b"));
            d.JumpTo(1, 0);                      // on "A two."
            int removed = d.RemoveOwnerFrom("a", 2);
            Assert.Equal(1, removed);
            Assert.Equal(new[] { "A one.", "A two.", "B one." }, d.Sentences.Select(s => s.Text));
            Assert.Equal("A two.", d.CurrentSentence!.Text);
        }

        [Fact]
        public void RemoveOwnerFrom_the_current_sentence_moves_the_reader_to_what_follows()
        {
            var d = Doc(("A one. A two.", "a"), ("B one.", "b"));
            d.JumpTo(0, 0);
            d.RemoveOwnerFrom("a", 0);
            Assert.Equal("B one.", d.CurrentSentence!.Text);
            Assert.Equal(new ReadingPosition(0, 0), d.Position);
        }

        [Fact]
        public void RemoveOwnerFrom_earlier_than_the_reader_shifts_the_position_back()
        {
            var d = Doc(("A one. A two.", "a"), ("B one. B two.", "b"));
            d.JumpTo(3, 0);                      // on "B two."
            d.RemoveOwnerFrom("a", 0);
            Assert.Equal("B two.", d.CurrentSentence!.Text);
        }
    }
}
