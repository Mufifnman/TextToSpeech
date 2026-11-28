namespace TextToSpeech.Api.Arbitration
{
    /// <summary>How a client names itself. The id should be stable per window or conversation (Claude Code's session id).</summary>
    /// <param name="Pid">The client's process id (for Claude Code, its own process): lets the app find the window it runs in.</param>
    /// <param name="TitleHint">Text expected in that window's title, to tell windows of one terminal process apart.</param>
    /// <param name="Priority">Higher goes first among waiting agents. Default 0.</param>
    public sealed record AgentRef(string? Id, string? Label, int? Pid, string? TitleHint, int? Priority);

    public sealed class AgentRecord
    {
        internal AgentRecord(string id) { Id = id; Label = id; }
        public string Id { get; }
        public string Label { get; internal set; }
        public int? Pid { get; internal set; }
        public string? TitleHint { get; internal set; }
        public int Priority { get; internal set; }
        public DateTimeOffset LastSeen { get; internal set; }
        internal List<PendingRead> Pending { get; } = new();
    }

    public sealed record PendingRead(string Text, long Seq, DateTimeOffset Added, object? Extra = null);

    public enum SubmitOutcome { Speaking, Waiting, Skipped, Dropped }

    /// <param name="HeldBy">Label of the agent whose turn it is, when this one has to wait.</param>
    /// <param name="Position">1 = next in line (only meaningful when Waiting).</param>
    public sealed record SubmitResult(SubmitOutcome Outcome, string Reason, string? HeldBy, int Position);

    /// <summary>
    /// Decides whose turn it is to speak. One agent holds the turn at a time; other agents' text waits here (not in
    /// the reading list) until the holder's reading is finished, then the next agent is picked: the one whose window
    /// is focused first, then higher priority, then who has waited longest. Pure logic, not thread safe (call it on the
    /// UI thread). The reading itself is delegated through <c>speak</c>.
    /// </summary>
    public sealed class ReadArbiter
    {
        public static readonly TimeSpan AgentTtl = TimeSpan.FromHours(6);

        /// <summary>Default for how long waiting text is kept before it is dropped as stale.</summary>
        public static readonly TimeSpan DefaultPendingTtl = TimeSpan.FromHours(4);

        private readonly Func<DateTimeOffset> clock;
        private readonly Func<bool> isBusy;
        private readonly Action<AgentRecord, string, string> speak;
        private readonly Action<AgentRecord, string, string, object?>? speakWithExtra;
        private readonly Func<TimeSpan?> pendingTtl;
        private readonly Dictionary<string, AgentRecord> agents = new();
        private long seq;

        /// <param name="isBusy">True while anything is speaking or waiting in the reading list.</param>
        /// <param name="speak">Starts reading text for an agent: (agent, text, mode "queue" or "interrupt").</param>
        /// <param name="pendingTtl">How long waiting text is kept; null means for ever. Default 4 hours.</param>
        /// <param name="speakWithExtra">When given, used instead of <paramref name="speak"/> and also receives the opaque extra the request came with (pause points).</param>
        public ReadArbiter(Func<bool> isBusy, Action<AgentRecord, string, string> speak, Func<DateTimeOffset>? clock = null, Func<TimeSpan?>? pendingTtl = null,
            Action<AgentRecord, string, string, object?>? speakWithExtra = null)
        {
            this.speakWithExtra = speakWithExtra;
            this.pendingTtl = pendingTtl ?? (() => DefaultPendingTtl);
            this.isBusy = isBusy;
            this.speak = speak;
            this.clock = clock ?? (() => DateTimeOffset.UtcNow);
        }

        public string? HolderId { get; private set; }
        public string? FocusedAgentId { get; private set; }
        public IReadOnlyCollection<AgentRecord> Agents => agents.Values;

        public AgentRecord? Holder => HolderId != null && agents.TryGetValue(HolderId, out var h) ? h : null;

        /// <summary>Adds the agent or refreshes its details.</summary>
        public AgentRecord Upsert(AgentRef r)
        {
            if (string.IsNullOrWhiteSpace(r.Id))
            {
                throw ApiException.BadRequest("agent id is required.");
            }
            if (!agents.TryGetValue(r.Id, out var a))
            {
                a = new AgentRecord(r.Id);
                agents[r.Id] = a;
            }
            if (!string.IsNullOrWhiteSpace(r.Label)) a.Label = r.Label!.Trim();
            if (r.Pid.HasValue) a.Pid = r.Pid;
            if (!string.IsNullOrWhiteSpace(r.TitleHint)) a.TitleHint = r.TitleHint!.Trim();
            if (r.Priority.HasValue) a.Priority = r.Priority.Value;
            a.LastSeen = clock();
            return a;
        }

        public void Remove(string id)
        {
            agents.Remove(id);
            if (HolderId == id)
            {
                HolderId = null;
                if (!isBusy()) PickNext();
            }
            if (FocusedAgentId == id) FocusedAgentId = null;
        }

        /// <summary>An agent wants text read. The caller has already checked the policy.</summary>
        public SubmitResult Submit(AgentRef who, string text, string mode, int maxPending, object? extra = null)
        {
            var a = Upsert(who);
            Prune();
            if (HolderId != null && HolderId != a.Id && !isBusy())
            {
                HolderId = null; // the previous turn ended without anyone telling us
            }
            if (HolderId == null || HolderId == a.Id)
            {
                HolderId = a.Id;
                Speak(a, text, mode, extra);
                return new SubmitResult(SubmitOutcome.Speaking, "Your turn.", null, 0);
            }
            string heldBy = Holder?.Label ?? HolderId;
            if (mode == "if-idle")
            {
                return new SubmitResult(SubmitOutcome.Skipped, heldBy + " is reading.", heldBy, 0);
            }
            a.Pending.Add(new PendingRead(text, ++seq, clock(), extra));
            while (a.Pending.Count > Math.Max(1, maxPending))
            {
                a.Pending.RemoveAt(0);
            }
            return new SubmitResult(SubmitOutcome.Waiting, "Waiting for " + heldBy + ".", heldBy, QueuePosition(a));
        }

        /// <summary>The reading list ran dry: the turn is over, hand it to the next agent in line.</summary>
        public string? OnReadingFinished()
        {
            HolderId = null;
            return PickNext();
        }

        /// <summary>
        /// The user is now in this agent's window (null: in none of them). <paramref name="focusMode"/> is the policy's
        /// FocusMode: "now" interrupts the current reader, "next" only moves the agent to the front of the line.
        /// </summary>
        public void NotifyFocus(string? agentId, string focusMode)
        {
            FocusedAgentId = agentId != null && agents.ContainsKey(agentId) ? agentId : null;
            if (FocusedAgentId == null || focusMode == "off")
            {
                return;
            }
            var a = agents[FocusedAgentId];
            if (a.Pending.Count == 0 || HolderId == a.Id)
            {
                return;
            }
            if (focusMode == "now" && isBusy())
            {
                HolderId = a.Id;
                Flush(a, firstInterrupts: true);
            }
            else if (!isBusy())
            {
                HolderId = null;
                PickNext();
            }
        }

        /// <summary>Drops an agent's waiting text (the user cleared it).</summary>
        public void ClearPending(string id)
        {
            if (agents.TryGetValue(id, out var a)) a.Pending.Clear();
        }

        /// <summary>Forgets agents not heard from for a long time (and holding nothing).</summary>
        public void Prune()
        {
            var now = clock();
            foreach (var a in agents.Values.ToList())
            {
                if (a.Id != HolderId && a.Pending.Count == 0 && now - a.LastSeen > AgentTtl)
                {
                    agents.Remove(a.Id);
                    if (FocusedAgentId == a.Id) FocusedAgentId = null;
                }
            }
        }

        /// <summary>Waiting agents in the order they will get their turn.</summary>
        public IReadOnlyList<AgentRecord> WaitingOrder()
        {
            DropExpired();
            return agents.Values.Where(a => a.Pending.Count > 0 && a.Id != HolderId)
                .OrderByDescending(a => a.Id == FocusedAgentId)
                .ThenByDescending(a => a.Priority)
                .ThenBy(a => a.Pending[0].Seq)
                .ToList();
        }

        private void DropExpired()
        {
            var ttl = pendingTtl();
            if (ttl == null)
            {
                return;
            }
            var now = clock();
            foreach (var a in agents.Values)
            {
                a.Pending.RemoveAll(p => now - p.Added > ttl.Value);
            }
        }

        private int QueuePosition(AgentRecord a) => WaitingOrder().ToList().FindIndex(x => x.Id == a.Id) + 1;

        private string? PickNext()
        {
            var next = WaitingOrder().FirstOrDefault();
            if (next == null)
            {
                return null;
            }
            HolderId = next.Id;
            Flush(next, firstInterrupts: false);
            return next.Id;
        }

        private void Speak(AgentRecord a, string text, string mode, object? extra)
        {
            if (speakWithExtra != null)
            {
                speakWithExtra(a, text, mode, extra);
            }
            else
            {
                speak(a, text, mode);
            }
        }

        private void Flush(AgentRecord a, bool firstInterrupts)
        {
            var items = a.Pending.ToList();
            a.Pending.Clear();
            for (int i = 0; i < items.Count; i++)
            {
                Speak(a, items[i].Text, i == 0 && firstInterrupts ? "interrupt" : "queue", items[i].Extra);
            }
        }
    }

    /// <summary>Works out which agent's window the user is in. Pure; the app supplies the facts.</summary>
    public static class FocusMatcher
    {
        /// <param name="isAncestor">(ancestorPid, pid): is the first process an ancestor of (or equal to) the second?</param>
        public static string? Match(IEnumerable<AgentRecord> agents, int? focusedPid, string? focusedTitle, Func<int, int, bool> isAncestor)
        {
            if (focusedPid == null)
            {
                return null;
            }
            var byProcess = agents.Where(a => a.Pid.HasValue && isAncestor(focusedPid.Value, a.Pid.Value)).ToList();
            if (byProcess.Count == 1)
            {
                return byProcess[0].Id;
            }
            // One terminal process hosts several agents (tabs, split panes): only a title hint can tell them apart.
            var byTitle = (byProcess.Count > 1 ? byProcess : agents.ToList())
                .Where(a => !string.IsNullOrEmpty(a.TitleHint) && focusedTitle != null
                    && focusedTitle.Contains(a.TitleHint!, StringComparison.OrdinalIgnoreCase)
                    && (byProcess.Count > 1 || !a.Pid.HasValue || isAncestor(focusedPid.Value, a.Pid.Value)))
                .ToList();
            return byTitle.Count == 1 ? byTitle[0].Id : null;
        }
    }
}
