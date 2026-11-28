using TextToSpeech.Core.Reading;

namespace TextToSpeech.Core.Tests
{
    public class MarkdownTests
    {
        [Fact]
        public void Headings_Lists_AndParagraphs_BecomeBlocks_WithoutMarkup()
        {
            const string text = "# Plan\nWe will **do** the `thing`.\n\n- first item\n- second [link](http://x.y)\n  - nested\n\n1. one\n2. two";
            var blocks = MarkdownReader.Parse(text);

            Assert.Collection(blocks,
                b => { Assert.Equal(BlockKind.Heading, b.Kind); Assert.Equal(1, b.Level); Assert.Equal("Plan", b.Text); },
                b => { Assert.Equal(BlockKind.Paragraph, b.Kind); Assert.Equal("We will do the thing.", b.Text); },
                b => { Assert.Equal(BlockKind.ListItem, b.Kind); Assert.Equal(0, b.Level); Assert.Equal("•", b.Marker); Assert.Equal("first item", b.Text); },
                b => { Assert.Equal("second link", b.Text); },
                b => { Assert.Equal(1, b.Level); Assert.Equal("nested", b.Text); },
                b => { Assert.Equal(1, b.Ordinal); Assert.Equal("1.", b.Marker); Assert.Equal(0, b.Level); },
                b => { Assert.Equal(2, b.Ordinal); });
        }

        [Fact]
        public void CodeFence_BecomesOneBlockPerLine_AndCheckboxesGetAMark()
        {
            var blocks = MarkdownReader.Parse("```cs\nvar a = 1;\n\nvar b = 2;\n```\n- [x] done\n- [ ] todo");
            Assert.Equal(new[] { BlockKind.Code, BlockKind.Code, BlockKind.ListItem, BlockKind.ListItem }, blocks.Select(b => b.Kind));
            Assert.Equal("var a = 1;", blocks[0].Text);
            Assert.Equal("☑", blocks[2].Marker);
            Assert.Equal("☐", blocks[3].Marker);
            Assert.Equal("todo", blocks[3].Text);
        }

        [Fact]
        public void SnakeCase_AndMathStars_AreNotTreatedAsMarkup()
        {
            Assert.Equal("use my_var_name and 2 * 3 * 4", MarkdownReader.StripInline("use my_var_name and 2 * 3 * 4"));
            Assert.Equal("bold and italic", MarkdownReader.StripInline("**bold** and _italic_"));
        }

        [Fact]
        public void LooksLikeMarkdown_NeedsStructure()
        {
            Assert.True(MarkdownReader.LooksLikeMarkdown("Intro\n- a\n- b"));
            Assert.True(MarkdownReader.LooksLikeMarkdown("## Title"));
            Assert.False(MarkdownReader.LooksLikeMarkdown("Just a **sentence**. And another."));
        }

        [Fact]
        public void NumberedItems_AreSpokenWithTheirNumber_AndPauseTwiceAsLong()
        {
            var doc = new ReadingDocument();
            doc.AppendMarkdown("1. Alpha one. Alpha two.\n2. Beta.");

            Assert.Equal(3, doc.Count);
            Assert.Equal("One. Alpha one.", doc.Sentences[0].SpokenText);
            Assert.Equal("Alpha one.", doc.Sentences[0].Text);       // the number is not part of the words
            Assert.Equal("Alpha two.", doc.Sentences[1].SpokenText); // only the first sentence of an item has it
            Assert.Equal("Two. Beta.", doc.Sentences[2].SpokenText);
            Assert.Equal(1, doc.Sentences[0].Format!.GapFactor);
            Assert.Equal(2, doc.Sentences[1].Format!.GapFactor);     // after the last sentence of an item
            Assert.Equal(2, doc.Sentences[2].Format!.GapFactor);
            Assert.True(doc.Sentences[0].Format!.StartsBlock);
            Assert.False(doc.Sentences[1].Format!.StartsBlock);
        }

        [Fact]
        public void TextToSpeak_StartsWithThePrefix_ButHighlightsTheFirstWord()
        {
            var doc = new ReadingDocument();
            doc.AppendMarkdown("1. Alpha beta.");

            Assert.Equal("One. Alpha beta.", doc.GetTextToSpeak(out int offset));
            Assert.Equal(-5, offset);                  // "One. " is 5 characters
            doc.ReportSpokenCharacter(offset + 0);     // the engine is on "One"
            Assert.Equal(0, doc.SpokenWord);
            doc.ReportSpokenCharacter(offset + 5);     // "Alpha"
            Assert.Equal(0, doc.SpokenWord);
            doc.ReportSpokenCharacter(offset + 11);    // "beta."
            Assert.Equal(1, doc.SpokenWord);
        }

        [Fact]
        public void UpcomingTexts_MatchWhatWillBeSpoken_SoPrewarmedAudioIsUsed()
        {
            var doc = new ReadingDocument();
            doc.AppendMarkdown("Intro.\n\n1. First.\n2. Second.");
            var upcoming = doc.PeekUpcomingTexts(2);

            doc.JumpTo(1);
            Assert.Equal(upcoming[0], doc.GetTextToSpeak(out _));
            doc.JumpTo(2);
            Assert.Equal(upcoming[1], doc.GetTextToSpeak(out _));
        }

        [Fact]
        public void PauseSpecs_AttachToTheBlockThatContainsTheOffset()
        {
            const string text = "Intro here.\n\n1. Which branch?\n2. Add tests?\n\nBye.";
            var doc = new ReadingDocument();
            doc.AppendMarkdown(text, pauses: new[]
            {
                new PauseSpec("a", "Which branch?", text.IndexOf("Which", StringComparison.Ordinal)),
                new PauseSpec("b", null, text.Length + 50),   // past the end: the last block
            });

            var pauses = doc.Pauses();
            Assert.Equal(2, pauses.Count);
            Assert.Equal("Which branch?", pauses[0].Sentence.Text);
            Assert.Equal("a", pauses[0].Sentence.Pause!.Id);
            Assert.Equal("Bye.", pauses[1].Sentence.Text);
            Assert.Null(pauses[1].Sentence.Pause!.Label);
        }

        [Fact]
        public void QuestionList_DoesNotRepeatAQuestionTheTextAlreadySays()
        {
            var (text, pauses) = QuestionList.Append("Done. **Which branch** should I use?\n\nAlso, ok.", new[] { "Which branch should I use?", "Add tests?" });
            Assert.Equal("Done. **Which branch** should I use?\n\nAlso, ok.\n\n1. Add tests?\n", text);   // only the new one is added
            Assert.Equal(new[] { "q1", "q2" }, pauses.Select(p => p.Id));
            Assert.Equal("Which branch", text.Substring(pauses[0].At, "Which branch".Length));   // q1 waits where it was said

            var doc = new ReadingDocument();
            doc.AppendMarkdown(text, pauses: pauses);
            Assert.Equal(new[] { "q1", "q2" }, doc.Pauses().Select(p => p.Sentence.Pause!.Id));
            Assert.Equal(1, doc.Sentences.Count(s => s.Text.Contains("Which branch")));
        }

        [Fact]
        public void QuestionList_AddsANumberedList_WithOnePausePerQuestion()
        {
            var (text, pauses) = QuestionList.Append("Done with the parser.", new[] { "Which branch?", "  ", "Add tests?" });
            Assert.Equal("Done with the parser.\n\n1. Which branch?\n2. Add tests?\n", text);
            Assert.Equal(new[] { "q1", "q2" }, pauses.Select(p => p.Id));
            Assert.Equal("1. Which branch?", text.Substring(pauses[0].At, "1. Which branch?".Length));

            var doc = new ReadingDocument();
            doc.AppendMarkdown(text, pauses: pauses);
            Assert.Equal(new[] { "Which branch?", "Add tests?" }, doc.Pauses().Select(p => p.Sentence.Text));
        }

        [Fact]
        public void PlainMode_DoesNotInterpretMarkup()
        {
            var doc = new ReadingDocument();
            doc.AppendMarkdown("# Not a heading. - not a list", markdown: false);
            Assert.StartsWith("# Not a heading.", doc.Sentences[0].Text);
            Assert.Null(doc.Sentences[0].Format);
        }
    }
}
