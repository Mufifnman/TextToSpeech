namespace TextToSpeech.Speech
{
    /// <summary>
    /// Volume for generated (neural) audio. Gain up to 1.0 is a plain multiply. Above 1.0 the result is
    /// soft-limited so loud peaks round off smoothly instead of clipping harshly.
    /// </summary>
    public static class AudioGain
    {
        // Samples below this level (after gain) pass through untouched; above it they are squeezed toward +-1.
        private const float Knee = 0.8f;

        public static float Apply(float sample, float gain)
        {
            float x = sample * gain;
            if (gain <= 1f)
            {
                return Math.Clamp(x, -1f, 1f);
            }

            float magnitude = Math.Abs(x);
            if (magnitude <= Knee)
            {
                return x;
            }
            float over = (magnitude - Knee) / (1f - Knee);
            float limited = Knee + (1f - Knee) * MathF.Tanh(over);
            return x < 0 ? -limited : limited;
        }
    }
}
