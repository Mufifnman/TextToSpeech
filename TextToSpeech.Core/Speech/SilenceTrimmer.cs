namespace TextToSpeech.Speech
{
    /// <summary>
    /// Trims the near-silence a speech model leaves at the edges of a clip. Used where two pieces of one sentence
    /// are joined, so the pause at the join is the short one a comma asks for and not two models' worth of silence.
    /// </summary>
    public static class SilenceTrimmer
    {
        /// <summary>Quiet means below about -40 dB.</summary>
        public const float DefaultThreshold = 0.01f;

        /// <summary>
        /// Returns the clip with leading and/or trailing silence cut down to <paramref name="keepSeconds"/>.
        /// A clip with no sound above the threshold is returned unchanged.
        /// </summary>
        public static float[] Trim(float[] samples, int sampleRate, bool leading, bool trailing,
            double keepSeconds = 0.06, float threshold = DefaultThreshold)
        {
            if (samples.Length == 0 || sampleRate <= 0 || (!leading && !trailing)) return samples;

            int first = -1, last = -1;
            for (int i = 0; i < samples.Length; i++)
            {
                if (Math.Abs(samples[i]) > threshold) { first = i; break; }
            }
            if (first < 0) return samples;
            for (int i = samples.Length - 1; i >= first; i--)
            {
                if (Math.Abs(samples[i]) > threshold) { last = i; break; }
            }

            int keep = (int)(keepSeconds * sampleRate);
            int start = leading ? Math.Max(0, first - keep) : 0;
            int end = trailing ? Math.Min(samples.Length, last + 1 + keep) : samples.Length;
            if (start == 0 && end == samples.Length) return samples;

            var result = new float[end - start];
            Array.Copy(samples, start, result, 0, result.Length);
            return result;
        }
    }
}
