using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using System.Threading.Channels;

namespace TextToSpeech.Api
{
    /// <summary>
    /// The loopback HTTP listener. Binds 127.0.0.1 only (HttpListener also refuses a Host header that is not the
    /// prefix), turns each HTTP request into an <see cref="ApiRequest"/>, runs the router on the UI thread through
    /// <paramref name="runOnUi"/>, and streams <c>GET /v1/events</c> as Server-Sent Events.
    /// </summary>
    public sealed class LocalApiServer : IDisposable
    {
        private const int MaxEventClients = 8;
        private static readonly TimeSpan KeepAlive = TimeSpan.FromSeconds(15);

        private readonly ReadingHost host;
        private readonly string token;
        private readonly Func<Func<Task<ApiResponse>>, Task<ApiResponse>> runOnUi;
        private readonly CancellationTokenSource stopping = new();
        private readonly List<Channel<string>> eventClients = new();
        private HttpListener? listener;
        private ApiRouter? router;

        /// <param name="runOnUi">Runs the given work on the UI thread and returns its result (tests pass <c>work =&gt; work()</c>).</param>
        public LocalApiServer(ReadingHost host, string token, Func<Func<Task<ApiResponse>>, Task<ApiResponse>> runOnUi)
        {
            this.host = host;
            this.token = token;
            this.runOnUi = runOnUi;
        }

        public int Port { get; private set; }
        public bool IsRunning => listener?.IsListening == true;

        /// <summary>
        /// Starts listening on <paramref name="preferredPort"/>, or on a free port if that one is taken
        /// (<see cref="Port"/> says which). Call on the UI thread (it subscribes to host events).
        /// </summary>
        public void Start(int preferredPort)
        {
            Exception? last = null;
            foreach (int port in new[] { preferredPort, FreePort() })
            {
                var candidate = new HttpListener();
                candidate.Prefixes.Add($"http://127.0.0.1:{port}/");
                try
                {
                    candidate.Start();
                    listener = candidate;
                    Port = port;
                    break;
                }
                catch (Exception ex) when (ex is HttpListenerException or SocketException)
                {
                    last = ex;
                    candidate.Close();
                }
            }
            if (listener == null)
            {
                throw new InvalidOperationException("Could not open a local port for the API: " + last?.Message, last);
            }

            router = new ApiRouter(host, new ApiSecurity(token, Port));
            host.Event += OnHostEvent;
            _ = Task.Run(AcceptLoopAsync);
        }

        private static int FreePort()
        {
            var probe = new TcpListener(IPAddress.Loopback, 0);
            probe.Start();
            int port = ((IPEndPoint)probe.LocalEndpoint).Port;
            probe.Stop();
            return port;
        }

        private async Task AcceptLoopAsync()
        {
            while (listener is { IsListening: true } && !stopping.IsCancellationRequested)
            {
                HttpListenerContext context;
                try
                {
                    context = await listener.GetContextAsync();
                }
                catch (Exception)
                {
                    return; // stopped
                }
                _ = Task.Run(() => HandleAsync(context));
            }
        }

        private async Task HandleAsync(HttpListenerContext context)
        {
            try
            {
                var request = await ReadRequestAsync(context.Request);
                if (request.Method == "GET" && request.Path.TrimEnd('/') == "/v1/events")
                {
                    await StreamEventsAsync(context, request);
                    return;
                }
                var response = request.Body is { Length: > ApiRouter.MaxBodyChars }
                    ? ApiSecurity.Error(413, "too_large", "Request body is too large.")
                    : await runOnUi(() => router!.HandleAsync(request));
                await WriteAsync(context.Response, response);
            }
            catch (Exception)
            {
                try { context.Response.Abort(); } catch (Exception) { /* connection already gone */ }
            }
        }

        private static async Task<ApiRequest> ReadRequestAsync(HttpListenerRequest req)
        {
            var headers = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (string? key in req.Headers.AllKeys)
            {
                if (key != null)
                {
                    headers[key] = req.Headers[key] ?? "";
                }
            }

            string? body = null;
            if (req.HasEntityBody)
            {
                // Read one char past the limit so an oversized body is noticed without buffering all of it.
                using var reader = new StreamReader(req.InputStream, Encoding.UTF8);
                var buffer = new char[4096];
                var sb = new StringBuilder();
                while (sb.Length <= ApiRouter.MaxBodyChars)
                {
                    int n = await reader.ReadAsync(buffer, 0, buffer.Length);
                    if (n == 0) break;
                    sb.Append(buffer, 0, n);
                }
                body = sb.ToString();
            }
            return new ApiRequest(req.HttpMethod, req.Url!.AbsolutePath, req.Url.Query, headers, body);
        }

        private static async Task WriteAsync(HttpListenerResponse res, ApiResponse response)
        {
            byte[] bytes = Encoding.UTF8.GetBytes(response.Json ?? "");
            res.StatusCode = response.Status;
            res.ContentType = "application/json; charset=utf-8";
            res.Headers["Cache-Control"] = "no-store";
            res.ContentLength64 = bytes.Length;
            await res.OutputStream.WriteAsync(bytes);
            res.Close();
        }

        // ---------- events (SSE) ----------

        private void OnHostEvent(HostEvent e)
        {
            // Runs on the UI thread: take the snapshot here, send from the connection tasks.
            Channel<string>[] clients;
            lock (eventClients)
            {
                if (eventClients.Count == 0) return;
                clients = eventClients.ToArray();
            }
            string json = JsonSerializer.Serialize(new { type = e.Type, sessionId = e.SessionId, detail = e.Detail, status = host.Snapshot() }, ApiJson.Options);
            string frame = $"event: {e.Type}\ndata: {json}\n\n";
            foreach (var c in clients)
            {
                c.Writer.TryWrite(frame);
            }
        }

        private async Task StreamEventsAsync(HttpListenerContext context, ApiRequest request)
        {
            var denied = router!.Authorize(request);
            if (denied != null)
            {
                await WriteAsync(context.Response, denied);
                return;
            }

            var channel = Channel.CreateBounded<string>(new BoundedChannelOptions(64) { FullMode = BoundedChannelFullMode.DropOldest });
            lock (eventClients)
            {
                if (eventClients.Count >= MaxEventClients)
                {
                    _ = WriteAsync(context.Response, ApiSecurity.Error(429, "too_many_streams", "Too many event streams are open."));
                    return;
                }
                eventClients.Add(channel);
            }

            var res = context.Response;
            try
            {
                res.StatusCode = 200;
                res.ContentType = "text/event-stream";
                res.Headers["Cache-Control"] = "no-store";
                res.SendChunked = true;
                await SendAsync(res, ": connected\n\n");
                // First frame: where things are right now, so clients need no separate status call.
                var firstResponse = await runOnUi(() =>
                    Task.FromResult(new ApiResponse(200, JsonSerializer.Serialize(new { type = "state", status = host.Snapshot() }, ApiJson.Options))));
                string first = firstResponse.Json!;
                await SendAsync(res, $"event: state\ndata: {first}\n\n");

                while (!stopping.IsCancellationRequested)
                {
                    using var wait = CancellationTokenSource.CreateLinkedTokenSource(stopping.Token);
                    wait.CancelAfter(KeepAlive);
                    try
                    {
                        string frame = await channel.Reader.ReadAsync(wait.Token);
                        await SendAsync(res, frame);
                    }
                    catch (OperationCanceledException) when (!stopping.IsCancellationRequested)
                    {
                        await SendAsync(res, ": keep-alive\n\n");
                    }
                }
            }
            catch (Exception)
            {
                // client went away
            }
            finally
            {
                lock (eventClients) { eventClients.Remove(channel); }
                try { res.Close(); } catch (Exception) { /* already closed */ }
            }
        }

        private static async Task SendAsync(HttpListenerResponse res, string text)
        {
            byte[] bytes = Encoding.UTF8.GetBytes(text);
            await res.OutputStream.WriteAsync(bytes);
            await res.OutputStream.FlushAsync();
        }

        public void Dispose()
        {
            host.Event -= OnHostEvent;
            stopping.Cancel();
            try { listener?.Stop(); listener?.Close(); } catch (Exception) { /* already stopped */ }
            listener = null;
        }
    }
}
