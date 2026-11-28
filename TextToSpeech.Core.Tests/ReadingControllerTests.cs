using TextToSpeech.Core.Reading;
using TextToSpeech.Speech;

namespace TextToSpeech.Core.Tests
{
    /// <summary>A scripted engine: every SpeakAsync call waits until the test finishes (or fails) it.</summary>
    internal sealed class FakeEngine : ISpeechEngine
    {
        public sealed class Call
        {
            public required string Text { get; init; }
            public required VoiceInfo? Voice { get; init; }
            public required Action<WordBoundary>? OnWord { get; init; }
            public TaskCompletionSource Done { get; } = new();
            public bool Cancelled => Done.Task.IsCanceled;
        }

        public List<Call> Calls { get; } = new();
        public Exception? FailNext { get; set; }
        public IReadOnlyList<string> LastUpcoming { get; private set; } = Array.Empty<string>();
        public int ClearUpcomingCalls { get; private set; }
        public List<string> ResumeGuesses { get; } = new();
        public void PrepareResume(string text, VoiceInfo? voice, int rate) => ResumeGuesses.Add(text);

        public string Name => "Fake";
        public IReadOnlyList<VoiceInfo> GetVoices() => Array.Empty<VoiceInfo>();

        public Task SpeakAsync(string text, VoiceInfo? voice, int rate, Action<WordBoundary>? onWord, CancellationToken cancellationToken)
        {
            if (FailNext != null)
            {
                var ex = FailNext;
                FailNext = null;
                return Task.FromException(ex);
            }
            var call = new Call { Text = text, Voice = voice, OnWord = onWord };
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
        public void SetUpcoming(IReadOnlyList<string> texts, VoiceInfo? voice, int rate) => LastUpcoming = texts;
        public void ClearUpcoming() => ClearUpcomingCalls++;
        public void Dispose() { }

        public void Finish(int index) => Calls[index].Done.TrySetResult();
    }

    public class ReadingControllerTests
    {
        private static readonly VoiceInfo Neural = new("neural:0", "Kokoro", "Neural", IsNeural: true);
        private static readonly VoiceInfo Windows = new("sapi:David", "David", "Windows", IsNeural: false);
        private static readonly VoiceInfo Plain = new("plain:1", "Plain", "Fake", IsNeural: false);   // default test voice, spoken by Engine

        private sealed class Rig
        {
            public ReadingDocument Document { get; } = new();
            public FakeEngine Engine { get; } = new();
            public FakeEngine FallbackEngine { get; } = new();
            public List<TimeSpan> Gaps { get; } = new();
            public List<TaskCompletionSource> GapWaits { get; } = new();
            public int Finished, StateChanges, Starting;
            public List<string> Notices { get; } = new();
            public ReadingController Controller { get; }

            public Rig(string text, VoiceInfo? voice = null)
            {
                Controller = new ReadingController(
                    Document,
                    v => v?.Id == Windows.Id ? FallbackEngine : Engine,
                    fallbackVoice: () => Windows,
                    delay: (gap, token) =>
                    {
                        Gaps.Add(gap);
                        var wait = new TaskCompletionSource();
                        GapWaits.Add(wait);
                        token.Register(() => wait.TrySetCanceled(token));
                        return wait.Task;
                    })
                {
                    Voice = voice ?? Plain,
                    GapMs = 300,
                };
                Controller.Finished += () => Finished++;
                Controller.StateChanged += () => StateChanges++;
                Controller.SentenceStarting += () => Starting++;
                Controller.Notice += Notices.Add;
                Document.Append(text);
            }

            public void ReleaseGap(int index) => GapWaits[index].TrySetResult();
        }

        [Fact]
        public void Play_SpeaksTheFirstSentence_ThenTheNextAfterTheGap()
        {
            var rig = new Rig("One two. Three four.");
            rig.Controller.Play();

            Assert.Equal("One two.", rig.Engine.Calls.Single().Text);
            Assert.True(rig.Controller.IsPlaying);

            rig.Engine.Finish(0);
            Assert.Equal(new[] { TimeSpan.FromMilliseconds(300) }, rig.Gaps);   // waits before the next sentence
            Assert.Single(rig.Engine.Calls);

            rig.ReleaseGap(0);
            Assert.Equal("Three four.", rig.Engine.Calls[1].Text);
        }

        [Fact]
        public void TheLastSentence_HasNoGap_AndRaisesFinished()
        {
            var rig = new Rig("Only one.");
            rig.Controller.Play();
            rig.Engine.Finish(0);

            Assert.Empty(rig.Gaps);
            Assert.Equal(1, rig.Finished);
            Assert.False(rig.Controller.IsPlaying);
        }

        [Fact]
        public void Pause_StopsSpeaking_AndPlayContinuesFromTheSpokenWord()
        {
            var rig = new Rig("One two three four.");
            rig.Controller.Play();
            rig.Engine.Calls[0].OnWord!(new WordBoundary(8, 5));   // engine reached "three"

            rig.Controller.Pause();
            Assert.True(rig.Engine.Calls[0].Cancelled);
            Assert.False(rig.Controller.IsPlaying);

            rig.Controller.TogglePlayPause();
            Assert.Equal("three four.", rig.Engine.Calls[1].Text);   // continues from that word
            Assert.True(rig.Controller.IsPlaying);
        }

        [Fact]
        public void Pause_TellsTheEngineWhatWillProbablyBeReadNext_SoItCanPrepareIt()
        {
            var rig = new Rig("One two three four.");
            rig.Controller.Play();
            rig.Engine.Calls[0].OnWord!(new WordBoundary(8, 5));   // engine reached "three"

            rig.Controller.Pause();

            Assert.Equal(new[] { "three four." }, rig.Engine.ResumeGuesses);
        }

        [Fact]
        public void TheEngineIsToldAboutSeveralUpcomingSentences()
        {
            var rig = new Rig("One. Two. Three. Four. Five. Six. Seven. Eight.");
            rig.Controller.Play();

            Assert.Equal(ReadingController.UpcomingSentences, rig.Engine.LastUpcoming.Count);
        }

        [Fact]
        public void PausingDuringTheGap_ResumesAtTheNextSentence_NotTheOneJustRead()
        {
            var rig = new Rig("One two. Three four.");
            rig.Controller.Play();
            rig.Engine.Finish(0);                  // now waiting out the gap

            rig.Controller.Pause();
            rig.ReleaseGap(0);                     // the cancelled wait must not start anything
            Assert.Single(rig.Engine.Calls);

            rig.Controller.TogglePlayPause();
            Assert.Equal("Three four.", rig.Engine.Calls[1].Text);
        }

        [Fact]
        public void JumpTo_SpeaksFromThere_AndCancelsWhateverWasGoingOn()
        {
            var rig = new Rig("One two. Three four. Five six.");
            rig.Controller.Play();

            rig.Controller.JumpTo(2, 1);

            Assert.True(rig.Engine.Calls[0].Cancelled);
            Assert.Equal("six.", rig.Engine.Calls[1].Text);
        }

        [Fact]
        public void JumpingDuringTheGap_CancelsTheWait()
        {
            var rig = new Rig("One two. Three four. Five six.");
            rig.Controller.Play();
            rig.Engine.Finish(0);

            rig.Controller.JumpTo(2, 0);
            rig.ReleaseGap(0);                     // stale wait: ignored

            Assert.Equal(new[] { "One two.", "Five six." }, rig.Engine.Calls.Select(c => c.Text));
        }

        [Fact]
        public void WordsFromACancelledUtterance_AreIgnored()
        {
            var rig = new Rig("One two three. Four five six.");
            rig.Controller.Play();
            var oldCallback = rig.Engine.Calls[0].OnWord!;

            rig.Controller.JumpTo(1, 0);
            oldCallback(new WordBoundary(8, 5));   // late report from the sentence we left

            Assert.Equal(-1, rig.Document.SpokenWord);
        }

        [Fact]
        public void NeuralFailure_FallsBackToTheFallbackVoice_AndSaysSo()
        {
            var rig = new Rig("One two.", Neural);
            VoiceInfo? fellBackTo = null;
            rig.Controller.VoiceFellBack += v => fellBackTo = v;
            rig.Engine.FailNext = new InvalidOperationException("model missing");

            rig.Controller.Play();

            Assert.Equal(Windows, fellBackTo);
            Assert.Equal(Windows, rig.Controller.Voice);
            Assert.Equal("One two.", rig.FallbackEngine.Calls.Single().Text);
            Assert.Contains("model missing", rig.Notices.Single());
        }

        [Fact]
        public void AFailureOfTheFallbackItself_StopsWithAMessage()
        {
            var rig = new Rig("One two.", Windows);
            rig.FallbackEngine.FailNext = new InvalidOperationException("no audio device");

            rig.Controller.Play();

            Assert.False(rig.Controller.IsPlaying);
            Assert.Contains("no audio device", rig.Notices.Single());
        }

        [Fact]
        public void TheEngineIsToldWhichSentencesComeNext()
        {
            var rig = new Rig("One. Two. Three. Four.");
            rig.Controller.Play();

            Assert.Equal(new[] { "Two.", "Three." }, rig.Engine.LastUpcoming);
        }

        [Fact]
        public void Clear_StopsEverything_EmptiesTheDocument_AndDropsPreparedAudio()
        {
            int cleared = 0;
            var document = new ReadingDocument();
            var engine = new FakeEngine();
            var controller = new ReadingController(document, _ => engine, clearPreparedAudio: () => cleared++) { Voice = Windows };
            document.Append("One. Two.");
            controller.Play();

            controller.Clear();

            Assert.True(engine.Calls[0].Cancelled);
            Assert.Equal(0, document.Count);
            Assert.Equal(1, cleared);
            Assert.False(controller.IsSpeaking);
        }

        [Fact]
        public void Interrupt_StopsWithoutMovingThePosition()
        {
            var rig = new Rig("One two. Three four.");
            rig.Controller.Play();

            rig.Controller.Interrupt();

            Assert.True(rig.Engine.Calls[0].Cancelled);
            Assert.Equal(0, rig.Document.Position.Sentence);
            Assert.False(rig.Controller.IsPlaying);
        }

        [Fact]
        public void SkipAndRewind_MoveAndKeepReading_UnlessPaused()
        {
            var rig = new Rig("One. Two. Three.");
            rig.Controller.Play();

            rig.Controller.Skip();
            Assert.Equal("Two.", rig.Engine.Calls.Last().Text);

            rig.Controller.Pause();
            int before = rig.Engine.Calls.Count;
            rig.Controller.Skip();                          // paused: moves but does not speak
            Assert.Equal(before, rig.Engine.Calls.Count);
            Assert.Equal(2, rig.Document.Position.Sentence);
        }

        [Fact]
        public void SentenceStarting_IsRaisedForEverySentence()
        {
            var rig = new Rig("One. Two.");
            rig.Controller.Play();
            rig.Engine.Finish(0);
            rig.ReleaseGap(0);

            Assert.Equal(2, rig.Starting);
        }
    }
}
