using TextToSpeech.Api;
using TextToSpeech.Core.Reading;
using TextToSpeech.Speech;

namespace TextToSpeech.Api.Tests
{
    /// <summary>Scripted engine: each SpeakAsync waits until the test finishes it (or it is cancelled).</summary>
    internal sealed class ScriptedEngine : ISpeechEngine
    {
        public sealed class Call
        {
            public required string Text { get; init; }
            public required Action<WordBoundary>? OnWord { get; init; }
            public TaskCompletionSource Done { get; } = new();
            public bool Cancelled => Done.Task.IsCanceled;
        }

        public List<Call> Calls { get; } = new();
        public string Name => "Scripted";
        public IReadOnlyList<VoiceInfo> GetVoices() => Array.Empty<VoiceInfo>();

        public Task SpeakAsync(string text, VoiceInfo? voice, int rate, Action<WordBoundary>? onWord, CancellationToken cancellationToken)
        {
            var call = new Call { Text = text, OnWord = onWord };
            Calls.Add(call);
            cancellationToken.Register(() => call.Done.TrySetCanceled(cancellationToken));
            return call.Done.Task;
        }

        public bool IsPaused => false;
        public void Pause() { }
        public void Resume() { }
        public void SetVolume(int percent) { }
        public void SetRate(int rate) { }
        public void Prewarm(VoiceInfo? voice) { }
        public void SetUpcoming(IReadOnlyList<string> texts, VoiceInfo? voice, int rate) { }
        public void ClearUpcoming() { }
        public List<(string Key, string[] Texts)> Hints { get; } = new();
        public void PrepareHint(string key, IReadOnlyList<string> texts, VoiceInfo? voice, int rate) => Hints.Add((key, texts.ToArray()));
        public void Dispose() { }

        public void Finish(int index) => Calls[index].Done.TrySetResult();
        public void Word(int index, int charOffset) => Calls[index].OnWord!(new WordBoundary(charOffset, 1));
    }

    internal sealed class FakeApp : IAppControl
    {
        public List<string> Popups { get; } = new();
        public int Rate { get; private set; } = 4;
        public int Volume { get; private set; } = 100;
        public WindowDto Window => new("hidden", false);
        public IReadOnlyList<VoiceDto> ListVoices() => new[] { new VoiceDto("v1", "Voice One", "Scripted", false) };
        public bool TrySetVoice(string voiceId) => voiceId == "v1";
        public void SetRate(int rate) => Rate = rate;
        public void SetVolume(int volume) => Volume = volume;
        public void ShowPopup(string source) => Popups.Add(source);
        public string ApplyFilters(string text) => "[filtered] " + text;
    }

    internal sealed class HostRig
    {
        public const string Three = "Build finished. Three tests failed. Parser broke.";

        public ReadingDocument Document { get; } = new();
        public ScriptedEngine Engine { get; } = new();
        public FakeApp App { get; } = new();
        public DateTimeOffset Now { get; set; } = new(2026, 11, 25, 12, 0, 0, TimeSpan.Zero);
        public ReadingController Reading { get; }
        public ReadingHost Host { get; }
        public List<HostEvent> Events { get; } = new();

        public HostRig()
        {
            Reading = new ReadingController(Document, _ => Engine, delay: (_, _) => Task.CompletedTask);
            Host = new ReadingHost(Reading, App, new SessionStore(() => Now), () => Now);
            Host.Event += Events.Add;
        }

        public Task<SpeakResponse> Speak(string text, string? mode = null, string source = "claude-code", bool? popup = null, bool? filters = null) =>
            Host.SpeakAsync(new SpeakRequest(text, source, mode, filters, popup, null, null, null));

        /// <summary>Finishes the call at <paramref name="index"/> so the controller moves on to the next sentence.</summary>
        public void Finish(int index) => Engine.Finish(index);
    }
}
