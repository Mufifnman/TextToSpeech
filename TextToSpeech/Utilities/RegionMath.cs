using System.Drawing;

namespace TextToSpeech
{
    public static class RegionMath
    {
        /// <summary>
        /// Maps a selection made on the overlay (device-independent units) onto the frozen screenshot (pixels).
        /// It uses the ratio between the two instead of DPI numbers, so it stays right on scaled and mixed displays.
        /// The result is clamped to the screenshot and at least 1 x 1.
        /// </summary>
        public static Rectangle ToBitmapRect(double selX, double selY, double selWidth, double selHeight,
                                             double surfaceWidth, double surfaceHeight, int bitmapWidth, int bitmapHeight)
        {
            double sx = bitmapWidth / Math.Max(1.0, surfaceWidth);
            double sy = bitmapHeight / Math.Max(1.0, surfaceHeight);

            int left = Math.Clamp((int)Math.Round(selX * sx), 0, Math.Max(0, bitmapWidth - 1));
            int top = Math.Clamp((int)Math.Round(selY * sy), 0, Math.Max(0, bitmapHeight - 1));
            int right = Math.Clamp((int)Math.Round((selX + selWidth) * sx), left + 1, bitmapWidth);
            int bottom = Math.Clamp((int)Math.Round((selY + selHeight) * sy), top + 1, bitmapHeight);
            return new Rectangle(left, top, right - left, bottom - top);
        }
    }
}
