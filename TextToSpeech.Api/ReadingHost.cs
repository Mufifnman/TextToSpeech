using TextToSpeech.Core.Reading;

namespace TextToSpeech.Api
{
    /// <summary>Something happened that listeners (the SSE stream) may want to know about.</summary>
    public sealed record HostEvent(string Type, string? SessionId, string? Detail);

    /// <summary>
    /// Implements the API on top of the app's <see cref="ReadingController"/>. API text goes into the same
    /// reading list as typed and pasted text, tagged with the session id (<see cref="Sentence.Owner"/>), so
    /// the window, hotkeys and API all see one reading state. <see cref="SessionStore"/> holds the handoff
    /// rules; this class keeps it in step with the list.
    ///
    /// Every method must be called on the UI thread (the controller and document are single threaded).
    /// Methods are synchronous inside and return completed tasks so the router stays async-friendly.
    /// </summary>
    public sealed class ReadingHost : IReadingHost
    {
        private readonly ReadingController reading;
        private readonly IAppControl app;
        private readonly SessionStore store;
        private readonly ReadingDocument doc;

        // Taken while the reader was on this session's text and playing: Release should carry on reading it.
        private readonly HashSet<string> resumeOnRelease = new();

        public ReadingHost(ReadingController reading, IAppControl app, SessionStore? store = null)
        {
            this.reading = reading;
            this.app = app;
            this.store = store ?? new SessionStore();
            doc = reading.Document;

            reading.StateChanged += () => Raise("state");
            reading.SentenceStarting += () => Raise("sentence", doc.CurrentOwner);
            reading.Finished += () => Raise("finished");
            reading.Notice += message => Raise("notice", null, message);
            reading.Cleared += OnCleared;
        }

        /// <summary>Raised on the UI thread.</summary>
        public event Action<HostEvent>? Event;

        private void Raise(string type, string? session = null, string? detail = null) =>
            Event?.Invoke(new HostEvent(type, session, detail));

        private void OnCleared()
        {
            foreach (var closed in store.CloseHandoffs("cleared"))
            {
                resumeOnRelease.Remove(closed.HandoffId);
                Raise("handoff-expired", closed.Session.Id, "cleared");
            }
            foreach (var s in store.Sessions.ToList())
            {
                store.Remove(s.Id);
            }
        }

        /// <summary>Call every few seconds (and it runs before each API call): restores text whose lease ran out.</summary>
        public void Tick()
        {
            foreach (var expired in store.ExpireLeases())
            {
                resumeOnRelease.Remove(expired.HandoffId);
                // Original remainder goes back where it was, paused: a hung client must not start the app talking.
                Restore(expired.Session);
                Raise("handoff-expired", expired.Session.Id, expired.Reason);
            }
            store.Prune(id => doc.OwnerSentenceCount(id) > 0);
        }

        // ---------- status ----------

        public StatusDto Snapshot()
        {
            VoiceDto? voice = reading.Voice is { } v ? new VoiceDto(v.Id, v.DisplayName, v.EngineName, v.IsNeural) : null;
            return new StatusDto(State(), voice, app.Rate, app.Volume, ActiveSession(), QueuedSessions(), app.Window, ApiRouter.ApiVersion);
        }

        private string State()
        {
            if (reading.IsPlaying) return "speaking";
            if (!doc.IsAtEnd) return "paused";
            return store.Sessions.Any(s => s.State == SessionState.HandedOff) ? "handed_off" : "idle";
        }

        private SessionSummary? ActiveSession()
        {
            var sentence = doc.CurrentSentence;
            if (sentence == null)
            {
                return null;
            }
            string? owner = sentence.Owner;
            if (owner == null || !TryGet(owner, out var record))
            {
                return new SessionSummary("ui", "ui", 0, new PositionDto(doc.Position.Sentence, doc.Position.Word, 0), doc.Count, Fraction(doc.Position.Sentence, doc.Count));
            }
            var split = doc.PeekOwner(owner);
            int full = split.ReadText.Length + split.Remainder.Length;
            return new SessionSummary(record!.Id, record.Source, record.Revision,
                new PositionDto(SessionSentenceIndex(owner, split.FirstIndex), split.Word, split.CharOffset),
                doc.OwnerSentenceCount(owner), full == 0 ? 0 : (double)split.CharOffset / full);
        }

        /// <summary>The other sessions with unread text after the one being read, in reading order. Window text is "ui".</summary>
        private IReadOnlyList<QueuedSession> QueuedSessions()
        {
            var result = new List<QueuedSession>();
            string? activeKey = doc.CurrentSentence == null ? null : doc.CurrentSentence.Owner ?? "ui";
            for (int i = doc.Position.Sentence + 1; i < doc.Count; i++)
            {
                string? owner = doc.Sentences[i].Owner;
                string key = owner ?? "ui";
                if (key == activeKey || result.Any(q => q.Id == key))
                {
                    continue;
                }
                var record = owner != null && TryGet(owner, out var r) ? r : null;
                result.Add(new QueuedSession(key, record?.Source ?? "ui", owner == null ? CountUi() : doc.OwnerSentenceCount(owner)));
            }
            return result;
        }

        private int CountUi() => doc.Sentences.Count(s => s.Owner == null);

        private static double Fraction(int part, int whole) => whole == 0 ? 0 : (double)part / whole;

        private bool TryGet(string id, out SessionRecord? record)
        {
            record = store.Sessions.FirstOrDefault(s => s.Id == id);
            return record != null;
        }

        /// <summary>Index of a document sentence among its owner's sentences (0 when the document index is -1).</summary>
        private int SessionSentenceIndex(string owner, int documentIndex)
        {
            if (documentIndex < 0) return doc.OwnerSentenceCount(owner);
            int n = 0;
            for (int i = 0; i < documentIndex && i < doc.Count; i++)
            {
                if (doc.Sentences[i].Owner == owner) n++;
            }
            return n;
        }

        public Task<StatusDto> GetStatusAsync()
        {
            Tick();
            return Task.FromResult(Snapshot());
        }

        // ---------- speaking ----------

        public Task<SpeakResponse> SpeakAsync(SpeakRequest request)
        {
            Tick();
            string mode = request.Mode ?? "queue";
            bool hadUnread = !doc.IsAtEnd;

            if (mode == "if-idle" && (reading.IsPlaying || hadUnread))
            {
                return Task.FromResult(new SpeakResponse("", 0, State(), 0, true));
            }

            string text = request.ApplyFilters == true ? app.ApplyFilters(request.Text!) : request.Text!;
            var session = store.Create(request.Source ?? "api", text);

            bool wasPlaying = reading.IsPlaying;
            int added;
            int queuePosition = 0;
            if (mode == "interrupt")
            {
                if (wasPlaying)
                {
                    reading.Pause(); // keeps the word, so the interrupted text is not lost
                }
                added = doc.InsertAt(doc.Position.Sentence, text, owner: session.Id);
            }
            else
            {
                queuePosition = hadUnread ? SessionsAhead() : 0;
                added = doc.Append(text, owner: session.Id);
            }

            if (added == 0)
            {
                store.Remove(session.Id);
                if (mode == "interrupt" && wasPlaying) reading.Play();
                throw ApiException.BadRequest("There is no speakable text in the request.", "no_speakable_text");
            }
            store.SyncText(session.Id, doc.OwnerText(session.Id), 0);

            // queue: only start when nothing was waiting (a paused list stays paused). interrupt: always speak now.
            if (mode == "interrupt" || !hadUnread)
            {
                reading.Play();
            }
            if (request.Popup != false)
            {
                app.ShowPopup(session.Source);
            }
            Raise("session-added", session.Id);
            return Task.FromResult(new SpeakResponse(session.Id, session.Revision, State(), queuePosition, false));
        }

        /// <summary>How many sessions will be read before one appended now: the one being read plus those already queued.</summary>
        private int SessionsAhead() => (doc.CurrentSentence != null ? 1 : 0) + QueuedSessions().Count;

        public Task<StatusDto> ControlAsync(string action)
        {
            Tick();
            switch (action)
            {
                case "pause":
                case "stop":
                    reading.Pause();
                    break;
                case "resume":
                    reading.Play();
                    break;
                case "toggle":
                    reading.TogglePlayPause();
                    break;
                case "skip":
                    reading.Skip();
                    break;
                case "back":
                    reading.Rewind();
                    break;
                case "clear":
                    reading.Clear();
                    break;
                default:
                    throw ApiException.BadRequest("Unknown action.");
            }
            return Task.FromResult(Snapshot());
        }

        public Task<StatusDto> JumpAsync(JumpRequest request)
        {
            Tick();
            int global = request.Sentence;
            if (!string.IsNullOrEmpty(request.SessionId))
            {
                global = -1;
                int n = 0;
                for (int i = 0; i < doc.Count; i++)
                {
                    if (doc.Sentences[i].Owner == request.SessionId && n++ == request.Sentence)
                    {
                        global = i;
                        break;
                    }
                }
            }
            if (global < 0 || global >= doc.Count)
            {
                throw ApiException.BadRequest("That sentence does not exist.", "bad_position");
            }
            reading.JumpTo(global, request.Word);
            return Task.FromResult(Snapshot());
        }

        // ---------- voice, speed, volume ----------

        public Task<IReadOnlyList<VoiceDto>> GetVoicesAsync() => Task.FromResult(app.ListVoices());

        public Task<StatusDto> SetVoiceAsync(string voiceId)
        {
            if (!app.TrySetVoice(voiceId))
            {
                throw ApiException.NotFound($"No voice '{voiceId}'.", "no_such_voice");
            }
            return Task.FromResult(Snapshot());
        }

        public Task<StatusDto> SetRateAsync(int rate)
        {
            app.SetRate(rate);
            return Task.FromResult(Snapshot());
        }

        public Task<StatusDto> SetVolumeAsync(int volume)
        {
            app.SetVolume(volume);
            return Task.FromResult(Snapshot());
        }

        // ---------- sessions: peek, take, replace, release, patch ----------

        private string Resolve(string sessionId)
        {
            if (sessionId == "current")
            {
                return doc.CurrentOwner ?? throw ApiException.NotFound("The sentence being read is not an API session.", "no_current_session");
            }
            return sessionId;
        }

        public Task<PeekResponse> PeekAsync(string sessionId)
        {
            Tick();
            string id = Resolve(sessionId);
            var s = store.Get(id);
            if (s.State == SessionState.HandedOff)
            {
                return Task.FromResult(new PeekResponse(id, s.Revision, "handed_off", s.ReadText, s.Remainder, PositionOf(s, -1, 0)));
            }
            if (doc.OwnerSentenceCount(id) == 0)
            {
                throw ApiException.NotFound("That session is no longer in the reading list.", "session_gone");
            }
            var split = doc.PeekOwner(id);
            store.SyncText(id, split.ReadText + split.Remainder, split.CharOffset);
            return Task.FromResult(new PeekResponse(id, s.Revision, State(), split.ReadText, split.Remainder,
                new PositionDto(SessionSentenceIndex(id, split.FirstIndex), split.Word, split.CharOffset)));
        }

        private static PositionDto PositionOf(SessionRecord s, int sentence, int word) => new(Math.Max(sentence, 0), word, s.ReadChars);

        public Task<TakeResponse> TakeAsync(string sessionId, TakeRequest request)
        {
            Tick();
            string id = Resolve(sessionId);
            var s = store.Get(id);
            if (s.State == SessionState.HandedOff)
            {
                throw ApiException.Conflict("already_taken", "The remainder of this session is already taken.");
            }
            if (request.IfRevision.HasValue && request.IfRevision.Value != s.Revision)
            {
                throw ApiException.Conflict("revision_mismatch", $"Session is at revision {s.Revision}, not {request.IfRevision.Value}.");
            }
            if (doc.OwnerSentenceCount(id) == 0)
            {
                throw ApiException.NotFound("That session is no longer in the reading list.", "session_gone");
            }

            bool fromSentence = request.From == "sentence";
            bool onIt = doc.CurrentOwner == id;
            bool wasPlaying = reading.IsPlaying;
            if (onIt && wasPlaying)
            {
                reading.Pause(); // stops speech and keeps the word, so the split lands on it
            }

            var peek = doc.PeekOwner(id, fromSentence);
            if (peek.Remainder.Length == 0)
            {
                if (onIt && wasPlaying) reading.Play();
                throw ApiException.Conflict("nothing_to_take", "Everything in this session has already been read.");
            }

            var split = doc.TakeOwnerRemainder(id, fromSentence);
            store.SyncText(id, split.ReadText + split.Remainder, split.CharOffset);
            var taken = store.Take(id, split.CharOffset, request.LeaseSeconds, request.IfRevision);
            if (onIt && wasPlaying)
            {
                resumeOnRelease.Add(taken.Handoff.Id);
            }

            // Other queued text keeps playing.
            if (onIt && wasPlaying && !doc.IsAtEnd)
            {
                reading.Play();
            }
            Raise("handoff-taken", id);
            return Task.FromResult(new TakeResponse(taken.Handoff.Id, id, s.Revision, taken.ReadText, taken.Remainder,
                new PositionDto(SessionSentenceIndex(id, split.FirstIndex), split.Word, split.CharOffset), taken.Handoff.LeaseExpiresAt));
        }

        public Task<StatusDto> ReplaceAsync(string sessionId, ReplaceRequest request)
        {
            Tick();
            string id = Resolve(sessionId);
            var s = store.Replace(id, request.HandoffId!, request.Text!, request.StartAt ?? 0);
            resumeOnRelease.Remove(request.HandoffId!);

            // Only the part from startAt on goes back into the list.
            int index = request.Placement == "end" ? doc.Count : FrontIndex();
            string remainder = s.Remainder;
            if (remainder.Length > 0)
            {
                doc.InsertAt(index, remainder, owner: id);
                if (request.AutoResume != false && !reading.IsPlaying)
                {
                    reading.Play();
                }
            }
            Raise("handoff-returned", id);
            return Task.FromResult(Snapshot());
        }

        public Task<StatusDto> ReleaseAsync(string sessionId, ReleaseRequest request)
        {
            Tick();
            string id = Resolve(sessionId);
            var s = store.Get(id);
            string? handoffId = request.HandoffId ?? s.Handoff?.Id;
            if (handoffId == null)
            {
                throw ApiException.Conflict("not_taken", "This session is not handed off.");
            }
            store.Release(id, handoffId);
            bool resume = resumeOnRelease.Remove(handoffId);
            Restore(s);
            if (resume && !reading.IsPlaying)
            {
                reading.Play();
            }
            Raise("handoff-released", id);
            return Task.FromResult(Snapshot());
        }

        public Task<StatusDto> PatchAsync(string sessionId, PatchRequest request)
        {
            Tick();
            string id = Resolve(sessionId);
            var s = store.Get(id);
            if (s.State == SessionState.HandedOff)
            {
                throw ApiException.Conflict("already_taken", "The remainder of this session is taken; return or release it first.");
            }
            if (request.IfRevision.HasValue && request.IfRevision.Value != s.Revision)
            {
                throw ApiException.Conflict("revision_mismatch", $"Session is at revision {s.Revision}, not {request.IfRevision.Value}.");
            }
            if (doc.OwnerSentenceCount(id) == 0)
            {
                throw ApiException.NotFound("That session is no longer in the reading list.", "session_gone");
            }

            // Find the sentence the offset falls in (the change starts at that sentence's start).
            var mine = new List<(int Index, int Start)>();
            int offset = 0;
            for (int i = 0; i < doc.Count; i++)
            {
                if (doc.Sentences[i].Owner == id)
                {
                    mine.Add((i, offset));
                    offset += doc.Sentences[i].Text.Length + 1;
                }
            }
            string full = doc.OwnerText(id);
            int from = request.FromOffset!.Value;
            if (from > full.Length)
            {
                throw ApiException.BadRequest("fromOffset is past the end of the text.", "bad_offset");
            }
            var target = mine.Last(m => m.Start <= from);

            // Speech is playing this session's sentence: only later sentences may change without stopping it.
            bool speakingHere = reading.IsPlaying && doc.CurrentOwner == id;
            int earliest = speakingHere ? doc.Position.Sentence + 1 : doc.Position.Sentence;
            if (target.Index < earliest)
            {
                throw ApiException.Conflict("too_late", "That part has already been read (or is being read right now).");
            }

            doc.RemoveOwnerFrom(id, target.Index);
            if (request.Text!.Length > 0)
            {
                doc.InsertAt(target.Index, request.Text, owner: id);
            }
            var split = doc.PeekOwner(id);
            store.Revise(id, doc.OwnerText(id), split.CharOffset);
            Raise("session-patched", id);
            return Task.FromResult(Snapshot());
        }

        // ---------- helpers ----------

        /// <summary>Where text that should be read next goes: after the sentence being spoken, or at the reader when paused.</summary>
        private int FrontIndex() => reading.IsPlaying ? Math.Min(doc.Position.Sentence + 1, doc.Count) : doc.Position.Sentence;

        /// <summary>Puts a session's unread remainder back into the list (where "next" is), not starting to speak.</summary>
        private void Restore(SessionRecord s)
        {
            string remainder = s.Remainder;
            if (remainder.Length > 0)
            {
                doc.InsertAt(FrontIndex(), remainder, owner: s.Id);
            }
        }
    }
}
