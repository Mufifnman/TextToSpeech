using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Text;
using Windows.Globalization;
using Windows.Graphics.Imaging;
using WinOcrEngine = Windows.Media.Ocr.OcrEngine;

namespace TextToSpeech.Ocr
{
    /// <summary>
    /// OCR using the engine built into Windows 10/11 (Windows.Media.Ocr).
    /// Uses the user's profile languages, falling back to English.
    /// </summary>
    public sealed class WindowsOcrEngine : IOcrEngine
    {
        // GUESS: small screen text recognizes noticeably better when upscaled.
        // Images shorter than this get scaled 2x before OCR.
        private const int UpscaleBelowHeight = 600;

        private readonly WinOcrEngine? engine;

        public WindowsOcrEngine()
        {
            engine = WinOcrEngine.TryCreateFromUserProfileLanguages();
            if (engine == null)
            {
                var english = new Language("en-US");
                if (WinOcrEngine.IsLanguageSupported(english))
                {
                    engine = WinOcrEngine.TryCreateFromLanguage(english);
                }
            }
        }

        public string Name => "Windows OCR";

        public async Task<OcrOutcome> RecognizeAsync(Bitmap bitmap)
        {
            if (engine == null)
            {
                return OcrOutcome.Fail(
                    "Windows OCR isn't available for your languages. Add an OCR language pack in " +
                    "Settings > Time & language > Language & region, or switch OCR to Tesseract.");
            }

            try
            {
                using var softwareBitmap = await ToSoftwareBitmapAsync(PrepareForOcr(bitmap));
                var result = await engine.RecognizeAsync(softwareBitmap);

                var sb = new StringBuilder();
                foreach (var line in result.Lines)
                {
                    sb.Append(line.Text).Append('\n');
                }
                return OcrOutcome.Ok(sb.ToString());
            }
            catch (Exception e)
            {
                return OcrOutcome.Fail("Windows OCR failed: " + e.Message);
            }
        }

        private Bitmap PrepareForOcr(Bitmap bitmap)
        {
            int maxDim = (int)WinOcrEngine.MaxImageDimension;
            double scale = bitmap.Height < UpscaleBelowHeight ? 2.0 : 1.0;
            scale = Math.Min(scale, (double)maxDim / Math.Max(bitmap.Width, bitmap.Height));
            if (Math.Abs(scale - 1.0) < 0.01)
            {
                return bitmap;
            }

            var resized = new Bitmap((int)(bitmap.Width * scale), (int)(bitmap.Height * scale));
            using (var g = Graphics.FromImage(resized))
            {
                g.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.HighQualityBicubic;
                g.DrawImage(bitmap, 0, 0, resized.Width, resized.Height);
            }
            return resized;
        }

        private static async Task<SoftwareBitmap> ToSoftwareBitmapAsync(Bitmap bitmap)
        {
            using var memory = new MemoryStream();
            bitmap.Save(memory, ImageFormat.Png);
            memory.Position = 0;

            var decoder = await BitmapDecoder.CreateAsync(memory.AsRandomAccessStream());
            return await decoder.GetSoftwareBitmapAsync(BitmapPixelFormat.Bgra8, BitmapAlphaMode.Premultiplied);
        }
    }
}
