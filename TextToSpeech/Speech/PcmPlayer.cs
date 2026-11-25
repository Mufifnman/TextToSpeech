using System.IO;
using NAudio.Wave;
using NAudio.Wave.SampleProviders;

namespace TextToSpeech.Speech
{
    /// <summary>
    /// Plays a mono float clip through the default output device, supports pause/resume,
    /// and fires word callbacks by watching the playback position.
    /// </summary>
    internal sealed class PcmPlayer
    {
        private readonly object gate = new();
        private WaveOutEvent? output;
        private bool paused;

        public bool IsPaused { get { lock (gate) { return paused; } } }

        private volatile float gain = 1f;

        /// <summary>Volume multiplier, applied live while a clip plays (1.0 = as generated; above 1.0 is soft-limited).</summary>
        public float Gain { get => gain; set => gain = value; }

        public async Task PlayAsync(float[] samples, int sampleRate, IReadOnlyList<TimedWord> words,
            Action<WordBoundary>? onWord, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();

            // Gain is read as the clip plays, so the volume slider works live (with a short ramp, no pops).
            var format = new WaveFormat(sampleRate, 16, 1);
            var provider = new GainSampleProvider(samples, sampleRate, () => gain);
            using var wave = new WaveOutEvent { DesiredLatency = 120 };
            var stopped = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            wave.PlaybackStopped += (_, e) =>
            {
                if (e.Exception != null) stopped.TrySetException(e.Exception);
                else stopped.TrySetResult();
            };
            wave.Init(new SampleToWaveProvider16(provider));

            lock (gate) { output = wave; paused = false; }
            try
            {
                using (cancellationToken.Register(() => { try { wave.Stop(); } catch (Exception) { } }))
                {
                    wave.Play();
                    int next = 0;
                    while (!stopped.Task.IsCompleted)
                    {
                        double seconds = wave.GetPosition() / (double)format.AverageBytesPerSecond;
                        while (next < words.Count && words[next].StartSeconds <= seconds)
                        {
                            onWord?.Invoke(words[next].Boundary);
                            next++;
                        }
                        await Task.WhenAny(stopped.Task, Task.Delay(30));
                    }
                    await stopped.Task; // surface device errors
                }
            }
            finally
            {
                lock (gate) { if (output == wave) { output = null; paused = false; } }
            }
            cancellationToken.ThrowIfCancellationRequested();
        }

        public void Pause()
        {
            lock (gate)
            {
                if (output?.PlaybackState == PlaybackState.Playing) { output.Pause(); paused = true; }
            }
        }

        public void Resume()
        {
            lock (gate)
            {
                if (output?.PlaybackState == PlaybackState.Paused) { output.Play(); paused = false; }
            }
        }
    }
}
