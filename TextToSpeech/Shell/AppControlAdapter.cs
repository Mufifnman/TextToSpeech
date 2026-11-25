using TextToSpeech.Api;

namespace TextToSpeech.Shell
{
    /// <summary>
    /// Connects the API to the window without exposing the window: the main window passes in what the API may do.
    /// </summary>
    internal sealed class AppControlAdapter : IAppControl
    {
        public required Func<IReadOnlyList<VoiceDto>> Voices { get; init; }
        public required Func<string, bool> SelectVoice { get; init; }
        public required Func<int> GetRate { get; init; }
        public required Action<int> ApplyRate { get; init; }
        public required Func<int> GetVolume { get; init; }
        public required Action<int> ApplyVolume { get; init; }
        public required Func<WindowDto> GetWindow { get; init; }
        public required Action<string> Popup { get; init; }
        public required Func<string, string> Filter { get; init; }

        public IReadOnlyList<VoiceDto> ListVoices() => Voices();
        public bool TrySetVoice(string voiceId) => SelectVoice(voiceId);
        public int Rate => GetRate();
        public int Volume => GetVolume();
        public void SetRate(int rate) => ApplyRate(rate);
        public void SetVolume(int volume) => ApplyVolume(volume);
        public WindowDto Window => GetWindow();
        public void ShowPopup(string source) => Popup(source);
        public string ApplyFilters(string text) => Filter(text);
    }
}
