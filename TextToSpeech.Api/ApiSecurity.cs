using System.Security.Cryptography;
using System.Text;

namespace TextToSpeech.Api
{
    /// <summary>A transport-neutral HTTP request. Header names are matched case-insensitively.</summary>
    public sealed record ApiRequest(string Method, string Path, string? Query, IReadOnlyDictionary<string, string> Headers, string? Body)
    {
        public string? Header(string name)
        {
            foreach (var kv in Headers)
            {
                if (string.Equals(kv.Key, name, StringComparison.OrdinalIgnoreCase))
                {
                    return kv.Value;
                }
            }
            return null;
        }
    }

    public sealed record ApiResponse(int Status, string? Json);

    /// <summary>
    /// Guards the loopback API: bearer token, Host check (DNS rebinding) and Origin rejection (a web
    /// page in the user's browser must not be able to drive the app).
    /// </summary>
    public sealed class ApiSecurity
    {
        private readonly byte[] tokenBytes;
        private readonly HashSet<string> allowedHosts;

        public ApiSecurity(string token, int port)
        {
            if (string.IsNullOrEmpty(token))
            {
                throw new ArgumentException("token is required", nameof(token));
            }
            tokenBytes = Encoding.UTF8.GetBytes(token);
            allowedHosts = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            {
                $"127.0.0.1:{port}",
                $"localhost:{port}",
                $"[::1]:{port}",
            };
        }

        /// <summary>32 random bytes as 64 lowercase hex characters.</summary>
        public static string GenerateToken() => Convert.ToHexString(RandomNumberGenerator.GetBytes(32)).ToLowerInvariant();

        /// <summary>Null when the request may proceed, otherwise the error response to send.</summary>
        public ApiResponse? Check(ApiRequest request)
        {
            string? host = request.Header("Host");
            if (host == null || !allowedHosts.Contains(host))
            {
                return Error(403, "forbidden_host", "Host header must be 127.0.0.1, localhost or [::1] with the API port.");
            }
            if (request.Header("Origin") != null)
            {
                return Error(403, "forbidden_origin", "Browser requests are not allowed.");
            }

            string? auth = request.Header("Authorization");
            const string prefix = "Bearer ";
            if (auth == null || !auth.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            {
                return Error(401, "unauthorized", "Send the token as 'Authorization: Bearer <token>'.");
            }
            byte[] given = Encoding.UTF8.GetBytes(auth.Substring(prefix.Length).Trim());
            if (given.Length != tokenBytes.Length || !CryptographicOperations.FixedTimeEquals(given, tokenBytes))
            {
                return Error(401, "unauthorized", "Invalid token.");
            }
            return null;
        }

        public static ApiResponse Error(int status, string code, string message) =>
            new(status, System.Text.Json.JsonSerializer.Serialize(new { error = new { code, message } }, ApiJson.Options));
    }
}
