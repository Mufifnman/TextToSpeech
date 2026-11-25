namespace TextToSpeech.Api
{
    /// <summary>
    /// The few things the API needs from the app shell that are not reading itself: voices, speed and
    /// volume (which also live in settings and sliders), the window state, the reading popup and text filters.
    /// Called on the UI thread.
    /// </summary>
    public interface IAppControl
    {
        IReadOnlyList<VoiceDto> ListVoices();

        /// <summary>Selects the voice (and saves the choice). False when no voice has that id.</summary>
        bool TrySetVoice(string voiceId);

        int Rate { get; }
        int Volume { get; }

        /// <summary>Applies the value (the app clamps it to its own range, updates sliders and saves it).</summary>
        void SetRate(int rate);
        void SetVolume(int volume);

        WindowDto Window { get; }

        /// <summary>Reading started from the API; shows the reading popup if it is enabled and the window is hidden.</summary>
        void ShowPopup(string source);

        /// <summary>Runs the app's text filters (citations and so on) over API text when the client asked for it.</summary>
        string ApplyFilters(string text);
    }
}
