namespace TextToSpeech
{
    /// <summary>Turns raw OCR output into text that reads well.</summary>
    internal static class OcrTextProcessor
    {
        /// <param name="ocrText">What the OCR engine returned (one line per recognized line).</param>
        /// <param name="incompleteTail">A sentence cut off at the bottom of the last capture, which continues at the top of this one.</param>
        public static string Process(string ocrText, string? incompleteTail, FilterSettings filters)
        {
            string text = ocrText;
            if (incompleteTail != null)
            {
                text = incompleteTail + " " + text;
            }

            Filters.CombineLines(ref text);
            text = text.Replace('\u00A1', 'i');   // OCR often reads an "i" as an inverted exclamation mark
            Filters.ApplyOcrFilters(ref text, filters);
            return text;
        }
    }
}
