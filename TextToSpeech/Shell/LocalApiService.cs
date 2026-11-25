using System.Windows.Threading;
using TextToSpeech.Api;
using TextToSpeech.Core.Reading;

namespace TextToSpeech.Shell
{
    /// <summary>
    /// Owns the local API while it is switched on: the host on the reading controller, the loopback server, a fresh
    /// token each start and the <c>api.json</c> file that local tools read to find the port and token.
    /// </summary>
    internal sealed class LocalApiService : IDisposable
    {
        private readonly ReadingController reading;
        private readonly IAppControl app;
        private readonly Dispatcher dispatcher;
        private readonly DispatcherTimer tick;
        private LocalApiServer? server;

        public LocalApiService(ReadingController reading, IAppControl app, Dispatcher dispatcher)
        {
            this.reading = reading;
            this.app = app;
            this.dispatcher = dispatcher;
            Host = new ReadingHost(reading, app);
            tick = new DispatcherTimer(TimeSpan.FromSeconds(5), DispatcherPriority.Background, (_, _) => Host.Tick(), dispatcher);
            tick.Stop();
        }

        public ReadingHost Host { get; }
        public bool IsRunning => server?.IsRunning == true;
        public int Port => server?.Port ?? 0;
        public string? Token { get; private set; }

        /// <summary>Starts listening. Throws if no local port can be opened. Call on the UI thread.</summary>
        public void Start(int preferredPort)
        {
            if (IsRunning)
            {
                return;
            }
            Token = ApiSecurity.GenerateToken();
            var s = new LocalApiServer(Host, Token, work => dispatcher.InvokeAsync(work).Task.Unwrap());
            s.Start(preferredPort);
            server = s;
            new ConnectionInfo(s.Port, Token, Environment.ProcessId).Write();
            tick.Start();
        }

        public void Stop()
        {
            tick.Stop();
            server?.Dispose();
            server = null;
            Token = null;
            ConnectionInfo.Delete();
        }

        public void Dispose() => Stop();
    }
}
