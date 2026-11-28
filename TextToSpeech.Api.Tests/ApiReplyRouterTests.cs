using System.Text.Json;
using TextToSpeech.Api;
using Xunit;

namespace TextToSpeech.Api.Tests
{
    public class ApiReplyRouterTests
    {
        private readonly HostRig rig = new();
        private readonly ApiRouter router;

        public ApiReplyRouterTests() => router = new ApiRouter(rig.Host, new ApiSecurity(Req.Token, Req.Port));

        private Task<ApiResponse> Send(string method, string path, string? body = null) =>
            router.HandleAsync(Req.Make(method, path, body));

        [Fact]
        public async Task Speak_with_expectsReply_and_no_questions_is_refused()
        {
            var r = await Send("POST", "/v1/speak", "{\"text\":\"Hello.\",\"expectsReply\":true}");
            Assert.Equal(400, r.Status);
            Assert.Equal("reply_needs_pause", Req.ErrorCode(r));
        }

        [Fact]
        public async Task A_reply_travels_from_post_to_take()
        {
            var speak = await Send("POST", "/v1/speak",
                "{\"text\":\"Done.\",\"source\":\"claude-code\",\"questions\":[\"Which branch?\"],\"expectsReply\":true}");
            Assert.Equal(202, speak.Status);
            rig.Finish(0);
            rig.Finish(1);

            var status = JsonDocument.Parse((await Send("GET", "/v1/status")).Json!).RootElement;
            Assert.Equal("q1", status.GetProperty("replyWaiting").GetProperty("pauseId").GetString());

            var post = await Send("POST", "/v1/replies", "{\"text\":\"master\"}");
            Assert.Equal(202, post.Status);

            var take = await Send("POST", "/v1/replies/take", "{\"source\":\"claude-code\"}");
            Assert.Equal(200, take.Status);
            var replies = JsonDocument.Parse(take.Json!).RootElement.GetProperty("replies");
            Assert.Equal(1, replies.GetArrayLength());
            Assert.Equal("master", replies[0].GetProperty("text").GetString());
            Assert.Equal("Which branch?", replies[0].GetProperty("question").GetString());
        }

        [Theory]
        [InlineData("{}")]
        [InlineData("{\"text\":\"  \"}")]
        [InlineData("{\"text\":\"hi\",\"sessionId\":\"s_1\"}")]   // a session without a pause id
        public async Task Bad_replies_are_rejected(string body)
        {
            var r = await Send("POST", "/v1/replies", body);
            Assert.Equal(400, r.Status);
        }

        [Fact]
        public async Task Reply_with_nobody_waiting_is_409()
        {
            var r = await Send("POST", "/v1/replies", "{\"text\":\"hello\"}");
            Assert.Equal(409, r.Status);
            Assert.Equal("no_reply_expected", Req.ErrorCode(r));
        }

        [Fact]
        public async Task Wait_seconds_out_of_range_is_rejected()
        {
            var r = await Send("POST", "/v1/replies/take", "{\"source\":\"claude-code\",\"waitSeconds\":9999}");
            Assert.Equal(400, r.Status);
        }

        [Theory]
        [InlineData("{}")]
        [InlineData("{\"waitSeconds\":5}")]
        public async Task Taking_replies_without_saying_whose_is_rejected(string body)
        {
            var r = await Send("POST", "/v1/replies/take", body);
            Assert.Equal(400, r.Status);
            Assert.Equal("reply_filter_required", Req.ErrorCode(r));
        }

        [Fact]
        public async Task Replies_need_a_token()
        {
            var r = await router.HandleAsync(Req.Make("POST", "/v1/replies", "{\"text\":\"hi\"}", token: null));
            Assert.Equal(401, r.Status);
        }
    }
}
