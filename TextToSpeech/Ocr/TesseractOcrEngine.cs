using System.Drawing;

namespace TextToSpeech.Ocr
{
    /// <summary>Adapter that exposes the existing Tesseract manager as an <see cref="IOcrEngine"/>.</summary>
    public sealed class TesseractOcrEngine : IOcrEngine
    {
        public string Name => "Tesseract";

        public Task<OcrOutcome> RecognizeAsync(Bitmap bitmap)
        {
            // Tesseract is CPU bound and synchronous, so keep it off the UI thread.
            return Task.Run(() =>
            {
                try
                {
                    return TesseractOCRManager.Instance.GetTextFromImage(bitmap, out var text, out var error)
                        ? OcrOutcome.Ok(text)
                        : OcrOutcome.Fail(error);
                }
                catch (Exception e)
                {
                    return OcrOutcome.Fail("Tesseract failed to start: " + (e.InnerException?.Message ?? e.Message));
                }
            });
        }
    }
}
