using TextToSpeech.Api;
using Xunit;

namespace TextToSpeech.Api.Tests
{
    public class ReadingHostReplyTests
    {
        private readonly HostRig rig = new();

        private Task<SpeakResponse> Ask(bool expectsReply = true, string source = "claude-code") =>
            rig.Host.SpeakAsync(new SpeakRequest("Done with the parser.", source, null, null, null, null, null, null,
                Questions: new[] { "Which branch?", "Add tests?" }, ExpectsReply: expectsReply));

        private void ReachFirstQuestion()
        {
            rig.Finish(0);
            rig.Finish(1);
        }

        [Fact]
        public async Task Status_says_a_caller_is_waiting_for_the_reply_only_when_it_asked()
        {
            await Ask();
            ReachFirstQuestion();
            var waiting = rig.Host.Snapshot().ReplyWaiting;
            Assert.NotNull(waiting);
            Assert.Equal("q1", waiting!.PauseId);
            Assert.Equal("Which branch?", waiting.Question);
            Assert.Equal("claude-code", waiting.Source);
            Assert.Equal(1, waiting.Number);
            Assert.Equal(2, waiting.Of);
        }

        [Fact]
        public async Task Status_has_no_reply_waiting_when_the_caller_did_not_ask()
        {
            await Ask(expectsReply: false);
            ReachFirstQuestion();
            Assert.Null(rig.Host.Snapshot().ReplyWaiting);
        }

        [Fact]
        public async Task A_reply_is_handed_to_the_caller_once_and_reading_goes_on_to_the_next_question()
        {
            await Ask();
            ReachFirstQuestion();

            var reply = await rig.Host.SubmitReplyAsync(new ReplyRequest("  use master  ", null, null));
            Assert.Equal("use master", reply.Text);
            Assert.Equal("q1", reply.PauseId);
            Assert.Equal("Which branch?", reply.Question);
            Assert.Equal("Two. Add tests?", rig.Engine.Calls[2].Text);
            Assert.Contains(rig.Events, e => e.Type == "reply" && e.Detail == "q1");

            var taken = await rig.Host.TakeRepliesAsync(new TakeRepliesRequest("claude-code", null, null));
            Assert.Single(taken.Replies);
            Assert.Equal("use master", taken.Replies[0].Text);
            Assert.Empty((await rig.Host.TakeRepliesAsync(new TakeRepliesRequest("claude-code", null, null))).Replies);
        }

        [Fact]
        public async Task Replies_are_kept_per_source()
        {
            await Ask(source: "claude-code");
            ReachFirstQuestion();
            await rig.Host.SubmitReplyAsync(new ReplyRequest("master", null, null));

            Assert.Empty((await rig.Host.TakeRepliesAsync(new TakeRepliesRequest("someone-else", null, null))).Replies);
            Assert.Single((await rig.Host.GetRepliesAsync()).Replies);   // still there: listing does not remove
            Assert.Single((await rig.Host.TakeRepliesAsync(new TakeRepliesRequest("claude-code", null, null))).Replies);
        }

        [Fact]
        public async Task Has_replies_looks_without_removing()
        {
            await Ask();
            ReachFirstQuestion();
            Assert.False(rig.Host.HasReplies("claude-code", null));
            await rig.Host.SubmitReplyAsync(new ReplyRequest("master", null, null));
            Assert.True(rig.Host.HasReplies("claude-code", null));
            Assert.True(rig.Host.HasReplies("claude-code", null));
            Assert.False(rig.Host.HasReplies("someone-else", null));
        }

        [Fact]
        public async Task A_question_id_two_sessions_share_needs_a_session_id()
        {
            await Ask(source: "window-a");
            await Ask(source: "window-b");
            var e = await Assert.ThrowsAsync<ApiException>(() => rig.Host.SubmitReplyAsync(new ReplyRequest("yes", null, "q1")));
            Assert.Equal("ambiguous_pause", e.Code);
        }

        [Fact]
        public async Task A_reply_when_nobody_is_waiting_is_a_conflict()
        {
            await rig.Speak("Nothing to answer here.");
            var e = await Assert.ThrowsAsync<ApiException>(() => rig.Host.SubmitReplyAsync(new ReplyRequest("hello", null, null)));
            Assert.Equal("no_reply_expected", e.Code);
        }

        [Fact]
        public async Task A_reply_when_the_caller_did_not_ask_is_a_conflict()
        {
            await Ask(expectsReply: false);
            ReachFirstQuestion();
            var e = await Assert.ThrowsAsync<ApiException>(() => rig.Host.SubmitReplyAsync(new ReplyRequest("master", null, null)));
            Assert.Equal("no_reply_expected", e.Code);
        }

        [Fact]
        public async Task A_later_question_can_be_answered_ahead_of_time_by_id()
        {
            await Ask();
            var reply = await rig.Host.SubmitReplyAsync(new ReplyRequest("yes", null, "q2"));
            Assert.Equal("q2", reply.PauseId);
            Assert.Equal("Add tests?", reply.Question);
            Assert.Equal(new[] { "pending", "answered" }, rig.Host.Snapshot().Pauses!.Select(p => p.State));
        }

        [Fact]
        public async Task Naming_an_unknown_question_is_not_found()
        {
            await Ask();
            var e = await Assert.ThrowsAsync<ApiException>(() => rig.Host.SubmitReplyAsync(new ReplyRequest("yes", null, "q9")));
            Assert.Equal("no_such_pause", e.Code);
        }

        [Fact]
        public async Task Expects_reply_without_questions_is_refused()
        {
            var e = await Assert.ThrowsAsync<ApiException>(() => rig.Host.SpeakAsync(
                new SpeakRequest("No questions.", "claude-code", null, null, null, null, null, null, ExpectsReply: true)));
            Assert.Equal("reply_needs_pause", e.Code);
        }
    }
}
