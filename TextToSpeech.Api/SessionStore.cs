namespace TextToSpeech.Api
{
    public enum SessionState
    {
        /// <summary>Normal: queued, speaking or paused by the user.</summary>
        Active,
        /// <summary>A client took the remainder and has not returned it yet.</summary>
        HandedOff,
    }

    public sealed class Handoff
    {
        public Handoff(string id, DateTimeOffset leaseExpiresAt)
        {
            Id = id;
            LeaseExpiresAt = leaseExpiresAt;
        }

        public string Id { get; }
        public DateTimeOffset LeaseExpiresAt { get; }
    }

    /// <summary>
    /// One piece of text being read. <see cref="Text"/> is the normalized text as the reading list holds
    /// it; <see cref="ReadChars"/> is how many characters from the start have been read (always at a word
    /// start). Everything from <see cref="ReadChars"/> on is the remainder.
    /// </summary>
    public sealed class SessionRecord
    {
        public SessionRecord(string id, string source, string text)
        {
            Id = id;
            Source = source;
            Text = text;
            Revision = 1;
        }

        public string Id { get; }
        public string Source { get; }
        public string Text { get; internal set; }
        public int Revision { get; internal set; }
        public int ReadChars { get; internal set; }
        public SessionState State { get; internal set; } = SessionState.Active;
        public Handoff? Handoff { get; internal set; }

        public string ReadText => Text.Substring(0, ReadChars);
        public string Remainder => Text.Substring(ReadChars);
    }

    public sealed record TakeResult(SessionRecord Session, Handoff Handoff, string ReadText, string Remainder);

    public sealed record ExpiredHandoff(SessionRecord Session, string HandoffId, string Reason);

    /// <summary>
    /// The take / replace / release rules for handing a session's unread text to a client and getting it
    /// back. Pure logic with an injectable clock; the reading controller keeps it in step with the
    /// reading list. Not thread safe: call from the UI thread like the reading document.
    ///
    /// Rules: taking requires the session to be Active and, if given, the matching revision; while taken
    /// the remainder is out of the reading list; replace/release need the matching handoff id; replacing
    /// bumps the revision; an expired lease (or a closed handoff) leaves the original remainder in place
    /// and Active, so no text is lost.
    /// </summary>
    public sealed class SessionStore
    {
        public static readonly TimeSpan DefaultLease = TimeSpan.FromSeconds(120);
        public static readonly TimeSpan MaxLease = TimeSpan.FromSeconds(3600);

        private readonly Dictionary<string, SessionRecord> sessions = new();
        private readonly Func<DateTimeOffset> clock;

        public SessionStore(Func<DateTimeOffset>? clock = null)
        {
            this.clock = clock ?? (() => DateTimeOffset.UtcNow);
        }

        public IReadOnlyCollection<SessionRecord> Sessions => sessions.Values;

        public SessionRecord Create(string source, string text)
        {
            var s = new SessionRecord("s_" + Guid.NewGuid().ToString("N").Substring(0, 8), string.IsNullOrWhiteSpace(source) ? "api" : source, text);
            sessions[s.Id] = s;
            return s;
        }

        public SessionRecord Get(string id) =>
            sessions.TryGetValue(id, out var s) ? s : throw ApiException.NotFound($"No session '{id}'.", "no_such_session");

        public void Remove(string id) => sessions.Remove(id);

        /// <summary>
        /// Aligns an Active session with what the reading list really holds (the list is the truth while a
        /// session is being read: sentences are re-split, trimmed and re-ordered there). Ignored while taken.
        /// </summary>
        public void SyncText(string id, string text, int readChars)
        {
            var s = Get(id);
            if (s.State != SessionState.Active)
            {
                return;
            }
            s.Text = text;
            s.ReadChars = Math.Clamp(readChars, 0, text.Length);
        }

        /// <summary>The unread part of an Active session was changed in place: new text, next revision.</summary>
        public SessionRecord Revise(string id, string newText, int readChars)
        {
            var s = Get(id);
            if (s.State != SessionState.Active)
            {
                throw ApiException.Conflict("already_taken", "The remainder of this session is taken; return or release it first.");
            }
            s.Text = newText;
            s.ReadChars = Math.Clamp(readChars, 0, newText.Length);
            s.Revision++;
            return s;
        }

        /// <summary>Drops sessions that are Active but have nothing left in the reading list (read and cleared away).</summary>
        public void Prune(Func<string, bool> hasSentences)
        {
            foreach (var s in sessions.Values.ToList())
            {
                if (s.State == SessionState.Active && !hasSentences(s.Id))
                {
                    sessions.Remove(s.Id);
                }
            }
        }

        /// <summary>The reader reached character <paramref name="readChars"/> (clamped, never moves backwards unless <paramref name="allowBackwards"/>).</summary>
        public void ReportProgress(string id, int readChars, bool allowBackwards = false)
        {
            var s = Get(id);
            if (s.State != SessionState.Active)
            {
                return;
            }
            readChars = WordSnap.Down(s.Text, readChars);
            if (allowBackwards || readChars > s.ReadChars)
            {
                s.ReadChars = readChars;
            }
        }

        /// <summary>
        /// Takes the remainder. <paramref name="startOffset"/> is where the remainder begins (the host
        /// passes the word being spoken, or the sentence start for from:"sentence").
        /// </summary>
        public TakeResult Take(string id, int startOffset, int? leaseSeconds = null, int? ifRevision = null)
        {
            var s = Get(id);
            if (s.State == SessionState.HandedOff)
            {
                throw ApiException.Conflict("already_taken", "The remainder of this session is already taken.");
            }
            if (ifRevision.HasValue && ifRevision.Value != s.Revision)
            {
                throw ApiException.Conflict("revision_mismatch", $"Session is at revision {s.Revision}, not {ifRevision.Value}.");
            }

            var lease = leaseSeconds.HasValue ? TimeSpan.FromSeconds(Math.Clamp(leaseSeconds.Value, 1, (int)MaxLease.TotalSeconds)) : DefaultLease;
            s.ReadChars = WordSnap.Down(s.Text, startOffset);
            s.State = SessionState.HandedOff;
            s.Handoff = new Handoff("h_" + Guid.NewGuid().ToString("N").Substring(0, 8), clock() + lease);
            return new TakeResult(s, s.Handoff, s.ReadText, s.Remainder);
        }

        /// <summary>
        /// Returns modified text: the session becomes <c>ReadText + newText</c>, reading continues at
        /// <paramref name="startAt"/> inside the new text (rounded down to a word start).
        /// </summary>
        public SessionRecord Replace(string id, string handoffId, string newText, int startAt = 0)
        {
            var s = RequireHandoff(id, handoffId);
            if (startAt < 0 || startAt > newText.Length)
            {
                throw ApiException.BadRequest("startAt is outside the new text.", "bad_start_at");
            }
            string read = s.ReadText;
            s.Text = read + newText;
            s.ReadChars = read.Length + WordSnap.Down(newText, startAt);
            s.Revision++;
            s.State = SessionState.Active;
            s.Handoff = null;
            return s;
        }

        /// <summary>Cancel the handoff: the original remainder is back, unchanged.</summary>
        public SessionRecord Release(string id, string handoffId)
        {
            var s = RequireHandoff(id, handoffId);
            s.State = SessionState.Active;
            s.Handoff = null;
            return s;
        }

        /// <summary>
        /// Ends handoffs whose lease has passed, restoring their original remainder. The caller
        /// restores the text to the reading list paused (a hung client must not start the app talking).
        /// </summary>
        public IReadOnlyList<ExpiredHandoff> ExpireLeases()
        {
            var now = clock();
            var expired = new List<ExpiredHandoff>();
            foreach (var s in sessions.Values)
            {
                if (s.State == SessionState.HandedOff && s.Handoff != null && s.Handoff.LeaseExpiresAt <= now)
                {
                    expired.Add(new ExpiredHandoff(s, s.Handoff.Id, "timeout"));
                    closedReasons[s.Handoff.Id] = "timeout";
                    s.State = SessionState.Active;
                    s.Handoff = null;
                }
            }
            return expired;
        }

        /// <summary>
        /// The user cleared the list or started something else while a session was taken: the handoff is
        /// closed and the session removed. A later replace gets 409 handoff_closed.
        /// </summary>
        public IReadOnlyList<ExpiredHandoff> CloseHandoffs(string reason)
        {
            var closed = new List<ExpiredHandoff>();
            foreach (var s in sessions.Values.ToList())
            {
                if (s.State == SessionState.HandedOff && s.Handoff != null)
                {
                    closed.Add(new ExpiredHandoff(s, s.Handoff.Id, reason));
                    closedReasons[s.Handoff.Id] = reason;
                    sessions.Remove(s.Id);
                }
            }
            return closed;
        }

        private readonly Dictionary<string, string> closedReasons = new();

        private SessionRecord RequireHandoff(string id, string handoffId)
        {
            if (!sessions.TryGetValue(id, out var s))
            {
                if (closedReasons.TryGetValue(handoffId, out var reason))
                {
                    throw ApiException.Conflict("handoff_closed", $"The handoff is closed ({reason}). Send the text as a new speak request.");
                }
                throw ApiException.NotFound($"No session '{id}'.", "no_such_session");
            }
            if (s.State != SessionState.HandedOff || s.Handoff == null)
            {
                if (closedReasons.TryGetValue(handoffId, out var closedReason))
                {
                    throw ApiException.Conflict("handoff_closed", $"The handoff is closed ({closedReason}); the original remainder was restored. Send the text as a new speak request.");
                }
                // Never taken, or already released.
                throw ApiException.Conflict("not_taken", "This session is not handed off (never taken, released or expired).");
            }
            if (s.Handoff.Id != handoffId)
            {
                throw ApiException.Conflict("bad_handoff", "handoffId does not match the open handoff.");
            }
            return s;
        }
    }
}
