using System.Drawing;

namespace TextToSpeech.Ocr
{
    /// <summary>Result of running OCR on an image.</summary>
    public sealed record OcrOutcome(bool Succeeded, string? Text, string? ErrorMessage)
    {
        public static OcrOutcome Ok(string text) => new(true, text, null);
        public static OcrOutcome Fail(string error) => new(false, null, error);
    }

    /// <summary>
    /// A pluggable OCR engine. Engines return text with one recognized line per '\n'
    /// so the existing <see cref="Filters.CombineLines"/> logic keeps working.
    /// </summary>
    public interface IOcrEngine
    {
        string Name { get; }
        Task<OcrOutcome> RecognizeAsync(Bitmap bitmap);
    }

    public static class OcrEngines
    {
        private static readonly Lazy<IOcrEngine> windows = new(() => new WindowsOcrEngine());
        private static readonly Lazy<IOcrEngine> tesseract = new(() => new TesseractOcrEngine());

        public static IOcrEngine For(OcrOptions option) => option switch
        {
            OcrOptions.Windows => windows.Value,
            OcrOptions.Tesseract => tesseract.Value,
            _ => throw new ArgumentOutOfRangeException(nameof(option))
        };
    }
}
