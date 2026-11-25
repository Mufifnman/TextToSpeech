using System.Drawing;
using System.Runtime.InteropServices;

namespace TextToSpeech
{
    internal static class ScreenGeometry
    {
        [DllImport("user32.dll")] private static extern int GetSystemMetrics(int index);

        /// <summary>The rectangle covering every monitor, in pixels (the origin can be negative).</summary>
        public static Rectangle VirtualScreenPixels()
        {
            const int SM_XVIRTUALSCREEN = 76, SM_YVIRTUALSCREEN = 77, SM_CXVIRTUALSCREEN = 78, SM_CYVIRTUALSCREEN = 79;
            return new Rectangle(GetSystemMetrics(SM_XVIRTUALSCREEN), GetSystemMetrics(SM_YVIRTUALSCREEN),
                                 GetSystemMetrics(SM_CXVIRTUALSCREEN), GetSystemMetrics(SM_CYVIRTUALSCREEN));
        }
    }
}
