using NAudio.Wave;

namespace TextToSpeech.Speech
{
    /// <summary>
    /// Plays a clip of float samples with a gain that can change while it plays. The gain moves toward
    /// its target over a few milliseconds instead of jumping, so dragging the volume never clicks or pops.
    /// Above 1.0 the result is soft-limited by <see cref="AudioGain"/>.
    /// </summary>
    public sealed class GainSampleProvider : ISampleProvider
    {
        private readonly float[] samples;
        private readonly Func<float> targetGain;
        private readonly float smoothing;
        private int position;
        private float currentGain;

        /// <param name="rampSeconds">Roughly how long a gain change takes to settle.</param>
        public GainSampleProvider(float[] samples, int sampleRate, Func<float> targetGain, double rampSeconds = 0.03)
        {
            this.samples = samples;
            this.targetGain = targetGain;
            WaveFormat = WaveFormat.CreateIeeeFloatWaveFormat(sampleRate, 1);
            smoothing = (float)(1.0 - Math.Exp(-1.0 / Math.Max(1.0, rampSeconds * sampleRate)));
            currentGain = targetGain(); // start at the right level: no fade-in at the start of a clip
        }

        public WaveFormat WaveFormat { get; }

        public int Read(float[] buffer, int offset, int count)
        {
            int n = Math.Min(count, samples.Length - position);
            float target = targetGain();
            for (int i = 0; i < n; i++)
            {
                currentGain += (target - currentGain) * smoothing;
                buffer[offset + i] = AudioGain.Apply(samples[position + i], currentGain);
            }
            position += n;
            return n;
        }
    }
}
