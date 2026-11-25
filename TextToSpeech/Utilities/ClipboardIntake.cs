using TextToSpeech.Core.Reading;

namespace TextToSpeech
{
    /// <summary>
    /// Decides what to do with text found on the clipboard when the window gets focus: skip it if it is already in
    /// the edit box or still waiting to be read, otherwise run the clean-up filters and hand it back to be added.
    /// </summary>
    internal static class ClipboardIntake
    {
        /// <summary>The cleaned text to append to the edit box, or null when the clipboard text is a repeat.</summary>
        public static string? Prepare(string clipboardText, string boxText, ReadingDocument document, FilterSettings filters)
        {
            string longest = LongestSentence(clipboardText);
            string[] boxLines = boxText.Split('\n');
            if (boxLines.Contains(longest) || document.ContainsUnread(longest))
            {
                return null;
            }

            string text = clipboardText + "\n";
            Filters.ApplyClipboardFilters(ref text, filters);
            return text;
        }

        /// <summary>
        /// Current repeat-prevention key (not great): the longest sentence of the text.
        /// A better rule would check each sentence and add only the new ones, but that needs a position to insert at.
        /// </summary>
        public static string LongestSentence(string text)
        {
            string longest = "";
            foreach (string item in SentenceSplitter.Split(text))
            {
                if (item.Length > longest.Length)
                {
                    longest = item;
                }
            }
            return longest;
        }
    }
}
