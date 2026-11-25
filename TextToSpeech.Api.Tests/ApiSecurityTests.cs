using TextToSpeech.Api;
using Xunit;

namespace TextToSpeech.Api.Tests
{
    public class ApiSecurityTests
    {
        private readonly ApiSecurity security = new(Req.Token, Req.Port);

        [Fact]
        public void Valid_request_passes() =>
            Assert.Null(security.Check(Req.Make("GET", "/v1/status")));

        [Theory]
        [InlineData("localhost:47821")]
        [InlineData("[::1]:47821")]
        [InlineData("LOCALHOST:47821")]
        public void Loopback_host_names_pass(string host) =>
            Assert.Null(security.Check(Req.Make("GET", "/v1/status", host: host)));

        [Theory]
        [InlineData("evil.example:47821")]
        [InlineData("127.0.0.1:1")]
        [InlineData("127.0.0.1")]
        [InlineData(null)]
        public void Other_hosts_are_rejected_for_dns_rebinding(string? host)
        {
            var r = security.Check(Req.Make("GET", "/v1/status", host: host));
            Assert.Equal(403, r!.Status);
            Assert.Equal("forbidden_host", Req.ErrorCode(r));
        }

        [Fact]
        public void Any_origin_header_is_rejected()
        {
            var r = security.Check(Req.Make("GET", "/v1/status", origin: "http://localhost:3000"));
            Assert.Equal(403, r!.Status);
            Assert.Equal("forbidden_origin", Req.ErrorCode(r));
        }

        [Theory]
        [InlineData(null)]
        [InlineData("wrong")]
        [InlineData("test-token-extra")]
        [InlineData("")]
        public void Missing_or_wrong_token_is_401(string? token)
        {
            var r = security.Check(Req.Make("GET", "/v1/status", token: token));
            Assert.Equal(401, r!.Status);
        }

        [Fact]
        public void Generated_tokens_are_64_hex_chars_and_unique()
        {
            string a = ApiSecurity.GenerateToken(), b = ApiSecurity.GenerateToken();
            Assert.Equal(64, a.Length);
            Assert.Matches("^[0-9a-f]{64}$", a);
            Assert.NotEqual(a, b);
        }

        [Fact]
        public void Header_lookup_is_case_insensitive()
        {
            var r = new ApiRequest("GET", "/", null, new Dictionary<string, string> { ["authorization"] = "Bearer test-token", ["HOST"] = "127.0.0.1:47821" }, null);
            Assert.Null(security.Check(r));
        }
    }
}
