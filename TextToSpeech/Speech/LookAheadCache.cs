namespace TextToSpeech.Speech
{
    /// <summary>
    /// Starts work for a key once and hands every later caller the same task, keeping only the newest few
    /// entries. Used to generate the next sentences' audio while the current one is still playing.
    /// </summary>
    public sealed class LookAheadCache<T>
    {
        private readonly object gate = new();
        private readonly Dictionary<string, Task<T>> items = new();
        private readonly List<string> order = new();
        private readonly int maxEntries;

        public LookAheadCache(int maxEntries) => this.maxEntries = Math.Max(1, maxEntries);

        public int Count { get { lock (gate) { return items.Count; } } }

        public bool Contains(string key) { lock (gate) { return items.ContainsKey(key); } }

        /// <summary>The running or finished task for <paramref name="key"/>, starting <paramref name="create"/> on the thread pool if there is none.</summary>
        public Task<T> GetOrStart(string key, Func<T> create)
        {
            lock (gate)
            {
                if (items.TryGetValue(key, out var existing))
                {
                    return existing;
                }
                var task = Task.Run(create);
                items[key] = task;
                order.Add(key);
                while (order.Count > maxEntries)
                {
                    items.Remove(order[0]);   // the oldest is dropped (work already running just finishes unused)
                    order.RemoveAt(0);
                }
                return task;
            }
        }

        public void Remove(string key)
        {
            lock (gate)
            {
                items.Remove(key);
                order.Remove(key);
            }
        }

        public void Clear()
        {
            lock (gate)
            {
                items.Clear();
                order.Clear();
            }
        }
    }
}
