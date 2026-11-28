using System.Text.Json;

namespace TextToSpeech.Api
{
    /// <summary>Error that becomes an HTTP error response: <c>{ "error": { "code": ..., "message": ... } }</c>.</summary>
    public sealed class ApiException : Exception
    {
        public ApiException(int httpStatus, string code, string message) : base(message)
        {
            HttpStatus = httpStatus;
            Code = code;
        }

        public int HttpStatus { get; }
        public string Code { get; }

        public static ApiException BadRequest(string message, string code = "bad_request") => new(400, code, message);
        public static ApiException NotFound(string message, string code = "not_found") => new(404, code, message);
        public static ApiException Conflict(string code, string message) => new(409, code, message);
    }

    /// <summary>JSON settings shared by every request and response (camelCase, case-insensitive on input).</summary>
    public static class ApiJson
    {
        public static readonly JsonSerializerOptions Options = new()
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            PropertyNameCaseInsensitive = true,
        };
    }

    // ---- Requests -------------------------------------------------------------------------

    /// <param name="Mode">queue (default), interrupt or if-idle.</param>
    /// <param name="ApplyFilters">Run the app's text filters on the text. Default false for API text.</param>
    /// <param name="Popup">false reads without showing the reading popup. Default true.</param>
    /// <param name="Voice">Reserved: rejected for now (use PUT /v1/voice). Same for Rate and Volume.</param>
    /// <param name="Agent">Who is asking. Set, the request goes through arbitration (policy and turn taking); left out, it is spoken straight away as before.</param>
    /// <param name="Pauses">Places where reading stops and waits. The caller decides; the app never guesses what is a question.</param>
    /// <param name="Questions">Shorthand: the questions are added to the end of the text as a numbered list, each followed by a pause.</param>
    /// <param name="PlayAll">Read through the pauses of this text without stopping. Default: the app's "play all by default" setting.</param>
    /// <param name="ExpectsReply">The caller wants the user's answer to each question or pause point handed back (see <c>POST /v1/replies/take</c>). Needs questions or pauses.</param>
    public sealed record SpeakRequest(string? Text, string? Source, string? Mode, bool? ApplyFilters, bool? Popup,
        string? Voice, int? Rate, int? Volume, TextToSpeech.Api.Arbitration.AgentRef? Agent = null,
        IReadOnlyList<PauseRequest>? Pauses = null, IReadOnlyList<string>? Questions = null, bool? PlayAll = null,
        bool? ExpectsReply = null);

    /// <param name="Id">Name used to mark it answered later. Default: m1, m2...</param>
    /// <param name="Label">The question, so status can say what reading is waiting on. Optional.</param>
    /// <param name="At">Character offset into the text: reading stops after the paragraph or list item that contains it. Default: the end.</param>
    public sealed record PauseRequest(string? Id, string? Label, int? At);

    /// <param name="SessionId">Limits it to one session when two have a pause with the same id.</param>
    public sealed record AnsweredRequest(string? SessionId);

    /// <param name="Text">What the user said or typed.</param>
    /// <param name="SessionId">With <paramref name="PauseId"/>: answer a question that reading has not reached yet. Default: the question reading is waiting at.</param>
    /// <param name="PauseId">The pause point (question) being answered, such as q1.</param>
    public sealed record ReplyRequest(string? Text, string? SessionId, string? PauseId);

    /// <param name="Source">Only replies to text with this source (the agent label). Default: any.</param>
    /// <param name="SessionId">Only replies to this session. Default: any.</param>
    /// <param name="WaitSeconds">Wait up to this long (0 to 120) for a reply when none is there yet. Default 0.</param>
    public sealed record TakeRepliesRequest(string? Source, string? SessionId, int? WaitSeconds);

    public sealed record JumpRequest(string? SessionId, int Sentence, int Word);
    public sealed record VoiceRequest(string? Id);
    public sealed record RateRequest(int? Rate);
    public sealed record VolumeRequest(int? Volume);

    /// <param name="From">"word" (default) starts the remainder at the word being spoken, "sentence" at its sentence start.</param>
    public sealed record TakeRequest(string? From, int? LeaseSeconds, int? IfRevision);

    /// <param name="StartAt">Character offset into the new text to start speaking from (rounded down to a word start).</param>
    /// <param name="Placement">front (default, where the remainder was) or end of the queue.</param>
    public sealed record ReplaceRequest(string? HandoffId, string? Text, int? StartAt, string? Placement, bool? AutoResume);

    public sealed record ReleaseRequest(string? HandoffId);

    /// <summary>Replace everything from <paramref name="FromOffset"/> on without stopping speech.</summary>
    public sealed record PatchRequest(int? IfRevision, int? FromOffset, string? Text);

    // ---- Responses ------------------------------------------------------------------------

    public sealed record PositionDto(int Sentence, int Word, int CharOffset);

    public sealed record VoiceDto(string Id, string Name, string Engine, bool IsNeural);

    public sealed record SessionSummary(string Id, string Source, int Revision, PositionDto? Position, int Sentences, double Progress);

    public sealed record QueuedSession(string Id, string Source, int Sentences);

    /// <param name="Mode">hidden, popup, full.</param>
    public sealed record WindowDto(string Mode, bool Pinned);

    /// <param name="State">idle, speaking, paused, paused_at_pause (waiting at a question) or handed_off.</param>
    /// <param name="PausedAt">Only while State is paused_at_pause.</param>
    /// <param name="Pauses">Every pause point in the reading list, in reading order.</param>
    public sealed record StatusDto(string State, VoiceDto? Voice, int Rate, int Volume, SessionSummary? ActiveSession,
        IReadOnlyList<QueuedSession> Queued, WindowDto Window, int ApiVersion,
        PausedAtDto? PausedAt = null, IReadOnlyList<PauseDto>? Pauses = null, bool PlayAll = false,
        ReplyWaitingDto? ReplyWaiting = null);

    /// <summary>A caller that asked for the answer (<c>expectsReply</c>) is waiting at this question.</summary>
    public sealed record ReplyWaitingDto(string SessionId, string Source, string PauseId, string? Question, int Number, int Of);

    /// <param name="Seq">Increases with every reply, starting at 1.</param>
    /// <param name="Question">The label of the pause point that was answered.</param>
    public sealed record ReplyDto(long Seq, string SessionId, string Source, string PauseId, string? Question, string Text, DateTimeOffset At);

    public sealed record RepliesResponse(IReadOnlyList<ReplyDto> Replies);

    /// <param name="Number">Position among the session's pause points, starting at 1.</param>
    public sealed record PausedAtDto(string SessionId, string PauseId, string? Label, int Number, int Of);

    /// <param name="State">pending, waiting (reading is stopped here) or answered.</param>
    public sealed record PauseDto(string SessionId, string Id, string? Label, string State);

    /// <param name="Decision">Only with an agent: speaking, waiting, skipped or denied. Waiting text has no session yet (SessionId is empty).</param>
    public sealed record SpeakResponse(string SessionId, int Revision, string State, int QueuePosition, bool Skipped,
        string? Decision = null, string? Reason = null, string? HeldBy = null);

    public sealed record MuteRequest(int? Minutes);

    public sealed record AgentDto(string Id, string Label, int Priority, bool IsHolder, bool IsFocused, int Waiting, int WaitPosition);

    public sealed record PolicyResponse(bool MayRead, IReadOnlyList<string> Reasons, TextToSpeech.Api.Arbitration.ReadPolicy Policy,
        bool? HeadphonesConnected, string? MutedUntil);

    public sealed record ArbiterStatus(IReadOnlyList<AgentDto> Agents, string? HolderId, string? FocusedId, PolicyResponse Policy);

    /// <summary><c>ReadText + Remainder</c> always equals the session's text.</summary>
    public sealed record PeekResponse(string SessionId, int Revision, string State, string ReadText, string Remainder, PositionDto Position);

    public sealed record TakeResponse(string HandoffId, string SessionId, int Revision, string ReadText, string Remainder,
        PositionDto Position, DateTimeOffset LeaseExpiresAt);
}
