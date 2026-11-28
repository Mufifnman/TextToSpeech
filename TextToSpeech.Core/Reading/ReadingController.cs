using TextToSpeech.Speech;

namespace TextToSpeech.Core.Reading
{
    /// <summary>Where reading is waiting: the pause point, who owns the text, and the document index of the sentence it follows.</summary>
    public sealed record PausedAtInfo(string? Owner, PausePoint Point, int SentenceIndex);

    /// <summary>
    /// Drives reading: speaks the <see cref="ReadingDocument"/> sentence by sentence through a speech engine,
    /// with play, pause, skip, rewind, jump, the pause between sentences, a fallback voice, and look-ahead hints.
    ///
    /// It has no UI and no knowledge of WPF, Windows or any concrete engine, so the window, a hotkey handler,
    /// a pop-up or a local API can all use the same instance and it can be unit tested with a fake engine.
    /// Everything it is given must be called from one thread (the UI thread); engines may report words from any
    /// thread and the controller hands those back through <c>post</c>.
    /// </summary>
    public sealed class ReadingController : IDisposable
    {
        private readonly ReadingDocument document;
        private readonly Func<VoiceInfo?, ISpeechEngine> engineFor;
        private readonly Func<VoiceInfo?> fallbackVoice;
        private readonly Action clearPreparedAudio;
        private readonly Action<Action> post;
        private readonly Func<TimeSpan, CancellationToken, Task> delay;

        // The last sentence of a "play just this line" request. Held as the sentence itself, not an index, so text inserted
        // or removed while the line plays cannot move the stop to the wrong place.
        private Sentence? stopAfter;

        // Set when a line was played while waiting at a question: the wait (and the place in the text) comes back after the line.
        private (PausedAtInfo At, ReadingPosition Position)? resumeAfterLine;

        private CancellationTokenSource? speechCts;
        private int generation;      // bumped for every utterance and every cancel; late events from old ones are ignored
        private bool paused;         // true after the user paused or stopped: nothing speaks until they resume or jump

        /// <param name="engineFor">The engine that speaks a given voice.</param>
        /// <param name="fallbackVoice">A voice that always works, used when a neural voice fails (null if there is none).</param>
        /// <param name="clearPreparedAudio">Forgets audio engines prepared ahead of time (called when the list is cleared).</param>
        /// <param name="post">Runs an action on the UI thread. Defaults to running it right away.</param>
        /// <param name="delay">Waits between sentences. Defaults to <see cref="Task.Delay(TimeSpan, CancellationToken)"/>.</param>
        public ReadingController(
            ReadingDocument document,
            Func<VoiceInfo?, ISpeechEngine> engineFor,
            Func<VoiceInfo?>? fallbackVoice = null,
            Action? clearPreparedAudio = null,
            Action<Action>? post = null,
            Func<TimeSpan, CancellationToken, Task>? delay = null)
        {
            this.document = document;
            this.engineFor = engineFor;
            this.fallbackVoice = fallbackVoice ?? (() => null);
            this.clearPreparedAudio = clearPreparedAudio ?? (() => { });
            this.post = post ?? (action => action());
            this.delay = delay ?? Task.Delay;
        }

        /// <summary>How many following sentences the engine is told about (it decides how many it prepares, by seconds of audio).</summary>
        public const int UpcomingSentences = 6;

        /// <summary>An API caller's hint that this text will be read soon (an empty list withdraws it). The engine may prepare the audio.</summary>
        public void PrewarmHint(string key, IReadOnlyList<string> texts) => engineFor(Voice).PrepareHint(key, texts, Voice, Rate);

        /// <summary>Look-ahead hits and misses of the engine for the current voice (null where the engine has none).</summary>
        public SynthesisStats? LookAheadStats => engineFor(Voice).LookAheadStats;

        public ReadingDocument Document => document;

        /// <summary>The voice used for the next sentence. Changing it takes effect from the next sentence.</summary>
        public VoiceInfo? Voice { get; set; }

        /// <summary>Speaking rate on the SAPI scale.</summary>
        public int Rate { get; set; } = 4;

        /// <summary>Silence after each sentence, in milliseconds.</summary>
        public int GapMs { get; set; } = SentencePacing.DefaultGapMs;

        /// <summary>
        /// Read through pause points without stopping. Off by default; the app sets it from its "play all by default"
        /// setting when new text with pause points arrives, and the play-all button flips it.
        /// </summary>
        public bool PlayAll { get; set; }

        /// <summary>Set while reading is stopped at a pause point (a question waiting for an answer).</summary>
        public PausedAtInfo? PausedAt { get; private set; }

        /// <summary>Reading stopped at a pause point.</summary>
        public event Action? PauseReached;

        /// <summary>Something is being spoken or waited on (this stays true while paused only until the pause cancels it).</summary>
        public bool IsSpeaking => speechCts != null;

        /// <summary>Speaking and not paused: what the play/pause button should show as "playing".</summary>
        public bool IsPlaying => IsSpeaking && !paused;

        /// <summary>The play or pause state changed.</summary>
        public event Action? StateChanged;

        /// <summary>A sentence is about to be spoken (the window may want to come up).</summary>
        public event Action? SentenceStarting;

        /// <summary>The end of the document was reached.</summary>
        public event Action? Finished;

        /// <summary>The list was emptied by <see cref="Clear"/> (window button, hotkey or API).</summary>
        public event Action? Cleared;

        /// <summary>A short message for the status line (a voice failed, speech failed).</summary>
        public event Action<string>? Notice;

        /// <summary>A neural voice failed and <see cref="Voice"/> was switched to the fallback.</summary>
        public event Action<VoiceInfo>? VoiceFellBack;

        // ---------- commands ----------

        /// <summary>Starts or continues reading from the current position (does nothing if already speaking).</summary>
        public void Play()
        {
            paused = false;
            ClearPausedAt(markAnswered: true);
            if (!IsSpeaking)
            {
                SpeakCurrent();
            }
        }

        /// <summary>Pauses. The position is kept at the word being spoken, so Play continues from there.</summary>
        public void Pause()
        {
            paused = true;
            CancelSpeech();
            document.MarkStoppedAtSpokenWord();
            StateChanged?.Invoke();

            // Guess that reading resumes from here: let the engine make that audio while idle.
            string? rest = document.GetTextToSpeak(out _);
            if (rest != null)
            {
                engineFor(Voice).PrepareResume(rest, Voice, Rate);
            }
        }

        /// <summary>Pauses while playing, otherwise resumes from the saved position (if anything is left).</summary>
        public void TogglePlayPause()
        {
            if (IsPlaying)
            {
                Pause();
            }
            else if (PausedAt != null || !document.IsAtEnd)
            {
                paused = false;
                ClearPausedAt(markAnswered: true);
                SpeakCurrent();
            }
        }

        public void Rewind()
        {
            ClearPausedAt(markAnswered: false);
            stopAfter = null;
            resumeAfterLine = null;
            document.Rewind();
            AfterMove();
        }

        public void Skip()
        {
            // Skipping while waiting at a question counts as moving past it.
            ClearPausedAt(markAnswered: true);
            stopAfter = null;
            resumeAfterLine = null;
            if (document.Skip())
            {
                AfterMove();
            }
        }

        /// <summary>
        /// Marks a pause point as answered. When reading is waiting at exactly that one it carries on from there.
        /// Returns false when nothing has that point.
        /// </summary>
        public bool MarkAnswered(PausePoint point)
        {
            point.Answered = true;
            if (PausedAt?.Point == point)
            {
                Play();
            }
            else
            {
                StateChanged?.Invoke();
            }
            return true;
        }

        /// <summary>Continues after the pause point reading is waiting at (marking it answered). False when not waiting at one.</summary>
        public bool ContinueFromPause()
        {
            if (PausedAt == null)
            {
                return false;
            }
            Play();
            return true;
        }

        /// <summary>Reads from a word the user picked, even when paused or stopped.</summary>
        public void JumpTo(int sentence, int word)
        {
            ClearPausedAt(markAnswered: false);
            stopAfter = null;
            resumeAfterLine = null;
            document.JumpTo(sentence, word);
            paused = false;
            SpeakCurrent();
        }

        /// <summary>
        /// Plays the sentences <paramref name="first"/> to <paramref name="last"/> (document indexes) and then stops,
        /// even when paused or stopped. For "play just this line".
        /// </summary>
        public void PlayRange(int first, int last)
        {
            if (first < 0 || first >= document.Count)
            {
                return;   // the text changed since the click: nothing to play
            }
            if (PausedAt != null)
            {
                // Waiting at a question: remember it, and come back to it when the line is done.
                resumeAfterLine = (PausedAt, document.Position);
            }
            ClearPausedAt(markAnswered: false);
            document.JumpTo(first, 0);
            stopAfter = document.Sentences[Math.Clamp(last, first, document.Count - 1)];
            paused = false;
            SpeakCurrent();
        }

        /// <summary>Stops everything and empties the document.</summary>
        public void Clear()
        {
            stopAfter = null;
            resumeAfterLine = null;
            CancelSpeech();
            clearPreparedAudio();
            document.Clear();
            paused = false;
            PausedAt = null;
            StateChanged?.Invoke();
            Cleared?.Invoke();
        }

        /// <summary>Stops speaking and holds, without moving the position (used when text is taken out of the document).</summary>
        public void Interrupt()
        {
            CancelSpeech();
            ClearPausedAt(markAnswered: false);
            stopAfter = null;
            resumeAfterLine = null;
            paused = true;
            StateChanged?.Invoke();
        }

        public void Dispose() => CancelSpeech();

        // ---------- internals ----------

        /// <summary>After the position moved: keep reading from there, unless the user is paused.</summary>
        private void AfterMove()
        {
            if (paused)
            {
                CancelSpeech();
                StateChanged?.Invoke();
            }
            else
            {
                SpeakCurrent();
            }
        }

        private void ClearPausedAt(bool markAnswered)
        {
            if (PausedAt != null && markAnswered)
            {
                PausedAt.Point.Answered = true;
            }
            PausedAt = null;
        }

        private void CancelSpeech()
        {
            generation++;   // events from the utterance being cancelled are ignored from now on
            var cts = speechCts;
            speechCts = null;
            cts?.Cancel();
        }

        /// <summary>Speaks the document from its current position, cancelling anything already going. Calls itself as sentences finish.</summary>
        private void SpeakCurrent()
        {
            CancelSpeech();

            string? text = document.GetTextToSpeak(out int offset);
            if (text == null)
            {
                Finished?.Invoke();
                StateChanged?.Invoke();
                return;
            }

            SentenceStarting?.Invoke();

            int myGeneration = ++generation;
            var cts = new CancellationTokenSource();
            speechCts = cts;
            _ = SpeakSentenceAsync(text, offset, myGeneration, cts);

            // Tell the engine what comes next so it can prepare it while this sentence plays. This comes after the
            // speak call on purpose: the engine has then asked for this sentence's audio, so the new list cannot
            // throw away the audio that was prepared for it (it is not part of "what comes next").
            engineFor(Voice).SetUpcoming(document.PeekUpcomingTexts(UpcomingSentences), Voice, Rate);
            StateChanged?.Invoke();
        }

        /// <summary>
        /// Speaks one chunk with the chosen voice, then moves the document on and speaks the next. If a neural
        /// voice fails, falls back to the fallback voice instead of going silent.
        /// </summary>
        private async Task SpeakSentenceAsync(string text, int offset, int myGeneration, CancellationTokenSource cts)
        {
            VoiceInfo? voice = Voice;
            try
            {
                try
                {
                    await SpeakWith(voice, text, offset, myGeneration, cts.Token);
                }
                catch (OperationCanceledException)
                {
                    throw;
                }
                catch (Exception ex) when (voice != null && voice.IsNeural && fallbackVoice() != null)
                {
                    VoiceInfo fallback = fallbackVoice()!;
                    Notice?.Invoke("Neural voice failed (" + ex.Message + "); using " + fallback.DisplayName);
                    Voice = fallback;
                    VoiceFellBack?.Invoke(fallback);
                    await SpeakWith(fallback, text, offset, myGeneration, cts.Token);
                }
            }
            catch (OperationCanceledException)
            {
                return; // paused, stopped, or moved: whoever cancelled decides what happens next
            }
            catch (Exception ex)
            {
                if (myGeneration == generation)
                {
                    Notice?.Invoke("Speech failed: " + ex.Message);
                    CancelSpeech();
                    paused = true;
                    StateChanged?.Invoke();
                }
                return;
            }

            if (myGeneration != generation || cts.IsCancellationRequested)
            {
                return;
            }

            // Move on first, so pausing during the gap resumes at the next sentence, not the one just read.
            var finished = document.CurrentSentence;
            document.CompleteSentence();

            // "Play just this line": the line is done, stop here and wait.
            if (stopAfter != null && ReferenceEquals(finished, stopAfter))
            {
                stopAfter = null;
                CancelSpeech();
                paused = true;
                if (resumeAfterLine is { } back)
                {
                    resumeAfterLine = null;
                    document.MoveToPosition(back.Position.Sentence, back.Position.Word);
                    if (!back.At.Point.Answered)
                    {
                        PausedAt = back.At;
                    }
                    else
                    {
                        // The question was answered while the line played: carry on from where the wait was.
                        paused = false;
                        SpeakCurrent();
                        return;
                    }
                }
                StateChanged?.Invoke();
                return;
            }

            // A pause point the caller asked for: stop here and wait (the next sentences are already being prepared).
            if (finished?.Pause is { Answered: false } point && !PlayAll)
            {
                CancelSpeech();
                paused = true;
                PausedAt = new PausedAtInfo(finished.Owner, point, document.Position.Sentence - 1);
                StateChanged?.Invoke();
                PauseReached?.Invoke();
                return;
            }

            var gap = SentencePacing.GapAfter(document.IsAtEnd, GapMs, finished?.Format?.GapFactor ?? 1);
            if (gap > TimeSpan.Zero)
            {
                try
                {
                    await delay(gap, cts.Token);   // jump, pause, stop and clear all cancel the wait
                }
                catch (OperationCanceledException)
                {
                    return;
                }
                if (myGeneration != generation)
                {
                    return;
                }
            }
            SpeakCurrent();
        }

        private Task SpeakWith(VoiceInfo? voice, string text, int offset, int myGeneration, CancellationToken token) =>
            engineFor(voice).SpeakAsync(text, voice, Rate, boundary =>
            {
                // engines report words from their own threads
                post(() =>
                {
                    if (myGeneration == generation)
                    {
                        document.ReportSpokenCharacter(offset + boundary.CharOffset);
                    }
                });
            }, token);
    }
}
