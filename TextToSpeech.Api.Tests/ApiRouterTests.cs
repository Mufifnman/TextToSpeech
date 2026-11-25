using System.Text.Json;
using TextToSpeech.Api;
using Xunit;

namespace TextToSpeech.Api.Tests
{
    public class ApiRouterTests
    {
        private readonly FakeHost host = new();
        private readonly ApiRouter router;

        public ApiRouterTests() => router = new ApiRouter(host, new ApiSecurity(Req.Token, Req.Port));

        private Task<ApiResponse> Send(string method, string path, string? body = null) =>
            router.HandleAsync(Req.Make(method, path, body));

        [Fact]
        public async Task Status_returns_camel_case_json()
        {
            var r = await Send("GET", "/v1/status");
            Assert.Equal(200, r.Status);
            var root = JsonDocument.Parse(r.Json!).RootElement;
            Assert.Equal("idle", root.GetProperty("state").GetString());
            Assert.Equal(1, root.GetProperty("apiVersion").GetInt32());
            Assert.Equal("hidden", root.GetProperty("window").GetProperty("mode").GetString());
        }

        [Fact]
        public async Task Unauthenticated_requests_never_reach_the_host()
        {
            var r = await router.HandleAsync(Req.Make("POST", "/v1/speak", "{\"text\":\"hi\"}", token: null));
            Assert.Equal(401, r.Status);
            Assert.Empty(host.Calls);
        }

        [Fact]
        public async Task Speak_applies_defaults_and_returns_202()
        {
            var r = await Send("POST", "/v1/speak", "{\"text\":\"Build finished.\",\"source\":\"claude-code\"}");
            Assert.Equal(202, r.Status);
            Assert.Equal("queue", host.LastSpeak!.Mode);
            Assert.Equal("claude-code", host.LastSpeak.Source);
            Assert.Equal("s_1", JsonDocument.Parse(r.Json!).RootElement.GetProperty("sessionId").GetString());
        }

        [Theory]
        [InlineData("{}")]
        [InlineData("{\"text\":\"  \"}")]
        [InlineData("{\"text\":\"hi\",\"mode\":\"shout\"}")]
        [InlineData("{\"text\":\"hi\",\"rate\":3}")]      // per-request overrides are not supported yet
        [InlineData("{\"text\":\"hi\",\"volume\":50}")]
        [InlineData("{\"text\":\"hi\",\"voice\":\"x\"}")]
        public async Task Bad_speak_requests_are_400_and_not_forwarded(string body)
        {
            var r = await Send("POST", "/v1/speak", body);
            Assert.Equal(400, r.Status);
            Assert.Empty(host.Calls);
        }

        [Fact]
        public async Task Malformed_json_is_400_bad_json()
        {
            var r = await Send("POST", "/v1/speak", "{nope");
            Assert.Equal(400, r.Status);
            Assert.Equal("bad_json", Req.ErrorCode(r));
        }

        [Fact]
        public async Task Missing_body_is_400()
        {
            var r = await Send("POST", "/v1/speak");
            Assert.Equal(400, r.Status);
        }

        [Fact]
        public async Task Oversized_body_is_413()
        {
            var r = await Send("POST", "/v1/speak", new string('x', ApiRouter.MaxBodyChars + 1));
            Assert.Equal(413, r.Status);
        }

        [Theory]
        [InlineData("pause")]
        [InlineData("resume")]
        [InlineData("toggle")]
        [InlineData("skip")]
        [InlineData("back")]
        [InlineData("stop")]
        [InlineData("clear")]
        public async Task Control_routes_call_the_host(string action)
        {
            var r = await Send("POST", "/v1/" + action);
            Assert.Equal(200, r.Status);
            Assert.Equal(new[] { action }, host.Calls);
        }

        [Fact]
        public async Task Wrong_method_is_405_and_unknown_route_is_404()
        {
            Assert.Equal(405, (await Send("GET", "/v1/pause")).Status);
            Assert.Equal(405, (await Send("POST", "/v1/status")).Status);
            Assert.Equal(404, (await Send("GET", "/v1/nothing")).Status);
            Assert.Equal(404, (await Send("GET", "/v2/status")).Status);
            Assert.Equal(404, (await Send("GET", "/")).Status);
        }

        [Fact]
        public async Task Voice_rate_and_volume_are_validated_and_forwarded()
        {
            Assert.Equal(200, (await Send("PUT", "/v1/voice", "{\"id\":\"piper-amy\"}")).Status);
            Assert.Equal(200, (await Send("PUT", "/v1/rate", "{\"rate\":-3}")).Status);
            Assert.Equal(200, (await Send("PUT", "/v1/volume", "{\"volume\":0}")).Status);
            Assert.Equal(200, (await Send("PUT", "/v1/volume", "{\"volume\":120}")).Status);
            Assert.Equal(new[] { "voice:piper-amy", "rate:-3", "volume:0", "volume:120" }, host.Calls);

            Assert.Equal(400, (await Send("PUT", "/v1/rate", "{\"rate\":99}")).Status);
            Assert.Equal(400, (await Send("PUT", "/v1/volume", "{}")).Status);
            Assert.Equal(400, (await Send("PUT", "/v1/volume", "{\"volume\":121}")).Status);
            Assert.Equal(400, (await Send("PUT", "/v1/voice", "{}")).Status);
        }

        [Fact]
        public async Task Voices_lists_from_the_host()
        {
            var r = await Send("GET", "/v1/voices");
            Assert.Equal("v1", JsonDocument.Parse(r.Json!).RootElement[0].GetProperty("id").GetString());
        }

        [Fact]
        public async Task Peek_take_replace_release_patch_route_with_the_session_id()
        {
            Assert.Equal(200, (await Send("GET", "/v1/sessions/current")).Status);
            Assert.Equal(200, (await Send("POST", "/v1/sessions/s_1/take")).Status); // body optional
            Assert.Equal(200, (await Send("POST", "/v1/sessions/s_1/release")).Status);
            Assert.Equal(200, (await Send("PATCH", "/v1/sessions/s_1", "{\"ifRevision\":1,\"fromOffset\":5,\"text\":\"x\"}")).Status);
            Assert.Equal(new[] { "peek:current", "take:s_1", "release:s_1", "patch:s_1" }, host.Calls);
            Assert.Equal("word", host.LastTake!.From);
        }

        [Fact]
        public async Task Replace_defaults_placement_front_and_autoresume()
        {
            var r = await Send("POST", "/v1/sessions/s_1/replace", "{\"handoffId\":\"h_1\",\"text\":\"New rest.\"}");
            Assert.Equal(200, r.Status);
            Assert.Equal("front", host.LastReplace!.Placement);
            Assert.Equal(0, host.LastReplace.StartAt);
            Assert.True(host.LastReplace.AutoResume);
        }

        [Theory]
        [InlineData("{\"text\":\"x\"}")]                                       // no handoff id
        [InlineData("{\"handoffId\":\"h\"}")]                                   // no text
        [InlineData("{\"handoffId\":\"h\",\"text\":\"x\",\"placement\":\"middle\"}")]
        [InlineData("{\"handoffId\":\"h\",\"text\":\"x\",\"startAt\":5}")]      // beyond text
        public async Task Bad_replace_requests_are_400(string body)
        {
            var r = await Send("POST", "/v1/sessions/s_1/replace", body);
            Assert.Equal(400, r.Status);
            Assert.Empty(host.Calls);
        }

        [Fact]
        public async Task Bad_take_and_patch_requests_are_400()
        {
            Assert.Equal(400, (await Send("POST", "/v1/sessions/s_1/take", "{\"from\":\"paragraph\"}")).Status);
            Assert.Equal(400, (await Send("POST", "/v1/sessions/s_1/take", "{\"leaseSeconds\":0}")).Status);
            Assert.Equal(400, (await Send("PATCH", "/v1/sessions/s_1", "{\"text\":\"x\"}")).Status);
        }

        [Fact]
        public async Task Host_api_errors_become_error_json_with_their_status()
        {
            var failing = new ApiRouter(new ThrowingHost(), new ApiSecurity(Req.Token, Req.Port));
            var r = await failing.HandleAsync(Req.Make("POST", "/v1/sessions/s_1/replace", "{\"handoffId\":\"h\",\"text\":\"x\"}"));
            Assert.Equal(409, r.Status);
            Assert.Equal("handoff_closed", Req.ErrorCode(r));
        }

        [Fact]
        public async Task Unexpected_exceptions_are_500_without_details()
        {
            var failing = new ApiRouter(new CrashingHost(), new ApiSecurity(Req.Token, Req.Port));
            var r = await failing.HandleAsync(Req.Make("GET", "/v1/status"));
            Assert.Equal(500, r.Status);
            Assert.DoesNotContain("secret", r.Json);
        }

        private sealed class ThrowingHost : IReadingHost
        {
            private static Exception Fail() => ApiException.Conflict("handoff_closed", "closed");
            public Task<StatusDto> GetStatusAsync() => throw Fail();
            public Task<SpeakResponse> SpeakAsync(SpeakRequest r) => throw Fail();
            public Task<StatusDto> ControlAsync(string a) => throw Fail();
            public Task<StatusDto> JumpAsync(JumpRequest r) => throw Fail();
            public Task<IReadOnlyList<VoiceDto>> GetVoicesAsync() => throw Fail();
            public Task<StatusDto> SetVoiceAsync(string v) => throw Fail();
            public Task<StatusDto> SetRateAsync(int v) => throw Fail();
            public Task<StatusDto> SetVolumeAsync(int v) => throw Fail();
            public Task<PeekResponse> PeekAsync(string s) => throw Fail();
            public Task<TakeResponse> TakeAsync(string s, TakeRequest r) => throw Fail();
            public Task<StatusDto> ReplaceAsync(string s, ReplaceRequest r) => throw Fail();
            public Task<StatusDto> ReleaseAsync(string s, ReleaseRequest r) => throw Fail();
            public Task<StatusDto> PatchAsync(string s, PatchRequest r) => throw Fail();
        }

        private sealed class CrashingHost : IReadingHost
        {
            private static Exception Fail() => new InvalidOperationException("secret path C:\\Users\\x");
            public Task<StatusDto> GetStatusAsync() => throw Fail();
            public Task<SpeakResponse> SpeakAsync(SpeakRequest r) => throw Fail();
            public Task<StatusDto> ControlAsync(string a) => throw Fail();
            public Task<StatusDto> JumpAsync(JumpRequest r) => throw Fail();
            public Task<IReadOnlyList<VoiceDto>> GetVoicesAsync() => throw Fail();
            public Task<StatusDto> SetVoiceAsync(string v) => throw Fail();
            public Task<StatusDto> SetRateAsync(int v) => throw Fail();
            public Task<StatusDto> SetVolumeAsync(int v) => throw Fail();
            public Task<PeekResponse> PeekAsync(string s) => throw Fail();
            public Task<TakeResponse> TakeAsync(string s, TakeRequest r) => throw Fail();
            public Task<StatusDto> ReplaceAsync(string s, ReplaceRequest r) => throw Fail();
            public Task<StatusDto> ReleaseAsync(string s, ReleaseRequest r) => throw Fail();
            public Task<StatusDto> PatchAsync(string s, PatchRequest r) => throw Fail();
        }
    }
}
