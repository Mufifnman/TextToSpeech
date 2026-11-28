using TextToSpeech.Api;
using TextToSpeech.Api.Arbitration;
using Xunit;

namespace TextToSpeech.Api.Tests
{
    public class ReadPolicyTests
    {
        private static PolicyContext Ctx(bool? phones = true, string time = "12:00", DateTimeOffset? mutedUntil = null) =>
            new(phones, TimeSpan.Parse(time), DateTimeOffset.UnixEpoch, mutedUntil);

        [Fact]
        public void Default_policy_allows_reading()
        {
            Assert.True(PolicyEvaluator.Evaluate(new ReadPolicy(), Ctx()).MayRead);
        }

        [Fact]
        public void Switched_off_denies_with_a_reason()
        {
            var d = PolicyEvaluator.Evaluate(new ReadPolicy { Enabled = false }, Ctx());
            Assert.False(d.MayRead);
            Assert.Single(d.Reasons);
        }

        [Theory]
        [InlineData(true, true)]
        [InlineData(false, false)]
        [InlineData(null, false)]   // unknown output counts as "not headphones"
        public void Headphone_rule(bool? phones, bool expected)
        {
            Assert.Equal(expected, PolicyEvaluator.Evaluate(new ReadPolicy { RequireHeadphones = true }, Ctx(phones)).MayRead);
        }

        [Fact]
        public void Headphones_are_ignored_when_not_required()
        {
            Assert.True(PolicyEvaluator.Evaluate(new ReadPolicy(), Ctx(false)).MayRead);
        }

        [Theory]
        [InlineData("23:00", false)]
        [InlineData("03:00", false)]
        [InlineData("07:00", true)]   // end is exclusive
        [InlineData("12:00", true)]
        [InlineData("22:00", false)]  // start is inclusive
        public void Quiet_hours_across_midnight(string time, bool expected)
        {
            var p = new ReadPolicy { QuietFrom = "22:00", QuietTo = "07:00" };
            Assert.Equal(expected, PolicyEvaluator.Evaluate(p, Ctx(time: time)).MayRead);
        }

        [Fact]
        public void Quiet_hours_within_a_day()
        {
            var p = new ReadPolicy { QuietFrom = "09:00", QuietTo = "17:00" };
            Assert.False(PolicyEvaluator.Evaluate(p, Ctx(time: "10:30")).MayRead);
            Assert.True(PolicyEvaluator.Evaluate(p, Ctx(time: "18:00")).MayRead);
        }

        [Theory]
        [InlineData(null, "07:00")]
        [InlineData("22:00", null)]
        [InlineData("nonsense", "07:00")]
        [InlineData("25:00", "07:00")]
        [InlineData("08:00", "08:00")]
        public void Incomplete_or_invalid_quiet_hours_are_ignored(string? from, string? to)
        {
            Assert.True(PolicyEvaluator.Evaluate(new ReadPolicy { QuietFrom = from, QuietTo = to }, Ctx(time: "08:00")).MayRead);
        }

        [Fact]
        public void Mute_denies_until_it_runs_out()
        {
            Assert.False(PolicyEvaluator.Evaluate(new ReadPolicy(), Ctx(mutedUntil: DateTimeOffset.UnixEpoch.AddMinutes(5))).MayRead);
            Assert.True(PolicyEvaluator.Evaluate(new ReadPolicy(), Ctx(mutedUntil: DateTimeOffset.UnixEpoch.AddMinutes(-1))).MayRead);
        }

        [Fact]
        public void Every_failing_rule_is_listed()
        {
            var p = new ReadPolicy { Enabled = false, RequireHeadphones = true };
            Assert.Equal(2, PolicyEvaluator.Evaluate(p, Ctx(false)).Reasons.Count);
        }
    }

    public class ReadArbiterTests
    {
        private readonly List<string> spoken = new();
        private bool busy;
        private DateTimeOffset now = DateTimeOffset.UnixEpoch;
        private readonly ReadArbiter arb;

        public ReadArbiterTests()
        {
            arb = new ReadArbiter(() => busy, (a, text, mode) => { spoken.Add($"{a.Id}:{mode}:{text}"); busy = true; }, () => now);
        }

        private static AgentRef A(string id, int? priority = null, int? pid = null, string? hint = null) => new(id, id.ToUpperInvariant(), pid, hint, priority);

        [Fact]
        public void First_agent_speaks_at_once_and_holds_the_turn()
        {
            var r = arb.Submit(A("a"), "hello", "queue", 5);
            Assert.Equal(SubmitOutcome.Speaking, r.Outcome);
            Assert.Equal("a", arb.HolderId);
            Assert.Equal(new[] { "a:queue:hello" }, spoken);
        }

        [Fact]
        public void Holder_can_keep_adding_to_its_own_turn()
        {
            arb.Submit(A("a"), "one", "queue", 5);
            Assert.Equal(SubmitOutcome.Speaking, arb.Submit(A("a"), "two", "queue", 5).Outcome);
            Assert.Equal(2, spoken.Count);
        }

        [Fact]
        public void Second_agent_waits_and_never_overlaps()
        {
            arb.Submit(A("a"), "one", "queue", 5);
            var r = arb.Submit(A("b"), "two", "queue", 5);
            Assert.Equal(SubmitOutcome.Waiting, r.Outcome);
            Assert.Equal("A", r.HeldBy);
            Assert.Equal(1, r.Position);
            Assert.Single(spoken);
        }

        [Fact]
        public void If_idle_is_skipped_while_someone_else_reads()
        {
            arb.Submit(A("a"), "one", "queue", 5);
            Assert.Equal(SubmitOutcome.Skipped, arb.Submit(A("b"), "two", "if-idle", 5).Outcome);
            Assert.Empty(arb.WaitingOrder());
        }

        [Fact]
        public void Waiting_text_is_read_when_the_turn_ends()
        {
            arb.Submit(A("a"), "one", "queue", 5);
            arb.Submit(A("b"), "two", "queue", 5);
            arb.Submit(A("b"), "three", "queue", 5);
            Assert.Equal("b", arb.OnReadingFinished());
            Assert.Equal(new[] { "a:queue:one", "b:queue:two", "b:queue:three" }, spoken);
            Assert.Equal("b", arb.HolderId);
        }

        [Fact]
        public void Turn_ends_empty_when_nobody_waits()
        {
            arb.Submit(A("a"), "one", "queue", 5);
            Assert.Null(arb.OnReadingFinished());
            Assert.Null(arb.HolderId);
        }

        [Fact]
        public void Order_is_focus_then_priority_then_longest_wait()
        {
            arb.Submit(A("a"), "a1", "queue", 5);
            arb.Submit(A("b"), "b1", "queue", 5);
            arb.Submit(A("c", priority: 5), "c1", "queue", 5);
            arb.Submit(A("d"), "d1", "queue", 5);
            Assert.Equal(new[] { "c", "b", "d" }, arb.WaitingOrder().Select(x => x.Id));

            arb.NotifyFocus("d", "next");
            Assert.Equal(new[] { "d", "c", "b" }, arb.WaitingOrder().Select(x => x.Id));
            Assert.Equal("d", arb.OnReadingFinished());
        }

        [Fact]
        public void Focus_next_does_not_interrupt()
        {
            arb.Submit(A("a"), "a1", "queue", 5);
            arb.Submit(A("b"), "b1", "queue", 5);
            arb.NotifyFocus("b", "next");
            Assert.Single(spoken);
            Assert.Equal("a", arb.HolderId);
        }

        [Fact]
        public void Focus_now_interrupts_with_the_first_text_only()
        {
            arb.Submit(A("a"), "a1", "queue", 5);
            arb.Submit(A("b"), "b1", "queue", 5);
            arb.Submit(A("b"), "b2", "queue", 5);
            arb.NotifyFocus("b", "now");
            Assert.Equal(new[] { "a:queue:a1", "b:interrupt:b1", "b:queue:b2" }, spoken);
            Assert.Equal("b", arb.HolderId);
        }

        [Fact]
        public void Focus_off_changes_nothing()
        {
            arb.Submit(A("a"), "a1", "queue", 5);
            arb.Submit(A("b"), "b1", "queue", 5);
            arb.NotifyFocus("b", "off");
            Assert.Equal("a", arb.HolderId);
            Assert.Single(spoken);
        }

        [Fact]
        public void Focus_on_a_waiting_agent_reads_at_once_when_nothing_is_speaking()
        {
            arb.Submit(A("a"), "a1", "queue", 5);
            arb.Submit(A("b"), "b1", "queue", 5);
            busy = false; // a's reading ended without a finished event
            arb.NotifyFocus("b", "next");
            Assert.Equal("b", arb.HolderId);
            Assert.Equal("b:queue:b1", spoken.Last());
        }

        [Fact]
        public void A_stale_holder_is_replaced_when_nothing_is_speaking()
        {
            arb.Submit(A("a"), "a1", "queue", 5);
            busy = false;
            Assert.Equal(SubmitOutcome.Speaking, arb.Submit(A("b"), "b1", "queue", 5).Outcome);
            Assert.Equal("b", arb.HolderId);
        }

        [Fact]
        public void Waiting_text_per_agent_is_capped_dropping_the_oldest()
        {
            arb.Submit(A("a"), "a1", "queue", 2);
            for (int i = 1; i <= 4; i++) arb.Submit(A("b"), "b" + i, "queue", 2);
            arb.OnReadingFinished();
            Assert.Equal(new[] { "b:queue:b3", "b:queue:b4" }, spoken.Skip(1));
        }

        [Fact]
        public void Removing_the_holder_hands_over_the_turn()
        {
            arb.Submit(A("a"), "a1", "queue", 5);
            arb.Submit(A("b"), "b1", "queue", 5);
            busy = false;
            arb.Remove("a");
            Assert.Equal("b", arb.HolderId);
        }

        [Fact]
        public void Clearing_drops_waiting_text()
        {
            arb.Submit(A("a"), "a1", "queue", 5);
            arb.Submit(A("b"), "b1", "queue", 5);
            arb.ClearPending("b");
            Assert.Null(arb.OnReadingFinished());
        }

        [Fact]
        public void Quiet_agents_are_forgotten_after_the_ttl_but_not_ones_with_waiting_text()
        {
            arb.Submit(A("a"), "a1", "queue", 5);
            arb.Upsert(A("idle"));
            arb.Submit(A("b"), "b1", "queue", 5);
            now += ReadArbiter.AgentTtl + TimeSpan.FromMinutes(1);
            arb.Prune();
            Assert.DoesNotContain(arb.Agents, x => x.Id == "idle");
            Assert.Contains(arb.Agents, x => x.Id == "b");
            Assert.Contains(arb.Agents, x => x.Id == "a");
        }

        [Fact]
        public void Waiting_text_expires()
        {
            arb.Submit(A("a"), "a1", "queue", 5);
            arb.Submit(A("b"), "b1", "queue", 5);
            now += TimeSpan.FromHours(3);
            Assert.Single(arb.WaitingOrder()); // the default is 4 hours
            now += TimeSpan.FromHours(2);
            Assert.Empty(arb.WaitingOrder());
            Assert.Null(arb.OnReadingFinished());
        }

        [Fact]
        public void Auto_expire_can_be_turned_off_or_changed()
        {
            TimeSpan? ttl = null;
            var a2 = new ReadArbiter(() => true, (_, _, _) => { }, () => now, () => ttl);
            a2.Submit(A("a"), "a1", "queue", 5);
            a2.Submit(A("b"), "b1", "queue", 5);
            now += TimeSpan.FromDays(30);
            Assert.Single(a2.WaitingOrder()); // never expires
            ttl = TimeSpan.FromMinutes(10);
            Assert.Empty(a2.WaitingOrder());
        }

        [Fact]
        public void Agent_needs_an_id()
        {
            Assert.Throws<ApiException>(() => arb.Upsert(new AgentRef(" ", null, null, null, null)));
        }
    }

    public class FocusMatcherTests
    {
        private static AgentRecord Agent(string id, int? pid, string? hint)
        {
            var arb = new ReadArbiter(() => false, (_, _, _) => { });
            return arb.Upsert(new AgentRef(id, null, pid, hint, null));
        }

        // Process 100 is a terminal that hosts agents 101 and 102; 200 is another window with agent 201.
        private static bool IsAncestor(int ancestor, int pid) => ancestor == pid || (ancestor == 100 && (pid == 101 || pid == 102)) || (ancestor == 200 && pid == 201);

        [Fact]
        public void One_agent_per_process_matches_by_process()
        {
            var agents = new[] { Agent("x", 201, null), Agent("y", 101, null) };
            Assert.Equal("x", FocusMatcher.Match(agents, 200, "whatever", IsAncestor));
        }

        [Fact]
        public void Shared_process_needs_the_title_hint()
        {
            var agents = new[] { Agent("p", 101, "projA"), Agent("q", 102, "projB") };
            Assert.Equal("q", FocusMatcher.Match(agents, 100, "claude - projB", IsAncestor));
            Assert.Null(FocusMatcher.Match(agents, 100, "claude - other", IsAncestor));
        }

        [Fact]
        public void Shared_process_without_hints_is_ambiguous()
        {
            var agents = new[] { Agent("p", 101, null), Agent("q", 102, null) };
            Assert.Null(FocusMatcher.Match(agents, 100, "claude", IsAncestor));
        }

        [Fact]
        public void Unrelated_window_matches_nothing()
        {
            var agents = new[] { Agent("p", 101, "projA") };
            Assert.Null(FocusMatcher.Match(agents, 999, "projA in a browser", IsAncestor));
            Assert.Null(FocusMatcher.Match(agents, null, null, IsAncestor));
        }

        [Fact]
        public void Title_alone_works_when_the_client_sent_no_pid()
        {
            var agents = new[] { Agent("p", null, "projA") };
            Assert.Equal("p", FocusMatcher.Match(agents, 555, "terminal - PROJA", IsAncestor));
        }
    }

    internal sealed class FakeProbe : IEnvironmentProbe
    {
        public bool? Headphones { get; set; } = true;
        public (int Pid, string Title)? Foreground { get; set; }
        public DateTimeOffset Clock { get; set; } = new DateTimeOffset(2026, 1, 1, 12, 0, 0, TimeSpan.Zero);
        public List<int> Fronted { get; } = new();
        public bool? HeadphonesConnected() => Headphones;
        public (int Pid, string Title)? ForegroundWindow() => Foreground;
        public bool IsAncestor(int ancestorPid, int pid) => ancestorPid == pid;
        public bool BringToFront(int pid) { Fronted.Add(pid); return true; }
        public DateTimeOffset Now => Clock;
    }

    public class ArbitratedHostTests
    {
        private readonly FakeHost inner = new();
        private readonly FakeProbe probe = new();
        private ReadPolicy policy = new();
        private bool busy;
        private readonly ArbitratedHost host;

        public ArbitratedHostTests() => host = new ArbitratedHost(inner, probe, () => policy, () => busy);

        private static SpeakRequest Req(string agent, string text = "hi", string? mode = null, int? pid = null) =>
            new(text, null, mode, null, null, null, null, null, new AgentRef(agent, agent.ToUpperInvariant(), pid, null, null));

        [Fact]
        public async Task Requests_without_an_agent_pass_through_untouched()
        {
            var r = await host.SpeakAsync(new SpeakRequest("hi", "x", "queue", null, null, null, null, null));
            Assert.Null(r.Decision);
            Assert.Equal("speak", inner.Calls.Single());
        }

        [Fact]
        public async Task Denied_by_policy_never_reaches_the_reader()
        {
            policy = new ReadPolicy { RequireHeadphones = true };
            probe.Headphones = false;
            var r = await host.SpeakAsync(Req("a"));
            Assert.Equal("denied", r.Decision);
            Assert.True(r.Skipped);
            Assert.Empty(inner.Calls);
        }

        [Fact]
        public async Task Agent_speaks_through_the_reader_with_its_label_as_source()
        {
            var r = await host.SpeakAsync(Req("a"));
            Assert.Equal("speaking", r.Decision);
            Assert.Equal("A", inner.LastSpeak!.Source);
            Assert.Equal("a", inner.LastSpeak.Agent!.Id);   // passed on so a prewarm hint from this agent can be matched
            Assert.Equal("A", inner.LastSpeak.Agent.Label);
        }

        [Fact]
        public async Task Second_agent_waits_then_speaks_after_finish()
        {
            await host.SpeakAsync(Req("a", "one"));
            busy = true;
            var w = await host.SpeakAsync(Req("b", "two"));
            Assert.Equal("waiting", w.Decision);
            Assert.Equal("A", w.HeldBy);
            Assert.Single(inner.Calls);

            host.OnReadingFinished();
            Assert.Equal(2, inner.Calls.Count);
            Assert.Equal("two", inner.LastSpeak!.Text);
        }

        [Fact]
        public async Task Mute_denies_and_can_only_be_extended()
        {
            await host.MuteAsync(30);
            Assert.Equal("denied", (await host.SpeakAsync(Req("a"))).Decision);
            await host.MuteAsync(5);
            var p = await host.GetPolicyAsync();
            Assert.False(p.MayRead);
            Assert.Equal(probe.Clock.AddMinutes(30).ToString("o"), p.MutedUntil);
            await Assert.ThrowsAsync<ApiException>(() => host.MuteAsync(0));
        }

        [Fact]
        public async Task Waiting_text_is_dropped_if_the_rules_forbid_reading_when_its_turn_comes()
        {
            await host.SpeakAsync(Req("a", "one"));
            busy = true;
            await host.SpeakAsync(Req("b", "two"));
            await host.MuteAsync(10);
            host.OnReadingFinished();
            Assert.Single(inner.Calls);
        }

        [Fact]
        public async Task Prompt_in_a_window_drops_its_waiting_text_and_focuses_it()
        {
            await host.SpeakAsync(Req("a", "one"));
            busy = true;
            await host.SpeakAsync(Req("b", "two"));
            var s = await host.PromptAsync(new AgentRef("b", null, null, null, null));
            Assert.Equal("b", s.FocusedId);
            Assert.Equal(0, s.Agents.Single(x => x.Id == "b").Waiting);
            host.OnReadingFinished();
            Assert.Single(inner.Calls);
        }

        [Fact]
        public async Task Prompt_keeps_waiting_text_when_the_setting_is_off()
        {
            policy = new ReadPolicy { ExpireOnPrompt = false };
            await host.SpeakAsync(Req("a", "one"));
            busy = true;
            await host.SpeakAsync(Req("b", "two"));
            await host.PromptAsync(new AgentRef("b", null, null, null, null));
            Assert.Equal(1, (await host.GetArbiterAsync()).Agents.Single(x => x.Id == "b").Waiting);
        }

        [Fact]
        public async Task Mute_ends()
        {
            await host.MuteAsync(5);
            probe.Clock = probe.Clock.AddMinutes(6);
            Assert.True((await host.GetPolicyAsync()).MayRead);
        }

        [Fact]
        public async Task Poll_promotes_the_agent_whose_window_gets_focus()
        {
            await host.SpeakAsync(Req("a", "one", pid: 10));
            busy = true;
            await host.SpeakAsync(Req("b", "two", pid: 20));
            probe.Foreground = (20, "terminal");
            host.Poll();
            Assert.Equal("b", host.Arbiter.FocusedAgentId);
            Assert.Equal("b", host.Arbiter.WaitingOrder()[0].Id);
        }

        [Fact]
        public async Task Focus_mode_now_interrupts_on_poll()
        {
            policy = new ReadPolicy { FocusMode = "now" };
            await host.SpeakAsync(Req("a", "one", pid: 10));
            busy = true;
            await host.SpeakAsync(Req("b", "two", pid: 20));
            probe.Foreground = (20, "terminal");
            host.Poll();
            Assert.Equal("interrupt", inner.LastSpeak!.Mode);
            Assert.Equal("two", inner.LastSpeak.Text);
        }

        [Fact]
        public async Task Bring_to_front_only_when_enabled_and_once_per_turn()
        {
            await host.SpeakAsync(Req("a", pid: 10));
            Assert.Empty(probe.Fronted);

            policy = new ReadPolicy { BringToFront = true };
            host.OnReadingFinished();
            await host.SpeakAsync(Req("a", pid: 10));
            await host.SpeakAsync(Req("a", "more", pid: 10));
            Assert.Equal(new[] { 10 }, probe.Fronted);
        }

        [Fact]
        public async Task Register_focus_and_unregister_round_trip()
        {
            var s = await host.RegisterAsync(new AgentRef("a", "Alpha", null, null, 3));
            Assert.Equal("Alpha", s.Agents.Single().Label);
            s = await host.FocusAsync(new AgentRef("a", null, null, null, null));
            Assert.Equal("a", s.FocusedId);
            s = await host.UnregisterAsync("a");
            Assert.Empty(s.Agents);
        }
    }

    public class ArbitrationRouterTests
    {
        private readonly FakeHost inner = new();
        private readonly ApiRouter router;

        public ArbitrationRouterTests()
        {
            var host = new ArbitratedHost(inner, new FakeProbe(), () => new ReadPolicy(), () => false);
            router = new ApiRouter(host, new ApiSecurity(Req.Token, Req.Port));
        }

        private Task<ApiResponse> Send(string method, string path, string? body = null) => router.HandleAsync(Req.Make(method, path, body));

        [Fact]
        public async Task Policy_is_readable()
        {
            var r = await Send("GET", "/v1/policy");
            Assert.Equal(200, r.Status);
            Assert.Contains("\"mayRead\":true", r.Json);
        }

        [Fact]
        public async Task Policy_cannot_be_changed_over_the_api_only_muted()
        {
            Assert.Equal(405, (await Send("PUT", "/v1/policy", "{}")).Status);
            Assert.Equal(200, (await Send("POST", "/v1/policy/mute", "{\"minutes\":10}")).Status);
            Assert.Equal(400, (await Send("POST", "/v1/policy/mute", "{\"minutes\":0}")).Status);
        }

        [Fact]
        public async Task Agents_register_focus_list_and_delete()
        {
            Assert.Equal(200, (await Send("POST", "/v1/agents", "{\"id\":\"s1\",\"label\":\"proj\",\"pid\":42}")).Status);
            Assert.Equal(200, (await Send("POST", "/v1/agents/focus", "{\"id\":\"s1\"}")).Status);
            Assert.Equal(200, (await Send("POST", "/v1/agents/prompt", "{\"id\":\"s1\"}")).Status);
            var list = await Send("GET", "/v1/agents");
            Assert.Contains("\"focusedId\":\"s1\"", list.Json);
            Assert.Equal(200, (await Send("DELETE", "/v1/agents/s1")).Status);
        }

        [Theory]
        [InlineData("{}")]
        [InlineData("{\"id\":\"s\",\"pid\":-1}")]
        [InlineData("{\"id\":\"s\",\"priority\":500}")]
        public async Task Bad_agents_are_400(string body)
        {
            Assert.Equal(400, (await Send("POST", "/v1/agents", body)).Status);
        }

        [Fact]
        public async Task Speak_with_an_agent_reports_the_decision()
        {
            var r = await Send("POST", "/v1/speak", "{\"text\":\"hi\",\"agent\":{\"id\":\"s1\"}}");
            Assert.Equal(202, r.Status);
            Assert.Contains("\"decision\":\"speaking\"", r.Json);
        }

        [Fact]
        public async Task Without_an_arbiter_the_routes_are_not_found()
        {
            var plain = new ApiRouter(inner, new ApiSecurity(Req.Token, Req.Port));
            Assert.Equal(404, (await plain.HandleAsync(Req.Make("GET", "/v1/agents"))).Status);
        }
    }
}
