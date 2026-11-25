using TextToSpeech.Api;
using Xunit;

namespace TextToSpeech.Api.Tests
{
    public class SessionStoreTests
    {
        private DateTimeOffset now = new(2026, 11, 25, 12, 0, 0, TimeSpan.Zero);
        private readonly SessionStore store;
        private const string Text = "Build finished. Three tests failed. The parser broke.";

        public SessionStoreTests() => store = new SessionStore(() => now);

        private SessionRecord NewSession() => store.Create("claude-code", Text);

        [Fact]
        public void Create_defaults_source_and_starts_at_revision_1()
        {
            var s = store.Create(" ", "hello");
            Assert.Equal("api", s.Source);
            Assert.Equal(1, s.Revision);
            Assert.Equal(0, s.ReadChars);
            Assert.Equal("hello", s.Remainder);
        }

        [Fact]
        public void Take_splits_text_at_word_start_and_read_plus_remainder_is_the_text()
        {
            var s = NewSession();
            var t = store.Take(s.Id, Text.IndexOf("Three") + 2); // mid word
            Assert.Equal("Build finished. ", t.ReadText);
            Assert.StartsWith("Three tests failed.", t.Remainder);
            Assert.Equal(Text, t.ReadText + t.Remainder);
            Assert.Equal(SessionState.HandedOff, s.State);
            Assert.Equal(now.AddSeconds(120), t.Handoff.LeaseExpiresAt);
        }

        [Fact]
        public void Take_twice_is_a_conflict()
        {
            var s = NewSession();
            store.Take(s.Id, 0);
            var e = Assert.Throws<ApiException>(() => store.Take(s.Id, 0));
            Assert.Equal("already_taken", e.Code);
            Assert.Equal(409, e.HttpStatus);
        }

        [Fact]
        public void Take_with_stale_revision_is_a_conflict()
        {
            var s = NewSession();
            var e = Assert.Throws<ApiException>(() => store.Take(s.Id, 0, ifRevision: 7));
            Assert.Equal("revision_mismatch", e.Code);
        }

        [Fact]
        public void Lease_is_clamped_to_the_maximum()
        {
            var s = NewSession();
            var t = store.Take(s.Id, 0, leaseSeconds: 999999);
            Assert.Equal(now.AddSeconds(3600), t.Handoff.LeaseExpiresAt);
        }

        [Fact]
        public void Replace_swaps_the_remainder_bumps_revision_and_goes_active()
        {
            var s = NewSession();
            var t = store.Take(s.Id, Text.IndexOf("Three"));
            var r = store.Replace(s.Id, t.Handoff.Id, "Two tests failed in the parser.");
            Assert.Equal("Build finished. Two tests failed in the parser.", r.Text);
            Assert.Equal("Build finished. ".Length, r.ReadChars);
            Assert.Equal(2, r.Revision);
            Assert.Equal(SessionState.Active, r.State);
            Assert.Null(r.Handoff);
        }

        [Fact]
        public void Replace_can_start_part_way_in_and_snaps_to_a_word()
        {
            var s = NewSession();
            var t = store.Take(s.Id, 0);
            var r = store.Replace(s.Id, t.Handoff.Id, "alpha beta gamma", startAt: 8); // inside "beta"
            Assert.Equal("beta gamma", r.Remainder);
        }

        [Fact]
        public void Replace_with_empty_text_drops_the_remainder()
        {
            var s = NewSession();
            var t = store.Take(s.Id, Text.IndexOf("Three"));
            var r = store.Replace(s.Id, t.Handoff.Id, "");
            Assert.Equal("", r.Remainder);
        }

        [Fact]
        public void Replace_start_outside_new_text_is_rejected_and_handoff_stays_open()
        {
            var s = NewSession();
            var t = store.Take(s.Id, 0);
            var e = Assert.Throws<ApiException>(() => store.Replace(s.Id, t.Handoff.Id, "abc", startAt: 10));
            Assert.Equal(400, e.HttpStatus);
            Assert.Equal(SessionState.HandedOff, s.State);
        }

        [Fact]
        public void Replace_with_wrong_handoff_id_is_a_conflict()
        {
            var s = NewSession();
            store.Take(s.Id, 0);
            var e = Assert.Throws<ApiException>(() => store.Replace(s.Id, "h_nope", "x"));
            Assert.Equal("bad_handoff", e.Code);
        }

        [Fact]
        public void Replace_without_a_take_is_a_conflict()
        {
            var s = NewSession();
            var e = Assert.Throws<ApiException>(() => store.Replace(s.Id, "h_1", "x"));
            Assert.Equal("not_taken", e.Code);
        }

        [Fact]
        public void Release_restores_the_original_remainder_unchanged()
        {
            var s = NewSession();
            var t = store.Take(s.Id, Text.IndexOf("Three"));
            store.Release(s.Id, t.Handoff.Id);
            Assert.Equal(Text, s.Text);
            Assert.Equal(1, s.Revision);
            Assert.Equal(SessionState.Active, s.State);
            Assert.StartsWith("Three tests", s.Remainder);
        }

        [Fact]
        public void Expired_lease_restores_the_remainder_and_a_late_replace_says_so()
        {
            var s = NewSession();
            var t = store.Take(s.Id, Text.IndexOf("Three"), leaseSeconds: 30);

            now = now.AddSeconds(29);
            Assert.Empty(store.ExpireLeases());

            now = now.AddSeconds(2);
            var expired = Assert.Single(store.ExpireLeases());
            Assert.Equal("timeout", expired.Reason);
            Assert.Equal(SessionState.Active, s.State);
            Assert.StartsWith("Three tests", s.Remainder);

            var e = Assert.Throws<ApiException>(() => store.Replace(s.Id, t.Handoff.Id, "late"));
            Assert.Equal("handoff_closed", e.Code);
            Assert.Equal(Text, s.Text);
        }

        [Fact]
        public void Closing_handoffs_because_the_list_was_cleared_removes_the_session()
        {
            var s = NewSession();
            var t = store.Take(s.Id, 0);
            var closed = Assert.Single(store.CloseHandoffs("cleared"));
            Assert.Equal("cleared", closed.Reason);
            var e = Assert.Throws<ApiException>(() => store.Replace(s.Id, t.Handoff.Id, "x"));
            Assert.Equal("handoff_closed", e.Code);
            Assert.Contains("cleared", e.Message);
        }

        [Fact]
        public void Progress_only_moves_forward_and_snaps_to_words_and_is_ignored_while_taken()
        {
            var s = NewSession();
            store.ReportProgress(s.Id, 20);
            int first = s.ReadChars;
            Assert.True(first > 0 && first <= 20);
            store.ReportProgress(s.Id, 3);
            Assert.Equal(first, s.ReadChars);
            store.ReportProgress(s.Id, 3, allowBackwards: true);
            Assert.True(s.ReadChars <= 3);

            store.Take(s.Id, 16);
            int taken = s.ReadChars;
            store.ReportProgress(s.Id, 40);
            Assert.Equal(taken, s.ReadChars);
        }

        [Fact]
        public void Unknown_session_is_404()
        {
            var e = Assert.Throws<ApiException>(() => store.Get("s_missing"));
            Assert.Equal(404, e.HttpStatus);
        }
    }
}
