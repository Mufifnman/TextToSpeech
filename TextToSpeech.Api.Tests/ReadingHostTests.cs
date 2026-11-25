using TextToSpeech.Api;
using Xunit;

namespace TextToSpeech.Api.Tests
{
    public class ReadingHostTests
    {
        private readonly HostRig rig = new();

        [Fact]
        public async Task Speak_on_an_idle_app_starts_reading_and_tags_the_text_with_the_session()
        {
            var r = await rig.Speak(HostRig.Three);
            Assert.StartsWith("s_", r.SessionId);
            Assert.Equal("speaking", r.State);
            Assert.Equal("Build finished.", rig.Engine.Calls[0].Text);
            Assert.All(rig.Document.Sentences, s => Assert.Equal(r.SessionId, s.Owner));
            Assert.Equal(new[] { "claude-code" }, rig.App.Popups);
        }

        [Fact]
        public async Task Popup_false_reads_quietly()
        {
            await rig.Speak("Quiet.", popup: false);
            Assert.Empty(rig.App.Popups);
        }

        [Fact]
        public async Task Filters_are_applied_only_when_asked()
        {
            await rig.Speak("Plain.");
            await rig.Speak("Filtered.", filters: true);
            Assert.Contains(rig.Document.Sentences, s => s.Text.StartsWith("[filtered]"));
            Assert.Equal(1, rig.Document.Sentences.Count(s => s.Text.StartsWith("[filtered]")));
        }

        [Fact]
        public async Task Queue_while_playing_waits_its_turn_and_reports_the_queue()
        {
            var first = await rig.Speak("First one.");
            var second = await rig.Speak("Second one.", source: "other");
            Assert.Single(rig.Engine.Calls);
            Assert.Equal(1, second.QueuePosition);
            var status = rig.Host.Snapshot();
            Assert.Equal(first.SessionId, status.ActiveSession!.Id);
            var queued = Assert.Single(status.Queued);
            Assert.Equal(second.SessionId, queued.Id);
            Assert.Equal("other", queued.Source);
        }

        [Fact]
        public async Task If_idle_is_skipped_when_something_is_playing_or_waiting()
        {
            await rig.Speak("Busy.");
            var r = await rig.Speak("Announcement.", mode: "if-idle");
            Assert.True(r.Skipped);
            Assert.Single(rig.Document.Sentences);
        }

        [Fact]
        public async Task If_idle_speaks_when_nothing_is_waiting()
        {
            var r = await rig.Speak("Announcement.", mode: "if-idle");
            Assert.False(r.Skipped);
            Assert.Equal("speaking", r.State);
        }

        [Fact]
        public async Task Interrupt_stops_the_current_sentence_and_speaks_now_keeping_the_rest()
        {
            await rig.Speak("Long one. Second part.");
            await rig.Speak("Urgent.", mode: "interrupt");
            Assert.True(rig.Engine.Calls[0].Cancelled);
            Assert.Equal("Urgent.", rig.Engine.Calls[1].Text);
            Assert.Equal(new[] { "Urgent.", "Long one.", "Second part." }, rig.Document.Sentences.Select(s => s.Text));
        }

        [Fact]
        public async Task Queue_onto_a_paused_list_stays_paused()
        {
            await rig.Speak("First one.");
            await rig.Host.ControlAsync("pause");
            var r = await rig.Speak("Later.");
            Assert.Equal("paused", r.State);
            Assert.Single(rig.Engine.Calls);
        }

        [Fact]
        public async Task Text_with_nothing_to_say_is_rejected()
        {
            var e = await Assert.ThrowsAsync<ApiException>(() => rig.Speak("   "));
            Assert.Equal("no_speakable_text", e.Code);
        }

        [Fact]
        public async Task Control_actions_drive_the_controller()
        {
            await rig.Speak("One. Two. Three.");
            Assert.Equal("paused", (await rig.Host.ControlAsync("pause")).State);
            Assert.Equal("speaking", (await rig.Host.ControlAsync("resume")).State);
            await rig.Host.ControlAsync("skip");
            Assert.Equal(1, rig.Document.Position.Sentence);
            await rig.Host.ControlAsync("back");
            Assert.Equal(0, rig.Document.Position.Sentence);
            var cleared = await rig.Host.ControlAsync("clear");
            Assert.Equal("idle", cleared.State);
            Assert.Equal(0, rig.Document.Count);
        }

        [Fact]
        public async Task Jump_uses_sentence_numbers_within_the_session()
        {
            await rig.Speak("Window text. More.", source: "a");  // session 1: sentences 0, 1
            var b = await rig.Speak("Mine one. Mine two.", source: "b");
            await rig.Host.JumpAsync(new JumpRequest(b.SessionId, 1, 0));
            Assert.Equal("Mine two.", rig.Document.CurrentSentence!.Text);
            await Assert.ThrowsAsync<ApiException>(() => rig.Host.JumpAsync(new JumpRequest(b.SessionId, 5, 0)));
        }

        [Fact]
        public async Task Voice_rate_and_volume_go_through_the_app()
        {
            await rig.Host.SetRateAsync(7);
            await rig.Host.SetVolumeAsync(90);
            Assert.Equal(7, rig.App.Rate);
            Assert.Equal(90, rig.App.Volume);
            var e = await Assert.ThrowsAsync<ApiException>(() => rig.Host.SetVoiceAsync("nope"));
            Assert.Equal(404, e.HttpStatus);
            await rig.Host.SetVoiceAsync("v1");
        }

        // ---------- take / replace / release ----------

        private async Task<(string Id, TakeResponse Taken)> SpeakAndTakeAfterFirstSentence()
        {
            var s = await rig.Speak(HostRig.Three);
            rig.Finish(0);                       // "Build finished." done, "Three tests failed." starts
            Assert.Equal("Three tests failed.", rig.Engine.Calls[1].Text);
            var taken = await rig.Host.TakeAsync(s.SessionId, new TakeRequest(null, null, null));
            return (s.SessionId, taken);
        }

        [Fact]
        public async Task Take_stops_speech_returns_the_unread_text_and_clears_the_list_state()
        {
            var (id, t) = await SpeakAndTakeAfterFirstSentence();
            Assert.True(rig.Engine.Calls[1].Cancelled);
            Assert.Equal("Build finished. ", t.ReadText);
            Assert.Equal("Three tests failed. Parser broke.", t.Remainder);
            Assert.Equal(HostRig.Three, t.ReadText + t.Remainder);
            Assert.Equal(1, t.Position.Sentence);
            Assert.Equal("Build finished. ".Length, t.Position.CharOffset);
            Assert.StartsWith("h_", t.HandoffId);
            Assert.Equal(rig.Now.AddSeconds(120), t.LeaseExpiresAt);

            // remainder is out of the list; highlight cleared; state says handed off
            Assert.Equal(new[] { "Build finished." }, rig.Document.Sentences.Select(s => s.Text));
            Assert.True(rig.Document.IsAtEnd);
            Assert.Equal(-1, rig.Document.SpokenWord);
            Assert.Equal("handed_off", rig.Host.Snapshot().State);
            Assert.Contains(rig.Events, e => e.Type == "handoff-taken" && e.SessionId == id);
        }

        [Fact]
        public async Task Take_mid_sentence_starts_the_remainder_at_the_spoken_word()
        {
            var s = await rig.Speak("Build finished. Three tests failed today.");
            rig.Finish(0);
            rig.Engine.Word(1, 6);               // "tests"
            var t = await rig.Host.TakeAsync(s.SessionId, new TakeRequest(null, null, null));
            Assert.Equal("tests failed today.", t.Remainder);
            Assert.Equal("Build finished. Three ", t.ReadText);
            Assert.Equal(1, t.Position.Word);
        }

        [Fact]
        public async Task Take_from_sentence_backs_up_to_the_sentence_start()
        {
            var s = await rig.Speak("Build finished. Three tests failed today.");
            rig.Finish(0);
            rig.Engine.Word(1, 6);
            var t = await rig.Host.TakeAsync(s.SessionId, new TakeRequest("sentence", null, null));
            Assert.Equal("Three tests failed today.", t.Remainder);
        }

        [Fact]
        public async Task Replace_puts_the_new_text_next_and_reads_it()
        {
            var (id, t) = await SpeakAndTakeAfterFirstSentence();
            var status = await rig.Host.ReplaceAsync(id, new ReplaceRequest(t.HandoffId, "Two tests failed.", null, null, null));
            Assert.Equal("speaking", status.State);
            Assert.Equal("Two tests failed.", rig.Engine.Calls[2].Text);
            Assert.Equal("Build finished. Two tests failed.", rig.Document.OwnerText(id));
            Assert.Equal(2, rig.Host.Snapshot().ActiveSession!.Revision);
            Assert.Contains(rig.Events, e => e.Type == "handoff-returned");
        }

        [Fact]
        public async Task Replace_with_start_at_skips_the_front_of_the_new_text()
        {
            var (id, t) = await SpeakAndTakeAfterFirstSentence();
            await rig.Host.ReplaceAsync(id, new ReplaceRequest(t.HandoffId, "Skip this. Say this.", 11, null, null));
            Assert.Equal("Say this.", rig.Engine.Calls[2].Text);
        }

        [Fact]
        public async Task Replace_with_auto_resume_false_leaves_it_paused()
        {
            var (id, t) = await SpeakAndTakeAfterFirstSentence();
            var status = await rig.Host.ReplaceAsync(id, new ReplaceRequest(t.HandoffId, "Quiet.", null, null, false));
            Assert.Equal("paused", status.State);
            Assert.Equal(2, rig.Engine.Calls.Count);   // nothing new started
        }

        [Fact]
        public async Task Replace_while_other_text_plays_goes_after_the_sentence_being_spoken()
        {
            var a = await rig.Speak("Mine one. Mine two.", source: "a");
            rig.Finish(0);
            var taken = await rig.Host.TakeAsync(a.SessionId, new TakeRequest(null, null, null));  // takes "Mine two."
            var b = await rig.Speak("Other one. Other two.", source: "b");
            Assert.Equal("Other one.", rig.Engine.Calls.Last().Text);

            await rig.Host.ReplaceAsync(a.SessionId, new ReplaceRequest(taken.HandoffId, "Back again.", null, "front", null));
            Assert.Equal(new[] { "Mine one.", "Other one.", "Back again.", "Other two." }, rig.Document.Sentences.Select(s => s.Text));
            Assert.Equal("Other one.", rig.Document.CurrentSentence!.Text);   // still speaking the same sentence
        }

        [Fact]
        public async Task Replace_at_the_end_placement_queues_last()
        {
            var a = await rig.Speak("Mine one. Mine two.", source: "a");
            rig.Finish(0);
            var taken = await rig.Host.TakeAsync(a.SessionId, new TakeRequest(null, null, null));
            await rig.Speak("Other one. Other two.", source: "b");
            await rig.Host.ReplaceAsync(a.SessionId, new ReplaceRequest(taken.HandoffId, "Last.", null, "end", null));
            Assert.Equal("Last.", rig.Document.Sentences.Last().Text);
        }

        [Fact]
        public async Task Other_queued_text_keeps_playing_while_taken()
        {
            var a = await rig.Speak("Mine one. Mine two.", source: "a");
            await rig.Speak("Other one.", source: "b");
            rig.Finish(0);                          // mine one done, mine two speaking
            await rig.Host.TakeAsync(a.SessionId, new TakeRequest(null, null, null));
            Assert.Equal("Other one.", rig.Engine.Calls.Last().Text);
            Assert.Equal("speaking", rig.Host.Snapshot().State);
        }

        [Fact]
        public async Task Release_restores_the_original_remainder_and_keeps_reading()
        {
            var (id, t) = await SpeakAndTakeAfterFirstSentence();
            var status = await rig.Host.ReleaseAsync(id, new ReleaseRequest(t.HandoffId));
            Assert.Equal("speaking", status.State);
            Assert.Equal("Three tests failed.", rig.Engine.Calls[2].Text);
            Assert.Equal(HostRig.Three, rig.Document.OwnerText(id));
            Assert.Equal(1, rig.Host.Snapshot().ActiveSession!.Revision);
        }

        [Fact]
        public async Task Release_without_a_handoff_id_uses_the_open_one()
        {
            var (id, _) = await SpeakAndTakeAfterFirstSentence();
            await rig.Host.ReleaseAsync(id, new ReleaseRequest(null));
            Assert.Equal(HostRig.Three, rig.Document.OwnerText(id));
        }

        [Fact]
        public async Task Expired_lease_restores_the_remainder_paused_and_a_late_replace_is_a_conflict()
        {
            var (id, t) = await SpeakAndTakeAfterFirstSentence();
            rig.Now = rig.Now.AddSeconds(121);
            rig.Host.Tick();

            Assert.Equal("paused", rig.Host.Snapshot().State);
            Assert.Equal(HostRig.Three, rig.Document.OwnerText(id));
            Assert.Equal(2, rig.Engine.Calls.Count);             // nothing started talking
            Assert.Contains(rig.Events, e => e.Type == "handoff-expired" && e.Detail == "timeout");

            var e = await Assert.ThrowsAsync<ApiException>(() =>
                rig.Host.ReplaceAsync(id, new ReplaceRequest(t.HandoffId, "late", null, null, null)));
            Assert.Equal("handoff_closed", e.Code);
        }

        [Fact]
        public async Task Any_api_call_also_expires_old_leases()
        {
            var (id, _) = await SpeakAndTakeAfterFirstSentence();
            rig.Now = rig.Now.AddSeconds(500);
            await rig.Host.GetStatusAsync();
            Assert.Equal(HostRig.Three, rig.Document.OwnerText(id));
        }

        [Fact]
        public async Task Clearing_while_taken_closes_the_handoff()
        {
            var (id, t) = await SpeakAndTakeAfterFirstSentence();
            rig.Reading.Clear();   // window button or hotkey
            var e = await Assert.ThrowsAsync<ApiException>(() =>
                rig.Host.ReplaceAsync(id, new ReplaceRequest(t.HandoffId, "x", null, null, null)));
            Assert.Equal("handoff_closed", e.Code);
            Assert.Contains("cleared", e.Message);
            Assert.Equal("idle", rig.Host.Snapshot().State);
        }

        [Fact]
        public async Task Taking_twice_conflicts_and_a_wrong_handoff_id_is_refused()
        {
            var (id, _) = await SpeakAndTakeAfterFirstSentence();
            var twice = await Assert.ThrowsAsync<ApiException>(() => rig.Host.TakeAsync(id, new TakeRequest(null, null, null)));
            Assert.Equal("already_taken", twice.Code);
            var wrong = await Assert.ThrowsAsync<ApiException>(() =>
                rig.Host.ReplaceAsync(id, new ReplaceRequest("h_wrong", "x", null, null, null)));
            Assert.Equal("bad_handoff", wrong.Code);
        }

        [Fact]
        public async Task Take_with_nothing_unread_conflicts_and_does_not_disturb_speech()
        {
            var s = await rig.Speak("Only one.");
            rig.Finish(0);   // finished; list at end
            var e = await Assert.ThrowsAsync<ApiException>(() => rig.Host.TakeAsync(s.SessionId, new TakeRequest(null, null, null)));
            Assert.Equal("nothing_to_take", e.Code);
        }

        [Fact]
        public async Task Take_with_a_stale_revision_does_not_stop_speech()
        {
            var s = await rig.Speak("One. Two.");
            var e = await Assert.ThrowsAsync<ApiException>(() => rig.Host.TakeAsync(s.SessionId, new TakeRequest(null, null, 9)));
            Assert.Equal("revision_mismatch", e.Code);
            Assert.False(rig.Engine.Calls[0].Cancelled);
            Assert.Equal("speaking", rig.Host.Snapshot().State);
        }

        [Fact]
        public async Task Current_means_the_session_being_read_and_unknown_ids_are_404()
        {
            var s = await rig.Speak("One. Two.");
            var peek = await rig.Host.PeekAsync("current");
            Assert.Equal(s.SessionId, peek.SessionId);
            Assert.Equal("One. Two.", peek.Remainder);
            var e = await Assert.ThrowsAsync<ApiException>(() => rig.Host.PeekAsync("s_nope"));
            Assert.Equal(404, e.HttpStatus);
        }

        [Fact]
        public async Task Peek_reports_read_and_unread_without_changing_anything()
        {
            var s = await rig.Speak(HostRig.Three);
            rig.Finish(0);
            var peek = await rig.Host.PeekAsync(s.SessionId);
            Assert.Equal("Build finished. ", peek.ReadText);
            Assert.Equal("Three tests failed. Parser broke.", peek.Remainder);
            Assert.Equal("speaking", rig.Host.Snapshot().State);
            Assert.Equal(3, rig.Document.Count);
        }

        // ---------- patch ----------

        [Fact]
        public async Task Patch_replaces_later_sentences_without_stopping_speech()
        {
            var s = await rig.Speak(HostRig.Three);
            int from = HostRig.Three.IndexOf("Parser");
            await rig.Host.PatchAsync(s.SessionId, new PatchRequest(1, from + 3, "The parser crashed. Fix pending."));
            Assert.False(rig.Engine.Calls[0].Cancelled);
            Assert.Equal("Build finished. Three tests failed. The parser crashed. Fix pending.", rig.Document.OwnerText(s.SessionId));
            Assert.Equal(2, rig.Host.Snapshot().ActiveSession!.Revision);
        }

        [Fact]
        public async Task Patch_of_the_sentence_being_spoken_or_earlier_is_too_late()
        {
            var s = await rig.Speak(HostRig.Three);
            var e = await Assert.ThrowsAsync<ApiException>(() => rig.Host.PatchAsync(s.SessionId, new PatchRequest(null, 0, "x")));
            Assert.Equal("too_late", e.Code);
            Assert.Equal(HostRig.Three, rig.Document.OwnerText(s.SessionId));
        }

        [Fact]
        public async Task Patch_with_a_stale_revision_conflicts()
        {
            var s = await rig.Speak(HostRig.Three);
            var e = await Assert.ThrowsAsync<ApiException>(() => rig.Host.PatchAsync(s.SessionId, new PatchRequest(5, 30, "x")));
            Assert.Equal("revision_mismatch", e.Code);
        }

        [Fact]
        public async Task Patch_while_taken_conflicts()
        {
            var (id, _) = await SpeakAndTakeAfterFirstSentence();
            var e = await Assert.ThrowsAsync<ApiException>(() => rig.Host.PatchAsync(id, new PatchRequest(null, 20, "x")));
            Assert.Equal("already_taken", e.Code);
        }

        // ---------- status ----------

        [Fact]
        public async Task Status_reports_the_active_session_position_and_progress()
        {
            var s = await rig.Speak(HostRig.Three);
            rig.Finish(0);
            var active = rig.Host.Snapshot().ActiveSession!;
            Assert.Equal(s.SessionId, active.Id);
            Assert.Equal("claude-code", active.Source);
            Assert.Equal(1, active.Position!.Sentence);
            Assert.Equal("Build finished. ".Length, active.Position.CharOffset);
            Assert.InRange(active.Progress, 0.2, 0.4);
            Assert.Equal(3, active.Sentences);
        }

        [Fact]
        public async Task Idle_status_has_no_session()
        {
            var status = rig.Host.Snapshot();
            Assert.Equal("idle", status.State);
            Assert.Null(status.ActiveSession);
            Assert.Empty(status.Queued);
            Assert.Equal(1, status.ApiVersion);
            await Task.CompletedTask;
        }

        [Fact]
        public async Task Finished_sessions_are_pruned_once_cleared_from_the_list()
        {
            await rig.Speak("One.");
            rig.Finish(0);
            rig.Reading.Clear();
            rig.Host.Tick();
            var e = await Assert.ThrowsAsync<ApiException>(() => rig.Host.PeekAsync("s_whatever"));
            Assert.Equal(404, e.HttpStatus);
        }

        [Fact]
        public async Task Open_handoffs_are_listed_and_release_all_restores_them()
        {
            var (id, _) = await SpeakAndTakeAfterFirstSentence();
            Assert.Equal(new[] { "claude-code" }, rig.Host.OpenHandoffSources);
            rig.Host.ReleaseAllHandoffs();
            Assert.Empty(rig.Host.OpenHandoffSources);
            Assert.Equal(HostRig.Three, rig.Document.OwnerText(id));
        }
    }
}
