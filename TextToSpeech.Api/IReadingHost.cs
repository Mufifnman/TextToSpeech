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

        /// <summary>Text that will probably be read soon: the app prepares the audio. One hint per caller; a new one replaces it.</summary>
        Task<PrewarmResponse> PrewarmAsync(PrewarmRequest request) =>
            throw ApiException.NotFound("Prewarming is not supported here.", "unsupported");

        Task<PrewarmResponse> CancelPrewarmAsync(CancelPrewarmRequest request) =>
            throw ApiException.NotFound("Prewarming is not supported here.", "unsupported");

        Task<PrewarmResponse> GetPrewarmAsync() =>
            throw ApiException.NotFound("Prewarming is not supported here.", "unsupported");

        /// <summary>Marks a pause point answered; when reading is waiting at it, carries on.</summary>
        Task<StatusDto> AnsweredAsync(string pauseId, string? sessionId) =>
            throw ApiException.NotFound("Pause points are not supported here.", "unsupported");
    }
}
