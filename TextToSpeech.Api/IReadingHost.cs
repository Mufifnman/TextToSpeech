namespace TextToSpeech.Api
{
    /// <summary>
    /// What the API needs from the app. The reading controller implements it and is responsible for
    /// running every call on the UI thread (the reading document is single threaded). Implementations
    /// throw <see cref="ApiException"/> for client errors such as an unknown session.
    /// </summary>
    public interface IReadingHost
    {
        Task<StatusDto> GetStatusAsync();
        Task<SpeakResponse> SpeakAsync(SpeakRequest request);

        /// <summary>action: pause, resume, toggle, skip, back, stop, clear or next (continue from a pause point).</summary>
        Task<StatusDto> ControlAsync(string action);
        Task<StatusDto> JumpAsync(JumpRequest request);

        Task<IReadOnlyList<VoiceDto>> GetVoicesAsync();
        Task<StatusDto> SetVoiceAsync(string voiceId);
        Task<StatusDto> SetRateAsync(int rate);
        Task<StatusDto> SetVolumeAsync(int volume);

        /// <summary>sessionId may be "current".</summary>
        Task<PeekResponse> PeekAsync(string sessionId);
        Task<TakeResponse> TakeAsync(string sessionId, TakeRequest request);
        Task<StatusDto> ReplaceAsync(string sessionId, ReplaceRequest request);
        Task<StatusDto> ReleaseAsync(string sessionId, ReleaseRequest request);
        Task<StatusDto> PatchAsync(string sessionId, PatchRequest request);

        /// <summary>Marks a pause point answered; when reading is waiting at it, carries on.</summary>
        Task<StatusDto> AnsweredAsync(string pauseId, string? sessionId) =>
            throw ApiException.NotFound("Pause points are not supported here.", "unsupported");

        /// <summary>The user's answer to the question reading is waiting at (or a named one); hands it to the caller and continues reading.</summary>
        Task<ReplyDto> SubmitReplyAsync(ReplyRequest request) =>
            throw ApiException.NotFound("Replies are not supported here.", "unsupported");

        /// <summary>Replies nobody has taken yet, without removing them.</summary>
        Task<RepliesResponse> GetRepliesAsync() =>
            throw ApiException.NotFound("Replies are not supported here.", "unsupported");

        /// <summary>Returns the matching replies and removes them (a caller gets each reply once).</summary>
        Task<RepliesResponse> TakeRepliesAsync(TakeRepliesRequest request) =>
            throw ApiException.NotFound("Replies are not supported here.", "unsupported");
    }
}
