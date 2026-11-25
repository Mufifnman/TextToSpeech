using System.Text.Json;
using TextToSpeech.Api;

namespace TextToSpeech.Api.Tests
{
    internal sealed class FakeHost : IReadingHost
    {
        public List<string> Calls { get; } = new();
        public SpeakRequest? LastSpeak { get; private set; }
        public TakeRequest? LastTake { get; private set; }
        public ReplaceRequest? LastReplace { get; private set; }

        public static StatusDto Status => new("idle", null, 4, 80, null, Array.Empty<QueuedSession>(), new WindowDto("hidden", false), ApiRouter.ApiVersion);

        public Task<StatusDto> GetStatusAsync() { Calls.Add("status"); return Task.FromResult(Status); }
        public Task<SpeakResponse> SpeakAsync(SpeakRequest request) { Calls.Add("speak"); LastSpeak = request; return Task.FromResult(new SpeakResponse("s_1", 1, "speaking", 0, false)); }
        public Task<StatusDto> ControlAsync(string action) { Calls.Add(action); return Task.FromResult(Status); }
        public Task<StatusDto> JumpAsync(JumpRequest request) { Calls.Add("jump"); return Task.FromResult(Status); }
        public Task<IReadOnlyList<VoiceDto>> GetVoicesAsync() { Calls.Add("voices"); return Task.FromResult<IReadOnlyList<VoiceDto>>(new[] { new VoiceDto("v1", "Voice One", "sapi", false) }); }
        public Task<StatusDto> SetVoiceAsync(string voiceId) { Calls.Add("voice:" + voiceId); return Task.FromResult(Status); }
        public Task<StatusDto> SetRateAsync(int rate) { Calls.Add("rate:" + rate); return Task.FromResult(Status); }
        public Task<StatusDto> SetVolumeAsync(int volume) { Calls.Add("volume:" + volume); return Task.FromResult(Status); }
        public Task<PeekResponse> PeekAsync(string sessionId) { Calls.Add("peek:" + sessionId); return Task.FromResult(new PeekResponse(sessionId, 1, "speaking", "a ", "b", new PositionDto(0, 1, 2))); }
        public Task<TakeResponse> TakeAsync(string sessionId, TakeRequest request) { Calls.Add("take:" + sessionId); LastTake = request; return Task.FromResult(new TakeResponse("h_1", sessionId, 1, "a ", "b", new PositionDto(0, 1, 2), DateTimeOffset.UnixEpoch)); }
        public Task<StatusDto> ReplaceAsync(string sessionId, ReplaceRequest request) { Calls.Add("replace:" + sessionId); LastReplace = request; return Task.FromResult(Status); }
        public Task<StatusDto> ReleaseAsync(string sessionId, ReleaseRequest request) { Calls.Add("release:" + sessionId); return Task.FromResult(Status); }
        public Task<StatusDto> PatchAsync(string sessionId, PatchRequest request) { Calls.Add("patch:" + sessionId); return Task.FromResult(Status); }
    }

    internal static class Req
    {
        public const string Token = "test-token";
        public const int Port = 47821;

        public static ApiRequest Make(string method, string path, string? body = null, string? token = Token, string? host = "127.0.0.1:47821", string? origin = null)
        {
            var headers = new Dictionary<string, string>();
            if (host != null) headers["Host"] = host;
            if (token != null) headers["Authorization"] = "Bearer " + token;
            if (origin != null) headers["Origin"] = origin;
            return new ApiRequest(method, path, null, headers, body);
        }

        public static string ErrorCode(ApiResponse r) =>
            JsonDocument.Parse(r.Json!).RootElement.GetProperty("error").GetProperty("code").GetString()!;
    }
}
