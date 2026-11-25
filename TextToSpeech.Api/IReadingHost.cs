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

        /// <summary>action: pause, resume, toggle, skip, back, stop or clear.</summary>
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
    }
}
