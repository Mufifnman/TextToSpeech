namespace TextToSpeech.Hotkeys
{
    /// <summary>
    /// Seam for region-select OCR. Implemented by the screen-capture overlay work;
    /// returns the recognized text, or null if the user cancelled or nothing was found.
    /// </summary>
    public interface IRegionReader
    {
        Task<string?> SelectRegionAndReadAsync();
    }
}
