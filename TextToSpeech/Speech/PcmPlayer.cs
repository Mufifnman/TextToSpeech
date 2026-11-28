using System.IO;
using NAudio.Wave;
using NAudio.Wave.SampleProviders;

namespace TextToSpeech.Speech
{
    /// <summary>
    /// Plays speech through one output device that stays open between sentences (and closes after a few idle
    /// seconds), so the next sentence starts at once instead of waiting for the device to open.
    /// A sentence arrives as chunks that are appended as they are generated; if a chunk is late the output is
    /// silent until it arrives. Supports pause/resume, live volume, and word callbacks driven by how much audio
    /// has actually played.
    /// </summary>
    internal sealed class PcmPlayer : IDisposable
    {
        private const int DeviceLatencyMs = 120;
        private const int IdleCloseMs = 5000;

        private readonly object gate = new();
        private WaveOutEvent? output;
        private Feed? feed;
        private int outputRate;
        private bool paused;
        private System.Threading.Timer? idleTimer;

        public bool IsPaused { get { lock (gate) { return paused; } } }

        private volatile float gain = 1f;

        /// <summary>Volume multiplier, applied live (1.0 = as generated; above 1.0 is soft-limited).</summary>
        public float Gain { get => gain; set => gain = value; }

        /// <summary>
        /// Plays <paramref name="chunkCount"/> chunks as one continuous clip. <paramref name="getChunk"/> returns each chunk's
        /// samples (it may take a while); <paramref name="wordsFor"/> gives the words of a chunk with start times relative to that chunk.
        /// Completes when the last sample has played. Throws <see cref="OperationCanceledException"/> if cancelled.
        /// </summary>
        public async Task PlayAsync(int sampleRate, int chunkCount, Func<int, Task<float[]>> getChunk,
            Func<int, float[], IReadOnlyList<TimedWord>> wordsFor, Action<WordBoundary>? onWord, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var clip = new Clip();
            var words = new List<TimedWord>();
            Feed currentFeed = Attach(sampleRate, clip);
            try
            {
                using (cancellationToken.Register(() => { currentFeed.Detach(clip); }))
                {
                    var feeder = Task.Run(async () =>
                    {
                        try
                        {
                            double startSeconds = 0;
                            for (int i = 0; i < chunkCount; i++)
                            {
                                float[] samples = await getChunk(i).ConfigureAwait(false);
                                var chunkWords = wordsFor(i, samples);
                                lock (words)
                                {
                                    foreach (var w in chunkWords)
                                    {
                                        words.Add(new TimedWord(startSeconds + w.StartSeconds, w.Boundary));
                                    }
                                }
                                startSeconds += samples.Length / (double)sampleRate;
                                clip.Append(samples);
                            }
                            clip.Complete();
                        }
                        catch (Exception ex)
                        {
                            clip.Fail(ex);
                        }
                    });

                    int next = 0;
                    while (!clip.Finished.IsCompleted)
                    {
                        double seconds = Math.Max(0, clip.PlayedSamples / (double)sampleRate - DeviceLatencyMs / 1000.0);
                        lock (words)
                        {
                            while (next < words.Count && words[next].StartSeconds <= seconds)
                            {
                                onWord?.Invoke(words[next].Boundary);
                                next++;
                            }
                        }
                        await Task.WhenAny(clip.Finished, Task.Delay(30)).ConfigureAwait(false);
                    }
                    await clip.Finished.ConfigureAwait(false);   // surfaces a failed chunk
                    cancellationToken.ThrowIfCancellationRequested();
                    await feeder.ConfigureAwait(false);
                    // The last samples are still in the device's buffer: wait for them, so the pause between sentences starts when speech really ends.
                    await Task.Delay(DeviceLatencyMs, cancellationToken).ConfigureAwait(false);
                }
            }
            finally
            {
                currentFeed.Detach(clip);
                ScheduleIdleClose();
            }
            cancellationToken.ThrowIfCancellationRequested();
        }

        public void Pause()
        {
            lock (gate)
            {
                if (output != null && feed?.HasClip == true && output.PlaybackState == PlaybackState.Playing) { output.Pause(); paused = true; }
            }
        }

        public void Resume()
        {
            lock (gate)
            {
                if (output?.PlaybackState == PlaybackState.Paused) { output.Play(); paused = false; }
            }
        }

        public void Dispose()
        {
            lock (gate)
            {
                idleTimer?.Dispose();
                idleTimer = null;
                CloseOutput();
            }
        }

        /// <summary>Makes sure the device is open at this sample rate and routes the clip to it.</summary>
        private Feed Attach(int sampleRate, Clip clip)
        {
            lock (gate)
            {
                idleTimer?.Change(Timeout.Infinite, Timeout.Infinite);
                if (output == null || feed == null || outputRate != sampleRate)
                {
                    CloseOutput();
                    feed = new Feed(sampleRate, () => gain);
                    var wave = new WaveOutEvent { DesiredLatency = DeviceLatencyMs };
                    wave.Init(new SampleToWaveProvider16(feed));
                    wave.Play();
                    output = wave;
                    outputRate = sampleRate;
                }
                else if (output.PlaybackState == PlaybackState.Paused)
                {
                    output.Play();
                }
                paused = false;
                feed.Attach(clip);
                return feed;
            }
        }

        private void ScheduleIdleClose()
        {
            lock (gate)
            {
                if (output == null) return;
                idleTimer ??= new System.Threading.Timer(_ => CloseIfIdle());
                idleTimer.Change(IdleCloseMs, Timeout.Infinite);
            }
        }

        private void CloseIfIdle()
        {
            lock (gate)
            {
                if (feed != null && !feed.HasClip) CloseOutput();
            }
        }

        private void CloseOutput()
        {
            var wave = output;
            output = null;
            feed = null;
            paused = false;
            if (wave != null)
            {
                try { wave.Stop(); } catch (Exception) { }
                wave.Dispose();
            }
        }

        /// <summary>The audio of one sentence: chunks arrive over time; reading drains them.</summary>
        private sealed class Clip
        {
            private readonly object g = new();
            private readonly Queue<float[]> queue = new();
            private readonly TaskCompletionSource finished = new(TaskCreationOptions.RunContinuationsAsynchronously);
            private float[]? current;
            private int position;
            private long played;
            private bool complete;

            public Task Finished => finished.Task;

            public long PlayedSamples { get { lock (g) { return played; } } }

            public void Append(float[] samples) { lock (g) { queue.Enqueue(samples); } }

            public void Complete() { lock (g) { complete = true; } }

            public void Fail(Exception ex) => finished.TrySetException(ex);

            public void Stop() => finished.TrySetResult();

            /// <summary>Copies up to <paramref name="count"/> samples; returns how many were real audio.</summary>
            public int Read(float[] buffer, int offset, int count)
            {
                lock (g)
                {
                    int n = 0;
                    while (n < count)
                    {
                        if (current == null || position >= current.Length)
                        {
                            if (!queue.TryDequeue(out current)) { current = null; break; }
                            position = 0;
                            continue;
                        }
                        int take = Math.Min(count - n, current.Length - position);
                        Array.Copy(current, position, buffer, offset + n, take);
                        position += take;
                        n += take;
                    }
                    played += n;
                    if (n < count && current == null && complete && queue.Count == 0)
                    {
                        finished.TrySetResult();   // everything has been handed to the device
                    }
                    return n;
                }
            }
        }

        /// <summary>What the device reads: the attached clip with volume applied, or silence. Never ends, so the device stays running.</summary>
        private sealed class Feed : ISampleProvider
        {
            private readonly Func<float> targetGain;
            private readonly float smoothing;
            private Clip? clip;
            private float currentGain;

            public Feed(int sampleRate, Func<float> targetGain, double rampSeconds = 0.03)
            {
                this.targetGain = targetGain;
                WaveFormat = WaveFormat.CreateIeeeFloatWaveFormat(sampleRate, 1);
                smoothing = (float)(1.0 - Math.Exp(-1.0 / Math.Max(1.0, rampSeconds * sampleRate)));
                currentGain = targetGain();
            }

            public WaveFormat WaveFormat { get; }

            public bool HasClip => Volatile.Read(ref clip) != null;

            public void Attach(Clip next)
            {
                var old = Interlocked.Exchange(ref clip, next);
                old?.Stop();   // a new sentence replaces an unfinished one
            }

            public void Detach(Clip which)
            {
                Interlocked.CompareExchange(ref clip, null, which);
                which.Stop();
            }

            public int Read(float[] buffer, int offset, int count)
            {
                var c = Volatile.Read(ref clip);
                int n = c?.Read(buffer, offset, count) ?? 0;
                float target = targetGain();
                for (int i = 0; i < n; i++)
                {
                    currentGain += (target - currentGain) * smoothing;
                    buffer[offset + i] = AudioGain.Apply(buffer[offset + i], currentGain);
                }
                if (n < count) Array.Clear(buffer, offset + n, count - n);
                return count;
            }
        }
    }
}
