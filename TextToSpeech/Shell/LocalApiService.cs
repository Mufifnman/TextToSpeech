using System.Windows.Threading;
using TextToSpeech.Api;
using TextToSpeech.Api.Arbitration;
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
        private readonly DispatcherTimer focusPoll;
        private LocalApiServer? server;

        public LocalApiService(ReadingController reading, IAppControl app, Dispatcher dispatcher, Func<ReadPolicy> policy)
        {
            this.reading = reading;
            this.app = app;
            this.dispatcher = dispatcher;
            Host = new ReadingHost(reading, app);
            Agents = new ArbitratedHost(Host, new WindowsEnvironmentProbe(), policy);
            // The turn is over when the reading list runs dry. Handled after the current callback so the next
            // agent's text is not added from inside the reader's own "finished" notification.
            Host.Event += e => { if (e.Type == "finished") dispatcher.BeginInvoke(new Action(Agents.OnReadingFinished)); };
            focusPoll = new DispatcherTimer(TimeSpan.FromSeconds(1), DispatcherPriority.Background, (_, _) => Agents.Poll(), dispatcher);
            focusPoll.Stop();
            tick = new DispatcherTimer(TimeSpan.FromSeconds(5), DispatcherPriority.Background, (_, _) => Host.Tick(), dispatcher);
            tick.Stop();
        }

        public ReadingHost Host { get; }

        /// <summary>What the API talks to: the host plus agent policy and turn taking.</summary>
        public ArbitratedHost Agents { get; }
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
            var s = new LocalApiServer(Host, Token, work => dispatcher.InvokeAsync(work).Task.Unwrap(), Agents);
            s.Start(preferredPort);
            server = s;
            new ConnectionInfo(s.Port, Token, Environment.ProcessId).Write();
            tick.Start();
            focusPoll.Start();
        }

        public void Stop()
        {
            tick.Stop();
            focusPoll.Stop();
            server?.Dispose();
            server = null;
            Token = null;
            ConnectionInfo.Delete();
        }

        public void Dispose() => Stop();
    }
}
