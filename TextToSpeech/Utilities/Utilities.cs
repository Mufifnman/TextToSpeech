using Microsoft.Office.Core;
using System.Drawing;

namespace TextToSpeech
{
    static internal class Utilities
    {
        public static Bitmap CaptureScreenInArea(Point origin, Size size)
        {
            Bitmap bitmap = new Bitmap(size.Width, size.Height);
            {
                using (Graphics graphics = Graphics.FromImage(bitmap))
                {
                    graphics.CopyFromScreen(origin, new System.Drawing.Point(0, 0), size);
                }
            }

            return bitmap;
        }
    }
}