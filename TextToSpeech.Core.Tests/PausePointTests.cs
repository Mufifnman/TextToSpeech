using TextToSpeech.Core.Reading;
using TextToSpeech.Speech;

namespace TextToSpeech.Core.Tests
{
    public class PausePointTests
    {
        private sealed class Rig
        {
            public ReadingDocument Document { get; } = new();
            public FakeEngine Engine { get; } = new();
            public List<TimeSpan> Gaps { get; } = new();
            public int Finished, PauseReached;
            public ReadingController Controller { get; }

            public Rig(string text, params PauseSpec[] pauses)
            {
                Controller = new ReadingController(Document, _ => Engine, delay: (gap, _) => { Gaps.Add(gap); return Task.CompletedTask; })
                {
                    Voice = new VoiceInfo("plain:1", "Plain", "Fake", IsNeural: false),
                    GapMs = 300,
                };
                Controller.Finished += () => Finished++;
                Controller.PauseReached += () => PauseReached++;
                Document.AppendMarkdown(text, pauses: pauses);
            }
        }

        private const string Text = "Intro.\n\n1. Which branch?\n2. Add tests?";

        private static PauseSpec[] Both() => new[]
        {
            new PauseSpec("q1", "Which branch?", Text.IndexOf("Which", StringComparison.Ordinal)),
            new PauseSpec("q2", "Add tests?", Text.IndexOf("Add", StringComparison.Ordinal)),
        };

        [Fact]
        public void Reading_StopsAfterAQuestion_AndPlayCarriesOn()
        {
            var rig = new Rig(Text, Both());
            rig.Controller.Play();
            rig.Engine.Finish(0);                       // Intro.
            Assert.Equal("One. Which branch?", rig.Engine.Calls[1].Text);
            rig.Engine.Finish(1);                       // the question

            Assert.Equal(1, rig.PauseReached);
            Assert.Equal("q1", rig.Controller.PausedAt!.Point.Id);
            Assert.False(rig.Controller.IsPlaying);
            Assert.Equal(2, rig.Engine.Calls.Count);    // nothing spoken while waiting

            rig.Controller.Play();
            Assert.Null(rig.Controller.PausedAt);
            Assert.True(rig.Document.Sentences[1].Pause!.Answered);
            Assert.Equal("Two. Add tests?", rig.Engine.Calls[2].Text);
        }

        [Fact]
        public void PlayAll_ReadsThroughTheQuestions_UsingTheNormalPauses()
        {
            var rig = new Rig(Text, Both());
            rig.Controller.PlayAll = true;
            rig.Controller.Play();
            rig.Engine.Finish(0);
            rig.Engine.Finish(1);

            Assert.Null(rig.Controller.PausedAt);
            Assert.Equal(0, rig.PauseReached);
            Assert.Equal("Two. Add tests?", rig.Engine.Calls[2].Text);
            Assert.Equal(TimeSpan.FromMilliseconds(600), rig.Gaps[1]);   // a list item: double pause
        }

        [Fact]
        public void AQuestionOnTheLastSentence_HoldsFinished_UntilResumed()
        {
            var rig = new Rig("Only question?", new PauseSpec("q1", "Only question?", 0));
            rig.Controller.Play();
            rig.Engine.Finish(0);

            Assert.Equal(0, rig.Finished);
            Assert.NotNull(rig.Controller.PausedAt);
            Assert.True(rig.Document.IsAtEnd);

            rig.Controller.TogglePlayPause();
            Assert.Equal(1, rig.Finished);
            Assert.Null(rig.Controller.PausedAt);
        }

        [Fact]
        public void MarkAnswered_WhileWaiting_Resumes_AndWhileReading_JustSkipsThatPause()
        {
            var rig = new Rig(Text, Both());
            rig.Controller.Play();
            rig.Engine.Finish(0);
            Assert.True(rig.Controller.MarkAnswered(rig.Document.Sentences[2].Pause!));   // q2 answered ahead of time
            rig.Engine.Finish(1);
            Assert.NotNull(rig.Controller.PausedAt);                                      // q1 still waits

            rig.Controller.ContinueFromPause();
            rig.Engine.Finish(2);
            Assert.Null(rig.Controller.PausedAt);                                         // q2 does not stop reading
            Assert.Equal(1, rig.Finished);
        }

        [Fact]
        public void Rewind_AtAQuestion_ClearsTheWaitingState_ButNotTheQuestion()
        {
            var rig = new Rig(Text, Both());
            rig.Controller.Play();
            rig.Engine.Finish(0);
            rig.Engine.Finish(1);
            rig.Controller.Rewind();

            Assert.Null(rig.Controller.PausedAt);
            Assert.False(rig.Document.Sentences[1].Pause!.Answered);
            Assert.Equal(1, rig.Document.Position.Sentence);
        }

        [Fact]
        public void PlayRange_PlaysJustThoseSentences_ThenStops()
        {
            var rig = new Rig("- One.\n- Two. Two b.\n- Three.");
            rig.Controller.PlayRange(1, 2);

            Assert.Equal("Two.", rig.Engine.Calls[0].Text);
            rig.Engine.Finish(0);
            Assert.Equal("Two b.", rig.Engine.Calls[1].Text);
            rig.Engine.Finish(1);

            Assert.Equal(2, rig.Engine.Calls.Count);            // "Three." was not started
            Assert.False(rig.Controller.IsPlaying);
            Assert.Equal(3, rig.Document.Position.Sentence);    // Play would continue after the line
            Assert.Equal(0, rig.Finished);
        }

        [Fact]
        public void PlayRange_IsEndedBySkip_SoReadingCarriesOnNormally()
        {
            var rig = new Rig("One.\n\nTwo.\n\nThree.\n\nFour.");
            rig.Controller.PlayRange(0, 0);
            rig.Controller.Skip();                 // the user moved on by hand
            rig.Engine.Finish(1);                  // "Two." done: no longer a one-line request, so Three follows

            Assert.Equal("Three.", rig.Engine.Calls[2].Text);
            Assert.True(rig.Controller.IsPlaying);
        }

        [Fact]
        public void PlayRange_WithAnIndexPastTheEnd_DoesNothing()
        {
            var rig = new Rig("Only.");
            rig.Controller.PlayRange(5, 6);

            Assert.Empty(rig.Engine.Calls);
        }

        [Fact]
        public void PlayRange_StopsAfterTheLine_AndNormalPlayContinues()
        {
            var rig = new Rig("First.\n\nSecond.\n\nThird.");
            rig.Controller.PlayRange(1, 1);
            rig.Engine.Finish(0);
            Assert.False(rig.Controller.IsPlaying);

            rig.Controller.Play();
            Assert.Equal("Third.", rig.Engine.Calls[1].Text);
        }

        [Fact]
        public void BlocksKeepTheirOriginalText_ForTheRawView()
        {
            var doc = new ReadingDocument();
            doc.AppendMarkdown("## Title\n- **bold** item. More.");
            Assert.Equal("## Title", doc.Sentences[0].Format!.Raw);
            Assert.Equal("- **bold** item. More.", doc.Sentences[1].Format!.Raw);
            Assert.Null(doc.Sentences[2].Format!.Raw);   // only the first sentence of a block carries it
        }

        [Fact]
        public void ContinueFromPause_WhenNotWaiting_ReturnsFalse()
        {
            var rig = new Rig("Plain text.");
            Assert.False(rig.Controller.ContinueFromPause());
        }
    }
}
