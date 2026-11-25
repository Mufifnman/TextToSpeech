namespace TextToSpeech.Shell
{
    /// <summary>
    /// Only one copy of the app runs. A second launch wakes the first (it shows its window) and exits.
    /// A named mutex says who is first; a named event carries the "show yourself" signal.
    /// </summary>
    internal sealed class SingleInstance : IDisposable
    {
        private const string MutexName = @"Local\TextToSpeech.Single";
        private const string EventName = @"Local\TextToSpeech.Show";

        private readonly Mutex mutex;
        private readonly EventWaitHandle showEvent;
        private RegisteredWaitHandle? registration;

        private SingleInstance(Mutex mutex, EventWaitHandle showEvent)
        {
            this.mutex = mutex;
            this.showEvent = showEvent;
        }

        /// <summary>Raised on a thread-pool thread when another launch asked this one to show itself.</summary>
        public event Action? ShowRequested;

        /// <summary>The first instance gets an object back; any later one gets null (and should call <see cref="SignalExisting"/>).</summary>
        public static SingleInstance? TryAcquire()
        {
            var mutex = new Mutex(initiallyOwned: true, MutexName, out bool createdNew);
            if (!createdNew)
            {
                mutex.Dispose();
                return null;
            }
            return new SingleInstance(mutex, new EventWaitHandle(false, EventResetMode.AutoReset, EventName));
        }

        public static void SignalExisting()
        {
            try
            {
                using var existing = EventWaitHandle.OpenExisting(EventName);
                existing.Set();
            }
            catch (Exception)
            {
                // The first instance is gone or still starting; nothing to wake.
            }
        }

        public void Listen() =>
            registration = ThreadPool.RegisterWaitForSingleObject(showEvent, (_, _) => ShowRequested?.Invoke(), null, Timeout.Infinite, executeOnlyOnce: false);

        public void Dispose()
        {
            registration?.Unregister(null);
            showEvent.Dispose();
            mutex.Dispose();
        }
    }
}
