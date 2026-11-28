namespace TextToSpeech.Speech
{
    /// <summary>
    /// Runs speech-generation jobs one at a time (the model can only do one at once) in a sensible order:
    /// 1. <b>Demanded</b> work (the sentence about to be spoken) always goes first.
    /// 2. <b>Wanted</b> work at priority 1 (the next sentences, in reading order) goes next, but only while less
    ///    than <c>budgetSeconds</c> of finished audio is waiting, so it never builds up without bound.
    /// 3. <b>Wanted</b> work at priority 2 (a guess, such as the rest of a sentence after a pause) or 3 (a hint from an API caller) runs only when
    ///    nothing else needs doing, and is thrown away as soon as it is no longer wanted.
    /// Work that is no longer wanted and has not started is cancelled. Work already running cannot be stopped, but
    /// its result is discarded and nothing else starts until it ends.
    /// Thread safe. Has no knowledge of any speech engine, so it can be tested with plain functions.
    /// </summary>
    /// <summary>How well look-ahead is working. "Hit" means the audio was finished before it was asked for.</summary>
    public sealed record SynthesisStats(
        int DemandsReady,        // asked for and already finished: no wait
        int DemandsRunning,      // asked for while queued or running: waited for the rest
        int DemandsFresh,        // not prepared at all: waited for all of it
        int GuessesUsed,         // guesses and hints (priority 2 and 3) that were then demanded
        int GuessesWasted,       // guesses and hints dropped without being used
        int NextWasted)          // priority 1 audio dropped without being used (a jump or clear)
    {
        public int Demands => DemandsReady + DemandsRunning + DemandsFresh;
        public override string ToString() =>
            $"ready {DemandsReady}, partly {DemandsRunning}, fresh {DemandsFresh}; guesses used {GuessesUsed}, wasted {GuessesWasted}; next-sentence audio wasted {NextWasted}";
    }

    public sealed class SynthesisQueue<T>
    {
        private int demandsReady, demandsRunning, demandsFresh, guessesUsed, guessesWasted, nextWasted;

        public SynthesisStats Stats
        {
            get
            {
                lock (gate)
                {
                    return new SynthesisStats(demandsReady, demandsRunning, demandsFresh, guessesUsed, guessesWasted, nextWasted);
                }
            }
        }

        public void ResetStats()
        {
            lock (gate) { demandsReady = demandsRunning = demandsFresh = guessesUsed = guessesWasted = nextWasted = 0; }
        }

        private sealed class Job
        {
            public required string Key { get; init; }
            public required Func<T> Work { get; init; }
            public required TaskCompletionSource<T> Tcs { get; init; }
            public int Origin;       // the priority it was first queued at (0, 1 or 2), for the statistics
            public bool Demanded;    // someone asked for its result
            public int Priority;     // 0 demanded, 1 next sentences, 2 guesses
            public long Order;       // lower runs first within a priority
            public bool Running;
            public bool Counted;     // its finished audio counts toward the budget
            public double Seconds;
        }

        private readonly object gate = new();
        private readonly Dictionary<string, Job> jobs = new();
        private readonly List<Job> pending = new();
        private readonly double budgetSeconds;
        private readonly Func<T, double> measureSeconds;
        private double readySeconds;
        private long order;
        private bool pumping;

        /// <param name="budgetSeconds">Stop starting priority 1 and 2 work once this much finished audio is waiting.</param>
        /// <param name="measureSeconds">How many seconds of audio a result holds.</param>
        public SynthesisQueue(double budgetSeconds, Func<T, double> measureSeconds)
        {
            this.budgetSeconds = budgetSeconds;
            this.measureSeconds = measureSeconds;
        }

        /// <summary>Jobs waiting, running or finished and not yet taken.</summary>
        public int Count { get { lock (gate) { return jobs.Count; } } }

        /// <summary>Seconds of finished next-sentence audio waiting to be used.</summary>
        public double ReadySeconds { get { lock (gate) { return readySeconds; } } }

        public bool Contains(string key) { lock (gate) { return jobs.ContainsKey(key); } }

        /// <summary>
        /// The result for <paramref name="key"/>, now at top priority. Reuses a running, finished or queued job for
        /// the same key. Call <see cref="Take"/> afterwards to release it.
        /// </summary>
        public Task<T> Demand(string key, Func<T> work)
        {
            Task<T> task;
            lock (gate)
            {
                if (jobs.TryGetValue(key, out var existing))
                {
                    if (existing.Tcs.Task.IsCompleted) demandsReady++; else demandsRunning++;
                    if (existing.Origin >= 2) guessesUsed++;
                    existing.Demanded = true;
                    if (!existing.Running && pending.Contains(existing))
                    {
                        existing.Priority = 0;
                        existing.Order = ++order;
                        Sort();
                    }
                    task = existing.Tcs.Task;
                }
                else
                {
                    demandsFresh++;
                    var job = NewJob(key, work, 0);
                    job.Demanded = true;
                    jobs[key] = job;
                    pending.Add(job);
                    Sort();
                    task = job.Tcs.Task;
                }
                Kick();
            }
            return task;
        }

        /// <summary>
        /// Replaces everything wanted at <paramref name="priority"/> (1, 2 or 3) with <paramref name="items"/>, in order.
        /// Items no longer listed are cancelled, or their finished audio dropped.
        /// </summary>
        public void SetWanted(int priority, IReadOnlyList<(string Key, Func<T> Work)> items)
        {
            if (priority < 1) throw new ArgumentOutOfRangeException(nameof(priority));
            lock (gate)
            {
                var keep = new HashSet<string>(items.Select(i => i.Key));
                // A job someone is waiting for (Demanded) is never dropped here, or its waiter would be cancelled; Take releases it.
                foreach (var job in jobs.Values.Where(j => j.Priority == priority && !j.Demanded && !keep.Contains(j.Key)).ToList())
                {
                    Drop(job);
                }
                foreach (var (key, work) in items)
                {
                    if (jobs.TryGetValue(key, out var existing))
                    {
                        if (existing.Priority == priority && !existing.Running && pending.Contains(existing))
                        {
                            existing.Order = ++order;   // keep the caller's order
                        }
                        else if (existing.Priority > priority && !existing.Running && pending.Contains(existing))
                        {
                            existing.Priority = priority;   // a guess became a real need
                            existing.Order = ++order;
                        }
                    }
                    else
                    {
                        var job = NewJob(key, work, priority);
                        jobs[key] = job;
                        pending.Add(job);
                    }
                }
                Sort();
                Kick();
            }
        }

        /// <summary>Releases a job's result (the caller has it), making room under the budget for more work.</summary>
        public void Take(string key)
        {
            lock (gate)
            {
                if (jobs.TryGetValue(key, out var job))
                {
                    Drop(job);
                    Kick();
                }
            }
        }

        /// <summary>Forgets everything. Work already running finishes unused.</summary>
        public void Clear()
        {
            lock (gate)
            {
                foreach (var job in jobs.Values.ToList())
                {
                    Drop(job);
                }
                readySeconds = 0;
            }
        }

        private Job NewJob(string key, Func<T> work, int priority) => new()
        {
            Key = key,
            Work = work,
            Origin = priority,
            Priority = priority,
            Order = ++order,
            Tcs = new TaskCompletionSource<T>(TaskCreationOptions.RunContinuationsAsynchronously),
        };

        private void Sort() => pending.Sort((a, b) => a.Priority != b.Priority ? a.Priority.CompareTo(b.Priority) : a.Order.CompareTo(b.Order));

        /// <summary>Removes a job (caller holds the gate). A running one is only forgotten, so its result is discarded.</summary>
        private void Drop(Job job)
        {
            jobs.Remove(job.Key);
            pending.Remove(job);
            if (!job.Demanded && job.Tcs.Task.Status != TaskStatus.Canceled)
            {
                if (job.Origin >= 2) guessesWasted++;
                else if (job.Origin == 1) nextWasted++;
            }
            if (job.Counted)
            {
                readySeconds = Math.Max(0, readySeconds - job.Seconds);
                job.Counted = false;
            }
            job.Tcs.TrySetCanceled();
        }

        private bool HasRunnable() =>
            pending.Count > 0 && (pending[0].Priority == 0 || readySeconds < budgetSeconds);

        /// <summary>Starts the single worker if there is something it may run (caller holds the gate).</summary>
        private void Kick()
        {
            if (!pumping && HasRunnable())
            {
                pumping = true;
                _ = Task.Run(Pump);
            }
        }

        private void Pump()
        {
            while (true)
            {
                Job job;
                lock (gate)
                {
                    if (!HasRunnable())
                    {
                        pumping = false;   // decided under the gate, so a later Kick cannot be missed
                        return;
                    }
                    job = pending[0];
                    pending.RemoveAt(0);
                    job.Running = true;
                }

                T result = default!;
                Exception? error = null;
                try { result = job.Work(); }
                catch (Exception ex) { error = ex; }

                lock (gate)
                {
                    job.Running = false;
                    if (!jobs.TryGetValue(job.Key, out var current) || !ReferenceEquals(current, job))
                    {
                        continue;   // dropped while running: the result is not wanted
                    }
                    if (error != null)
                    {
                        jobs.Remove(job.Key);
                        job.Tcs.TrySetException(error);
                    }
                    else
                    {
                        if (job.Priority == 1)
                        {
                            job.Seconds = measureSeconds(result);
                            job.Counted = true;
                            readySeconds += job.Seconds;
                        }
                        job.Tcs.TrySetResult(result);
                    }
                }
            }
        }
    }
}
