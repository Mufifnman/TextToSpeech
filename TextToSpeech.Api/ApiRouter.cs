using System.Text.Json;

namespace TextToSpeech.Api
{
    /// <summary>
    /// Turns an <see cref="ApiRequest"/> into a call on <see cref="IReadingHost"/> and a JSON response:
    /// security check, routing, input validation and error mapping. No HTTP server in here; the app's
    /// listener builds the request and writes the response. <c>GET /v1/events</c> (a stream) is handled
    /// by the listener, which calls <see cref="Authorize"/> itself.
    /// </summary>
    public sealed class ApiRouter
    {
        public const int ApiVersion = 1;
        public const int MaxBodyChars = 1_000_000;

        private static readonly HashSet<string> ControlActions = new() { "pause", "resume", "toggle", "skip", "back", "stop", "clear" };
        private static readonly HashSet<string> SpeakModes = new() { "queue", "interrupt", "if-idle" };

        private readonly IReadingHost host;
        private readonly ApiSecurity security;

        public ApiRouter(IReadingHost host, ApiSecurity security)
        {
            this.host = host;
            this.security = security;
        }

        public ApiResponse? Authorize(ApiRequest request) => security.Check(request);

        public async Task<ApiResponse> HandleAsync(ApiRequest request)
        {
            var denied = security.Check(request);
            if (denied != null)
            {
                return denied;
            }
            try
            {
                if (request.Body != null && request.Body.Length > MaxBodyChars)
                {
                    throw new ApiException(413, "too_large", "Request body is too large.");
                }
                return await RouteAsync(request);
            }
            catch (ApiException e)
            {
                return ApiSecurity.Error(e.HttpStatus, e.Code, e.Message);
            }
            catch (JsonException)
            {
                return ApiSecurity.Error(400, "bad_json", "Request body is not valid JSON.");
            }
            catch (Exception)
            {
                // Details stay in the app; do not leak internals over the wire.
                return ApiSecurity.Error(500, "internal", "Internal error.");
            }
        }

        private async Task<ApiResponse> RouteAsync(ApiRequest request)
        {
            string method = request.Method.ToUpperInvariant();
            string[] parts = request.Path.Trim('/').Split('/', StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length < 2 || parts[0] != "v1")
            {
                throw ApiException.NotFound("Unknown route.");
            }

            switch (parts[1])
            {
                case "status" when parts.Length == 2:
                    Expect(method, "GET");
                    return Ok(await host.GetStatusAsync());

                case "voices" when parts.Length == 2:
                    Expect(method, "GET");
                    return Ok(await host.GetVoicesAsync());

                case "speak" when parts.Length == 2:
                    Expect(method, "POST");
                    return Accepted(await host.SpeakAsync(ValidateSpeak(Body<SpeakRequest>(request))));

                case "jump" when parts.Length == 2:
                    Expect(method, "POST");
                    return Ok(await host.JumpAsync(ValidateJump(Body<JumpRequest>(request))));

                case "voice" when parts.Length == 2:
                    Expect(method, "PUT");
                    return Ok(await host.SetVoiceAsync(Required(Body<VoiceRequest>(request).Id, "id")));

                case "rate" when parts.Length == 2:
                    Expect(method, "PUT");
                    return Ok(await host.SetRateAsync(InRange(Body<RateRequest>(request).Rate, "rate", -10, 10)));

                case "volume" when parts.Length == 2:
                    Expect(method, "PUT");
                    return Ok(await host.SetVolumeAsync(InRange(Body<VolumeRequest>(request).Volume, "volume", 0, 120)));

                case "sessions" when parts.Length == 3:
                    if (method == "GET")
                    {
                        return Ok(await host.PeekAsync(parts[2]));
                    }
                    Expect(method, "PATCH");
                    return Ok(await host.PatchAsync(parts[2], ValidatePatch(Body<PatchRequest>(request))));

                case "sessions" when parts.Length == 4:
                    Expect(method, "POST");
                    switch (parts[3])
                    {
                        case "take":
                            return Ok(await host.TakeAsync(parts[2], ValidateTake(BodyOrDefault(request, new TakeRequest(null, null, null)))));
                        case "replace":
                            return Ok(await host.ReplaceAsync(parts[2], ValidateReplace(Body<ReplaceRequest>(request))));
                        case "release":
                            return Ok(await host.ReleaseAsync(parts[2], BodyOrDefault(request, new ReleaseRequest(null))));
                    }
                    break;

                default:
                    if (parts.Length == 2 && ControlActions.Contains(parts[1]))
                    {
                        Expect(method, "POST");
                        return Ok(await host.ControlAsync(parts[1]));
                    }
                    break;
            }
            throw ApiException.NotFound("Unknown route.");
        }

        // ---- Validation ----

        private static SpeakRequest ValidateSpeak(SpeakRequest r)
        {
            if (string.IsNullOrWhiteSpace(r.Text))
            {
                throw ApiException.BadRequest("text is required.");
            }
            string mode = r.Mode ?? "queue";
            if (!SpeakModes.Contains(mode))
            {
                throw ApiException.BadRequest("mode must be queue, interrupt or if-idle.");
            }
            if (r.Voice != null || r.Rate.HasValue || r.Volume.HasValue)
            {
                throw ApiException.BadRequest("Per-request voice, rate and volume are not supported yet. Use PUT /v1/voice, /v1/rate and /v1/volume.", "unsupported_override");
            }
            return r with { Mode = mode };
        }

        private static JumpRequest ValidateJump(JumpRequest r)
        {
            if (r.Sentence < 0 || r.Word < 0)
            {
                throw ApiException.BadRequest("sentence and word must be 0 or more.");
            }
            return r;
        }

        private static TakeRequest ValidateTake(TakeRequest r)
        {
            string from = r.From ?? "word";
            if (from != "word" && from != "sentence")
            {
                throw ApiException.BadRequest("from must be word or sentence.");
            }
            if (r.LeaseSeconds.HasValue && r.LeaseSeconds.Value < 1)
            {
                throw ApiException.BadRequest("leaseSeconds must be 1 or more.");
            }
            return r with { From = from };
        }

        private static ReplaceRequest ValidateReplace(ReplaceRequest r)
        {
            Required(r.HandoffId, "handoffId");
            if (r.Text == null)
            {
                throw ApiException.BadRequest("text is required (use an empty string to drop the remainder).");
            }
            string placement = r.Placement ?? "front";
            if (placement != "front" && placement != "end")
            {
                throw ApiException.BadRequest("placement must be front or end.");
            }
            int startAt = r.StartAt ?? 0;
            if (startAt < 0 || startAt > r.Text.Length)
            {
                throw ApiException.BadRequest("startAt is outside the new text.", "bad_start_at");
            }
            return r with { Placement = placement, StartAt = startAt, AutoResume = r.AutoResume ?? true };
        }

        private static PatchRequest ValidatePatch(PatchRequest r)
        {
            if (!r.FromOffset.HasValue || r.FromOffset.Value < 0)
            {
                throw ApiException.BadRequest("fromOffset is required and must be 0 or more.");
            }
            if (r.Text == null)
            {
                throw ApiException.BadRequest("text is required.");
            }
            return r;
        }

        private static string Required(string? value, string name) =>
            string.IsNullOrWhiteSpace(value) ? throw ApiException.BadRequest($"{name} is required.") : value;

        private static int InRange(int? value, string name, int min, int max)
        {
            if (!value.HasValue || value.Value < min || value.Value > max)
            {
                throw ApiException.BadRequest($"{name} must be between {min} and {max}.");
            }
            return value.Value;
        }

        // ---- Plumbing ----

        private static void Expect(string actual, string expected)
        {
            if (actual != expected)
            {
                throw new ApiException(405, "method_not_allowed", $"Use {expected} for this route.");
            }
        }

        private static T Body<T>(ApiRequest request) where T : class
        {
            if (string.IsNullOrWhiteSpace(request.Body))
            {
                throw ApiException.BadRequest("A JSON body is required.");
            }
            return JsonSerializer.Deserialize<T>(request.Body, ApiJson.Options)
                ?? throw ApiException.BadRequest("A JSON object body is required.");
        }

        private static T BodyOrDefault<T>(ApiRequest request, T fallback) where T : class =>
            string.IsNullOrWhiteSpace(request.Body) ? fallback : Body<T>(request);

        private static ApiResponse Ok(object body) => new(200, JsonSerializer.Serialize(body, ApiJson.Options));

        private static ApiResponse Accepted(object body) => new(202, JsonSerializer.Serialize(body, ApiJson.Options));
    }
}
