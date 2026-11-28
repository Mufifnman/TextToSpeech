namespace TextToSpeech.Api.Arbitration
{
    /// <summary>
    /// Sits in front of the reading host. Requests without an agent pass straight through (the window, hotkeys and
    /// plain API clients behave as before); requests with an agent are checked against the <see cref="ReadPolicy"/>
    /// and take turns through <see cref="ReadArbiter"/>. Call on the UI thread.
    /// </summary>
    /// <summary>What travels with waiting text through the arbiter: the pause points of the request.</summary>
    internal sealed record SpeakExtras(IReadOnlyList<PauseRequest>? Pauses, IReadOnlyList<string>? Questions, bool? PlayAll);

    public sealed class ArbitratedHost : IReadingHost, IArbiterHost
    {
        private readonly IReadingHost inner;
        private readonly IEnvironmentProbe probe;
        private readonly Func<ReadPolicy> policy;
        private readonly ReadArbiter arbiter;
        private DateTimeOffset? mutedUntil;
        private SpeakResponse? lastSpoken;
        private string? lastFocusSeen;
        private string? frontedFor;

        public ArbitratedHost(IReadingHost inner, IEnvironmentProbe probe, Func<ReadPolicy> policy, Func<bool>? isBusy = null)
        {
            this.inner = inner;
            this.probe = probe;
            this.policy = policy;
            arbiter = new ReadArbiter(isBusy ?? (() => DefaultBusy()), (a, text, mode) => SpeakFor(a, text, mode, null), () => probe.Now,
                () => policy().PendingExpiryMinutes > 0 ? TimeSpan.FromMinutes(policy().PendingExpiryMinutes) : null, SpeakFor);
        }

        public ReadArbiter Arbiter => arbiter;

        private bool DefaultBusy()
        {
            string state = inner.GetStatusAsync().GetAwaiter().GetResult().State;
            return state == "speaking" || state == "paused" || state == "paused_at_pause";
        }

        private void SpeakFor(AgentRecord agent, string text, string mode, object? extra)
        {
            // Waiting text can come due long after it was accepted (headphones unplugged, quiet hours began, muted):
            // the rules are checked again at the moment of speaking.
            if (!PolicyEvaluator.Evaluate(policy(), Context()).MayRead)
            {
                lastSpoken = new SpeakResponse("", 0, "idle", 0, true);
                return;
            }
            var extras = extra as SpeakExtras;
            lastSpoken = inner.SpeakAsync(new SpeakRequest(text, agent.Label, mode, null, null, null, null, null,
                Pauses: extras?.Pauses, Questions: extras?.Questions, PlayAll: extras?.PlayAll)).GetAwaiter().GetResult();
            if (frontedFor != agent.Id)
            {
                frontedFor = agent.Id;
                if (policy().BringToFront && agent.Pid is int pid)
                {
                    probe.BringToFront(pid);
                }
            }
        }

        /// <summary>Call when the reading list has run dry (the host's "finished" event).</summary>
        public void OnReadingFinished()
        {
            frontedFor = null;
            arbiter.OnReadingFinished();
        }

        /// <summary>Call every second or so: notices when the user switches to another agent's window.</summary>
        public void Poll()
        {
            var fg = probe.ForegroundWindow();
            if (fg == null)
            {
                return;
            }
            string? id = FocusMatcher.Match(arbiter.Agents, fg.Value.Pid, fg.Value.Title, probe.IsAncestor);
            if (id != null && id != lastFocusSeen)
            {
                arbiter.NotifyFocus(id, policy().FocusMode);
            }
            lastFocusSeen = id; // a window that is not an agent resets this, so coming back counts as a switch
        }

        // ---------- policy ----------

        private PolicyContext Context() =>
            new(probe.HeadphonesConnected(), probe.Now.ToLocalTime().TimeOfDay, probe.Now, mutedUntil);

        public Task<PolicyResponse> GetPolicyAsync() => Task.FromResult(PolicySnapshot());

        private PolicyResponse PolicySnapshot()
        {
            var ctx = Context();
            var d = PolicyEvaluator.Evaluate(policy(), ctx);
            return new PolicyResponse(d.MayRead, d.Reasons, policy(), ctx.HeadphonesConnected,
                mutedUntil > probe.Now ? mutedUntil!.Value.ToString("o") : null);
        }

        public Task<PolicyResponse> MuteAsync(int minutes)
        {
            if (minutes < 1 || minutes > 720)
            {
                throw ApiException.BadRequest("minutes must be between 1 and 720.");
            }
            var until = probe.Now.AddMinutes(minutes);
            if (mutedUntil == null || until > mutedUntil) mutedUntil = until; // never shortens an existing mute
            return Task.FromResult(PolicySnapshot());
        }

        // ---------- agents ----------

        public Task<ArbiterStatus> RegisterAsync(AgentRef agent)
        {
            arbiter.Upsert(agent);
            return Task.FromResult(Snapshot());
        }

        public Task<ArbiterStatus> UnregisterAsync(string agentId)
        {
            arbiter.Remove(agentId);
            return Task.FromResult(Snapshot());
        }

        public Task<ArbiterStatus> FocusAsync(AgentRef agent)
        {
            arbiter.Upsert(agent);
            lastFocusSeen = agent.Id;
            arbiter.NotifyFocus(agent.Id, policy().FocusMode);
            return Task.FromResult(Snapshot());
        }

        public Task<ArbiterStatus> PromptAsync(AgentRef agent)
        {
            arbiter.Upsert(agent);
            if (policy().ExpireOnPrompt)
            {
                arbiter.ClearPending(agent.Id); // the user is typing here now: whatever was waiting is old
            }
            lastFocusSeen = agent.Id;
            arbiter.NotifyFocus(agent.Id, policy().FocusMode);
            return Task.FromResult(Snapshot());
        }

        public Task<ArbiterStatus> GetArbiterAsync() => Task.FromResult(Snapshot());

        private ArbiterStatus Snapshot()
        {
            var waiting = arbiter.WaitingOrder().Select(a => a.Id).ToList();
            var agents = arbiter.Agents.Select(a => new AgentDto(a.Id, a.Label, a.Priority, a.Id == arbiter.HolderId,
                a.Id == arbiter.FocusedAgentId, a.Pending.Count, waiting.IndexOf(a.Id) + 1)).ToList();
            return new ArbiterStatus(agents, arbiter.HolderId, arbiter.FocusedAgentId, PolicySnapshot());
        }

        // ---------- speaking ----------

        public Task<SpeakResponse> SpeakAsync(SpeakRequest request)
        {
            if (request.Agent == null)
            {
                return inner.SpeakAsync(request);
            }
            var ctx = Context();
            var decision = PolicyEvaluator.Evaluate(policy(), ctx);
            if (!decision.MayRead)
            {
                arbiter.Upsert(request.Agent);
                return Task.FromResult(new SpeakResponse("", 0, "idle", 0, true, "denied", string.Join(" ", decision.Reasons)));
            }
            lastSpoken = null;
            var extras = request.Pauses != null || request.Questions != null || request.PlayAll != null
                ? new SpeakExtras(request.Pauses, request.Questions, request.PlayAll)
                : null;
            var result = arbiter.Submit(request.Agent, request.Text!, request.Mode ?? "queue", policy().MaxPendingPerAgent, extras);
            switch (result.Outcome)
            {
                case SubmitOutcome.Speaking:
                    var spoken = lastSpoken ?? new SpeakResponse("", 0, "idle", 0, false);
                    return Task.FromResult(spoken with { Decision = spoken.Skipped ? "skipped" : "speaking", Reason = result.Reason });
                case SubmitOutcome.Skipped:
                    return Task.FromResult(new SpeakResponse("", 0, "speaking", 0, true, "skipped", result.Reason, result.HeldBy));
                default:
                    return Task.FromResult(new SpeakResponse("", 0, "waiting", result.Position, false, "waiting", result.Reason, result.HeldBy));
            }
        }

        // ---------- plain delegation ----------

        public Task<StatusDto> GetStatusAsync() => inner.GetStatusAsync();

        public Task<StatusDto> ControlAsync(string action)
        {
            if (action == "clear")
            {
                foreach (var a in arbiter.Agents) arbiter.ClearPending(a.Id);
            }
            return inner.ControlAsync(action);
        }

        public Task<StatusDto> JumpAsync(JumpRequest request) => inner.JumpAsync(request);
        public Task<StatusDto> AnsweredAsync(string pauseId, string? sessionId) => inner.AnsweredAsync(pauseId, sessionId);
        public Task<IReadOnlyList<VoiceDto>> GetVoicesAsync() => inner.GetVoicesAsync();
        public Task<StatusDto> SetVoiceAsync(string voiceId) => inner.SetVoiceAsync(voiceId);
        public Task<StatusDto> SetRateAsync(int rate) => inner.SetRateAsync(rate);
        public Task<StatusDto> SetVolumeAsync(int volume) => inner.SetVolumeAsync(volume);
        public Task<PeekResponse> PeekAsync(string sessionId) => inner.PeekAsync(sessionId);
        public Task<TakeResponse> TakeAsync(string sessionId, TakeRequest request) => inner.TakeAsync(sessionId, request);
        public Task<StatusDto> ReplaceAsync(string sessionId, ReplaceRequest request) => inner.ReplaceAsync(sessionId, request);
        public Task<StatusDto> ReleaseAsync(string sessionId, ReleaseRequest request) => inner.ReleaseAsync(sessionId, request);
        public Task<StatusDto> PatchAsync(string sessionId, PatchRequest request) => inner.PatchAsync(sessionId, request);
    }
}
