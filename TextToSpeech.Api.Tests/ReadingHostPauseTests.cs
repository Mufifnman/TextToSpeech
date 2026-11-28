using TextToSpeech.Api;
using Xunit;

namespace TextToSpeech.Api.Tests
{
    public class ReadingHostPauseTests
    {
        private readonly HostRig rig = new();

        private Task<SpeakResponse> SpeakWithQuestions(bool? playAll = null) =>
            rig.Host.SpeakAsync(new SpeakRequest("Done with the parser.", "claude-code", null, null, null, null, null, null,
                Questions: new[] { "Which branch?", "Add tests?" }, PlayAll: playAll));

        [Fact]
        public async Task Questions_are_read_as_a_numbered_list_and_reading_waits_after_each()
        {
            await SpeakWithQuestions();
            Assert.Equal("Done with the parser.", rig.Engine.Calls[0].Text);
            rig.Finish(0);
            Assert.Equal("One. Which branch?", rig.Engine.Calls[1].Text);
            rig.Finish(1);

            var status = rig.Host.Snapshot();
            Assert.Equal("paused_at_pause", status.State);
            Assert.Equal("q1", status.PausedAt!.PauseId);
            Assert.Equal("Which branch?", status.PausedAt.Label);
            Assert.Equal(1, status.PausedAt.Number);
            Assert.Equal(2, status.PausedAt.Of);
            Assert.Equal(new[] { "waiting", "pending" }, status.Pauses!.Select(p => p.State));
            Assert.Equal(2, rig.Engine.Calls.Count);   // nothing spoken while waiting
        }

        [Fact]
        public async Task Next_continues_from_the_question_and_marks_it_answered()
        {
            await SpeakWithQuestions();
            rig.Finish(0);
            rig.Finish(1);

            var status = await rig.Host.ControlAsync("next");
            Assert.Equal("Two. Add tests?", rig.Engine.Calls[2].Text);
            Assert.Equal("speaking", status.State);
            Assert.Equal(new[] { "answered", "pending" }, status.Pauses!.Select(p => p.State));
        }

        [Fact]
        public async Task Next_when_not_waiting_is_a_conflict()
        {
            await rig.Speak("Nothing to wait for.");
            var e = await Assert.ThrowsAsync<ApiException>(() => rig.Host.ControlAsync("next"));
            Assert.Equal("not_paused_at_pause", e.Code);
        }

        [Fact]
        public async Task Answered_ahead_of_time_makes_reading_skip_that_question_stop()
        {
            await SpeakWithQuestions();
            await rig.Host.AnsweredAsync("q2", null);
            rig.Finish(0);
            rig.Finish(1);
            await rig.Host.ControlAsync("next");     // q1
            rig.Finish(2);                           // q2 is answered: no stop

            Assert.Equal("idle", rig.Host.Snapshot().State);
        }

        [Fact]
        public async Task Answered_for_an_unknown_pause_is_not_found()
        {
            await SpeakWithQuestions();
            var e = await Assert.ThrowsAsync<ApiException>(() => rig.Host.AnsweredAsync("nope", null));
            Assert.Equal("no_such_pause", e.Code);
        }

        [Fact]
        public async Task PlayAll_on_the_request_reads_straight_through()
        {
            await SpeakWithQuestions(playAll: true);
            rig.Finish(0);
            rig.Finish(1);
            Assert.Equal("Two. Add tests?", rig.Engine.Calls[2].Text);
            Assert.Null(rig.Host.Snapshot().PausedAt);
            Assert.True(rig.Host.Snapshot().PlayAll);
        }

        [Fact]
        public async Task Explicit_pauses_anchor_to_the_text_and_pauses_with_filters_are_refused()
        {
            const string text = "First part.\n\nSecond part.";
            await rig.Host.SpeakAsync(new SpeakRequest(text, "claude-code", null, null, null, null, null, null,
                Pauses: new[] { new PauseRequest("m1", null, text.IndexOf("First", StringComparison.Ordinal)) }));
            rig.Finish(0);
            Assert.Equal("paused_at_pause", rig.Host.Snapshot().State);

            var e = await Assert.ThrowsAsync<ApiException>(() => rig.Host.SpeakAsync(new SpeakRequest(text, "x", null, true, null, null, null, null,
                Pauses: new[] { new PauseRequest("m1", null, 0) })));
            Assert.Equal("pauses_with_filters", e.Code);
        }
    }
}
