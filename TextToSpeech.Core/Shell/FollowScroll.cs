namespace TextToSpeech.Core.Shell
{
    /// <summary>
    /// When the reading view follows the text being read. The view stays still while the sentence is in the middle
    /// third; once its middle drifts into the top or bottom third (or out of view) it is brought back to the center
    /// at once (no gliding). A sentence taller than half the view is lined up a quarter from the top instead, so
    /// its start stays visible.
    /// </summary>
    public static class FollowScroll
    {
        /// <param name="top">Top of the sentence in the content, in pixels.</param>
        /// <param name="bottom">Bottom of the sentence in the content.</param>
        /// <param name="offset">Current vertical scroll offset.</param>
        /// <param name="viewport">Height of the visible area.</param>
        /// <returns>The offset to scroll to, or null to leave the view where it is.</returns>
        public static double? TargetOffset(double top, double bottom, double offset, double viewport)
        {
            if (viewport <= 0 || bottom < top)
            {
                return null;
            }
            double t = top - offset;
            if (bottom - top > viewport / 2)
            {
                return t < 0 || t > viewport / 2 ? Math.Max(0, top - viewport / 4) : null;
            }
            double center = (t + (bottom - offset)) / 2;
            if (center >= viewport / 3 && center <= viewport * 2 / 3)
            {
                return null;
            }
            return Math.Max(0, (top + bottom) / 2 - viewport / 2);
        }
    }
}
