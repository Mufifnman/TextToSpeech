using TextToSpeech.Speech;

namespace TextToSpeech.Core.Tests
{
    public class SynthesisQueueTests
    {
        private static SynthesisQueue<int> NewQueue(double budget = 100) => new(budget, seconds => seconds);

        private static async Task<bool> Eventually(Func<bool> condition)
        {
            for (int i = 0; i < 200 && !condition(); i++) await Task.Delay(10);
            return condition();
        }

        [Fact]
        public async Task WantedItemsRunInReadingOrder_OneAtATime()
        {
            var q = NewQueue();
            var started = new List<string>();
            int active = 0, maxActive = 0;
            Func<string, Func<int>> work = name => () =>
            {
                lock (started) { started.Add(name); }
                int now = Interlocked.Increment(ref active);
                maxActive = Math.Max(maxActive, now);
                Thread.Sleep(15);
                Interlocked.Decrement(ref active);
                return 1;
            };

            q.SetWanted(1, new[] { ("a", work("a")), ("b", work("b")), ("c", work("c")) });
            Assert.Equal(1, await q.Demand("b", work("b")));   // already wanted: shared, not run twice
            Assert.True(await Eventually(() => { lock (started) { return started.Count == 3; } }));

            Assert.Equal(1, maxActive);
            Assert.Equal(3, started.Distinct().Count());
        }

        [Fact]
        public async Task DemandedWork_JumpsAheadOfQueuedWork()
        {
            var q = NewQueue();
            var release = new ManualResetEventSlim();
            var started = new ManualResetEventSlim();
            var order = new List<string>();
            Func<string, Func<int>> work = name => () => { lock (order) { order.Add(name); } return 1; };

            q.SetWanted(1, new (string, Func<int>)[]
            {
                ("first", () => { started.Set(); release.Wait(); lock (order) { order.Add("first"); } return 1; }),
                ("second", work("second")),
                ("third", work("third")),
            });
            Assert.True(started.Wait(5000));   // "first" is running now, so it cannot be overtaken
            var wanted = q.Demand("urgent", work("urgent"));
            release.Set();
            await wanted;

            Assert.True(await Eventually(() => { lock (order) { return order.Count == 4; } }));
            Assert.Equal(new[] { "first", "urgent", "second", "third" }, order);
        }

        [Fact]
        public async Task ItemsNoLongerWanted_AreCancelledBeforeTheyStart()
        {
            var q = NewQueue();
            var release = new ManualResetEventSlim();
            var started = new ManualResetEventSlim();
            int ranStale = 0;

            q.SetWanted(1, new (string, Func<int>)[]
            {
                ("blocker", () => { started.Set(); release.Wait(); return 1; }),
                ("stale", () => { Interlocked.Increment(ref ranStale); return 1; }),
            });
            Assert.True(started.Wait(5000));
            var stale = q.Demand("stale", () => 7);   // same key: shares the queued job (promoted)
            q.SetWanted(1, Array.Empty<(string, Func<int>)>());   // not wanted any more
            q.Clear();
            release.Set();

            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => stale);
            await Task.Delay(50);
            Assert.Equal(0, ranStale);
            Assert.Equal(0, q.Count);
        }

        [Fact]
        public async Task PrefetchStopsAtTheBudget_AndResumesWhenAudioIsTaken()
        {
            var q = new SynthesisQueue<int>(5, seconds => seconds);   // each result is 3 s of audio
            int runs = 0;
            Func<Func<int>> work = () => () => { Interlocked.Increment(ref runs); return 3; };

            q.SetWanted(1, new[] { ("a", work()), ("b", work()), ("c", work()), ("d", work()) });
            Assert.True(await Eventually(() => Volatile.Read(ref runs) == 2));
            await Task.Delay(60);
            Assert.Equal(2, Volatile.Read(ref runs));          // 6 s ready >= 5 s budget: no more started
            Assert.Equal(6, q.ReadySeconds);

            q.Take("a");
            Assert.True(await Eventually(() => Volatile.Read(ref runs) == 3));
        }

        [Fact]
        public async Task GuessesRunLast_AndAreDroppedWhenReplaced()
        {
            var q = NewQueue();
            var release = new ManualResetEventSlim();
            var started = new ManualResetEventSlim();
            var order = new List<string>();
            Func<string, Func<int>> work = name => () => { lock (order) { order.Add(name); } return 1; };

            q.SetWanted(1, new (string, Func<int>)[] { ("hold", () => { started.Set(); release.Wait(); return 1; }) });
            Assert.True(started.Wait(5000));
            q.SetWanted(2, new[] { ("guess1", work("guess1")) });
            q.SetWanted(1, new (string, Func<int>)[] { ("hold", () => 1), ("real", work("real")) });
            q.SetWanted(2, new[] { ("guess2", work("guess2")) });   // replaces guess1, which never started
            release.Set();

            Assert.True(await Eventually(() => { lock (order) { return order.Count == 2; } }));
            Assert.Equal(new[] { "real", "guess2" }, order);
            Assert.False(q.Contains("guess1"));
        }

        [Fact]
        public async Task AGuessThatBecomesNeeded_IsReusedWithoutRunningAgain()
        {
            var q = NewQueue();
            int runs = 0;
            Func<int> work = () => { Interlocked.Increment(ref runs); return 5; };

            q.SetWanted(2, new[] { ("rest", work) });
            Assert.True(await Eventually(() => Volatile.Read(ref runs) == 1));
            Assert.Equal(5, await q.Demand("rest", work));
            q.Take("rest");

            Assert.Equal(1, runs);
            Assert.Equal(0, q.Count);
        }

        [Fact]
        public async Task AFailedJob_ReportsTheErrorAndDoesNotStopTheQueue()
        {
            var q = NewQueue();
            var bad = q.Demand("bad", () => throw new InvalidOperationException("boom"));
            await Assert.ThrowsAsync<InvalidOperationException>(() => bad);

            Assert.Equal(2, await q.Demand("good", () => 2));
            Assert.False(q.Contains("bad"));
        }
    
        [Fact]
        public async Task Statistics_TellReadyFromWaitedFromFreshAndWastedGuesses()
        {
            var q = NewQueue();
            q.SetWanted(1, new[] { ("a", (Func<int>)(() => 1)) });
            Assert.True(await Eventually(() => q.ReadySeconds > 0));
            await q.Demand("a", () => 1);                 // finished ahead of time
            q.Take("a");
            await q.Demand("fresh", () => 1);             // nothing prepared
            q.Take("fresh");
            q.SetWanted(2, new[] { ("guess", (Func<int>)(() => 1)) });
            await Task.Delay(50);
            q.SetWanted(2, Array.Empty<(string, Func<int>)>());   // never used

            var stats = q.Stats;
            Assert.Equal(1, stats.DemandsReady);
            Assert.Equal(1, stats.DemandsFresh);
            Assert.Equal(1, stats.GuessesWasted);
            Assert.Equal(0, stats.GuessesUsed);
        }
    }
}
