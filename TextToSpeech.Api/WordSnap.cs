namespace TextToSpeech.Api
{
    /// <summary>Helpers for character offsets in text, matching the reading model's whitespace-delimited words.</summary>
    public static class WordSnap
    {
        /// <summary>
        /// Rounds <paramref name="offset"/> down to the start of the word it falls in. An offset inside
        /// whitespace snaps to the start of the next word. Offsets are clamped to 0..text.Length.
        /// </summary>
        public static int Down(string text, int offset)
        {
            offset = Math.Clamp(offset, 0, text.Length);
            if (offset >= text.Length)
            {
                return text.Length;
            }
            if (char.IsWhiteSpace(text[offset]))
            {
                while (offset < text.Length && char.IsWhiteSpace(text[offset]))
                {
                    offset++;
                }
                return offset;
            }
            while (offset > 0 && !char.IsWhiteSpace(text[offset - 1]))
            {
                offset--;
            }
            return offset;
        }
    }
}
