using System.Net;
using System.Net.Http.Headers;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using TextToSpeech.Api;
using Xunit;

namespace TextToSpeech.Api.Tests
{
    /// <summary>Real loopback HTTP against the real listener, router and host (fake engine and fake app shell).</summary>
    public class LocalApiServerTests : IDisposable
    {
        private const string Token = "integration-test-token";
        private readonly HostRig rig = new();
        private readonly LocalApiServer server;
        private readonly HttpClient http = new();

        public LocalApiServerTests()
        {
            server = new LocalApiServer(rig.Host, Token, work => work());
            server.Start(FreePort());
            http.BaseAddress = new Uri($"http://127.0.0.1:{server.Port}");
            http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", Token);
            http.Timeout = TimeSpan.FromSeconds(10);
        }

        public void Dispose()
        {
            server.Dispose();
            http.Dispose();
        }

        private static int FreePort()
        {
            var l = new TcpListener(IPAddress.Loopback, 0);
            l.Start();
            int port = ((IPEndPoint)l.LocalEndpoint).Port;
            l.Stop();
            return port;
        }

        private static StringContent Json(string json) => new(json, Encoding.UTF8, "application/json");

        [Fact]
        public async Task Status_over_http_returns_json()
        {
            var res = await http.GetAsync("/v1/status");
            Assert.Equal(HttpStatusCode.OK, res.StatusCode);
            var root = JsonDocument.Parse(await res.Content.ReadAsStringAsync()).RootElement;
            Assert.Equal("idle", root.GetProperty("state").GetString());
        }

        [Fact]
        public async Task Missing_or_wrong_token_is_401()
        {
            using var anon = new HttpClient { BaseAddress = http.BaseAddress };
            Assert.Equal(HttpStatusCode.Unauthorized, (await anon.GetAsync("/v1/status")).StatusCode);

            using var wrong = new HttpClient { BaseAddress = http.BaseAddress };
            wrong.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", "nope");
            Assert.Equal(HttpStatusCode.Unauthorized, (await wrong.GetAsync("/v1/status")).StatusCode);
        }

        [Fact]
        public async Task A_browser_origin_header_is_refused()
        {
            var req = new HttpRequestMessage(HttpMethod.Get, "/v1/status");
            req.Headers.Add("Origin", "http://evil.example");
            Assert.Equal(HttpStatusCode.Forbidden, (await http.SendAsync(req)).StatusCode);
        }

        [Fact]
        public async Task A_foreign_host_header_is_refused()
        {
            var req = new HttpRequestMessage(HttpMethod.Get, "/v1/status");
            req.Headers.Host = "evil.example";
            var res = await http.SendAsync(req);
            Assert.True(res.StatusCode is HttpStatusCode.Forbidden or HttpStatusCode.BadRequest, res.StatusCode.ToString());
        }

        [Fact]
        public async Task Speak_then_take_then_replace_round_trip()
        {
            var speak = await http.PostAsync("/v1/speak", Json("{\"text\":\"Build finished. Three tests failed. Parser broke.\",\"source\":\"test\"}"));
            Assert.Equal(HttpStatusCode.Accepted, speak.StatusCode);
            string id = JsonDocument.Parse(await speak.Content.ReadAsStringAsync()).RootElement.GetProperty("sessionId").GetString()!;
            rig.Finish(0);

            var take = await http.PostAsync($"/v1/sessions/{id}/take", Json("{}"));
            Assert.Equal(HttpStatusCode.OK, take.StatusCode);
            var taken = JsonDocument.Parse(await take.Content.ReadAsStringAsync()).RootElement;
            Assert.Equal("Three tests failed. Parser broke.", taken.GetProperty("remainder").GetString());
            string handoff = taken.GetProperty("handoffId").GetString()!;

            var replace = await http.PostAsync($"/v1/sessions/{id}/replace", Json($"{{\"handoffId\":\"{handoff}\",\"text\":\"Fixed.\"}}"));
            Assert.Equal(HttpStatusCode.OK, replace.StatusCode);
            Assert.Equal("Fixed.", rig.Engine.Calls.Last().Text);

            var late = await http.PostAsync($"/v1/sessions/{id}/replace", Json($"{{\"handoffId\":\"{handoff}\",\"text\":\"Again.\"}}"));
            Assert.Equal(HttpStatusCode.Conflict, late.StatusCode);
        }

        [Fact]
        public async Task Bad_input_is_400_and_unknown_route_is_404()
        {
            Assert.Equal(HttpStatusCode.BadRequest, (await http.PostAsync("/v1/speak", Json("{}"))).StatusCode);
            Assert.Equal(HttpStatusCode.NotFound, (await http.GetAsync("/v1/nope")).StatusCode);
        }

        [Fact]
        public async Task Events_stream_sends_the_current_state_first_and_then_changes()
        {
            using var stream = await http.GetStreamAsync("/v1/events");
            using var reader = new StreamReader(stream);
            var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));

            string text = await ReadUntilAsync(reader, "event: state", cts.Token);
            Assert.Contains("\"state\":\"idle\"", text);

            await http.PostAsync("/v1/speak", Json("{\"text\":\"Hello there.\"}"));
            string later = await ReadUntilAsync(reader, "event: session-added", cts.Token);
            Assert.Contains("session-added", later);
        }

        private static async Task<string> ReadUntilAsync(StreamReader reader, string marker, CancellationToken token)
        {
            var sb = new StringBuilder();
            while (!token.IsCancellationRequested)
            {
                string? line = await reader.ReadLineAsync(token);
                if (line == null) break;
                sb.AppendLine(line);
                if (line.StartsWith(marker)) 
                {
                    // include the data line that follows the event line
                    sb.AppendLine(await reader.ReadLineAsync(token) ?? "");
                    return sb.ToString();
                }
            }
            throw new TimeoutException("never saw '" + marker + "'. Got: " + sb);
        }

        [Fact]
        public void Start_falls_back_to_a_free_port_when_the_preferred_one_is_taken()
        {
            using var squatter = new TcpListener(IPAddress.Loopback, 0);
            squatter.Start();
            int taken = ((IPEndPoint)squatter.LocalEndpoint).Port;

            using var second = new LocalApiServer(rig.Host, Token, work => work());
            second.Start(taken);
            Assert.NotEqual(taken, second.Port);
            Assert.True(second.IsRunning);
        }
    }
}
