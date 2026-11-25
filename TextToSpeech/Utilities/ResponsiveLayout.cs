namespace TextToSpeech
{
    /// <summary>Window-size rules in one place so they can be tested and changed easily.</summary>
    public static class ResponsiveLayout
    {
        /// <summary>Below this window width (in device-independent pixels) the window counts as small; at or above it, big.</summary>
        public const double NarrowWidth = 700;

        /// <summary>The speed - and + buttons show only when the window is big; when it is small they hide (the slider popup always works).</summary>
        public static bool ShowSpeedStepButtons(double windowWidth) => windowWidth >= NarrowWidth;
    }
}
