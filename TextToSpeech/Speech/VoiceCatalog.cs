namespace TextToSpeech.Speech
{
    /// <summary>All engines and their voices in one list for the picker. SAPI is always present as the fallback.</summary>
    public sealed class VoiceCatalog : IDisposable
    {
        public SapiSpeechEngine Sapi { get; } = new();
        public NeuralSpeechEngine Neural { get; } = new();

        public IReadOnlyList<VoiceInfo> Voices { get; private set; } = Array.Empty<VoiceInfo>();

        /// <summary>Problems found while listing voices, to show the user instead of failing.</summary>
        public string? LastError { get; private set; }

        public void Refresh()
        {
            var voices = new List<VoiceInfo>();
            LastError = null;
            try { voices.AddRange(Neural.GetVoices()); }
            catch (Exception ex) { LastError = "Could not list neural voices: " + ex.Message; }
            try { voices.AddRange(Sapi.GetVoices()); }
            catch (Exception ex) { LastError = "Could not list Windows voices: " + ex.Message; }
            Voices = voices;
        }

        public ISpeechEngine EngineFor(VoiceInfo? voice) =>
            voice != null && voice.EngineName == Neural.Name ? Neural : Sapi;

        public VoiceInfo? DefaultFallbackVoice => Voices.FirstOrDefault(v => v.EngineName == Sapi.Name);

        /// <summary>The saved voice if it still exists, else the first neural voice, else the first Windows voice.</summary>
        public VoiceInfo? PickInitial(string? savedId) =>
            Voices.FirstOrDefault(v => v.Id == savedId)
            ?? Voices.FirstOrDefault(v => v.IsNeural)
            ?? Voices.FirstOrDefault();

        public void Dispose()
        {
            Neural.Dispose();
            Sapi.Dispose();
        }
    }
}
