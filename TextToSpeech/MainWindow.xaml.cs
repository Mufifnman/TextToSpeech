using System.Drawing;
using System.Windows;
using System.Windows.Input;
using TextToSpeech.Speech;
using TextToSpeech.Core.Reading;

using System.Windows.Controls;
using TextToSpeech.Hotkeys;


namespace TextToSpeech
{
    /// <summary>
    /// Interaction logic for MainWindow.xaml
    /// 
    /// The text being read lives in <see cref="ReadingDocument"/> (TextToSpeech.Core); this
    /// class only turns document positions into speech and user input into document moves.
    /// </summary>
    public partial class MainWindow : System.Windows.Window, IRegionReader
    {
        // objects
        readonly VoiceCatalog catalog = new VoiceCatalog();
        readonly AppSettings settings = AppSettings.Load();
        VoiceInfo? selectedVoice = null;

        // Cancels the sentence being spoken; null when nothing is speaking.
        CancellationTokenSource? speechCts = null;
        bool IsSpeaking => speechCts != null;

        readonly ReadingDocument document = new ReadingDocument();
        int rate = 4;

        // Characters at the start of the current sentence that were not sent to the engine
        // (when starting from the middle of a sentence). Engine positions are relative to what we sent.
        int spokenOffset = 0;

        // Incremented for every utterance; events from cancelled utterances are ignored.
        int speakGeneration = 0;

        // True after the user paused or stopped; nothing speaks until they resume or click a word.
        bool paused = false;
        FilterSettings filterSettings = FilterSettingsStore.Load();

        public MainWindow()
        {
            this.FontFamily = new System.Windows.Media.FontFamily("Segoe UI");
            InitializeComponent();
            SetSpeed(settings.Rate, save: false);
            speedSlider.ValueChanged += (_, e) => { if (!updatingSpeedUi) SetSpeed((int)Math.Round(e.NewValue)); };
            RefreshVoices();
            __ReadListDisplay__.Document = document;
            __ReadListDisplay__.WordClicked += JumpTo;

            BuildFiltersMenu();
            BuildOcrMenu();
            RegionReader = this;
            SourceInitialized += (_, _) => SetupHotkeys();
            Closed += (_, _) => { CancelSpeech(); hotkeys?.Dispose(); catalog.Dispose(); };
        }

        /// <summary>A bold, unclickable title at the top of a popup menu so people know what they are choosing.</summary>
        private static void AddMenuHeading(ContextMenu menu, string title)
        {
            menu.Items.Add(new MenuItem
            {
                Header = title,
                IsEnabled = false,
                FontWeight = FontWeights.SemiBold,
            });
            menu.Items.Add(new Separator());
        }

        /// <summary>Rebuilds the voice menu from what is installed, keeping the current choice if it still exists.</summary>
        private void RefreshVoices()
        {
            catalog.Refresh();
            string? wanted = selectedVoice?.Id ?? settings.VoiceId;
            selectedVoice = catalog.PickInitial(wanted);
            BuildVoiceMenu();
            if (catalog.LastError != null) statusText.Text = catalog.LastError;
        }

        /// <summary>One checkable item per voice (neural first, then Windows), then "More voices...".</summary>
        private void BuildVoiceMenu()
        {
            voiceMenu.Items.Clear();
            AddMenuHeading(voiceMenu, "Voice");
            foreach (VoiceInfo voice in catalog.Voices)
            {
                var item = new MenuItem
                {
                    Header = voice.DisplayName,
                    IsCheckable = true,
                    IsChecked = selectedVoice?.Id == voice.Id,
                    Tag = voice,
                };
                item.Click += VoiceMenuItem_Click;
                voiceMenu.Items.Add(item);
            }
            if (voiceMenu.Items.Count > 0) voiceMenu.Items.Add(new Separator());
            var more = new MenuItem { Header = "More voices..." };
            more.Click += MoreVoices_Click;
            voiceMenu.Items.Add(more);
            voiceButton.ToolTip = selectedVoice != null ? "Voice: " + selectedVoice.DisplayName : "Choose the voice";
        }

        private void FiltersButton_Click(object sender, RoutedEventArgs e)
        {
            filtersMenu.PlacementTarget = filtersButton;
            filtersMenu.Placement = System.Windows.Controls.Primitives.PlacementMode.Top;
            filtersMenu.IsOpen = true;
        }

        private void VoiceButton_Click(object sender, RoutedEventArgs e)
        {
            voiceMenu.PlacementTarget = voiceButton;
            voiceMenu.Placement = System.Windows.Controls.Primitives.PlacementMode.Bottom;
            voiceMenu.IsOpen = true;
        }

        private void VoiceMenuItem_Click(object sender, RoutedEventArgs e)
        {
            if (((MenuItem)sender).Tag is VoiceInfo v)
            {
                SelectVoice(v);
            }
        }

        private void SelectVoice(VoiceInfo v)
        {
            selectedVoice = v;
            settings.VoiceId = v.Id;
            settings.Save();
            BuildVoiceMenu(); // refresh the check mark; takes effect from the next sentence
        }

        private void MoreVoices_Click(object sender, RoutedEventArgs e)
        {
            var dialog = new VoiceDownloadWindow { Owner = this };
            dialog.ShowDialog();
            RefreshVoices();
        }

        // One submenu per FilterRegistry group, one checkable item per filter.
        private void BuildFiltersMenu()
        {
            filtersMenu.Items.Clear();
            AddMenuHeading(filtersMenu, "Filters");
            foreach (string group in FilterRegistry.Groups)
            {
                var groupItem = new MenuItem { Header = group };
                foreach (var filter in FilterRegistry.All.Where(f => f.Group == group))
                {
                    var item = new MenuItem
                    {
                        Header = filter.Label,
                        IsCheckable = true,
                        StaysOpenOnClick = true,
                        IsChecked = filterSettings.IsEnabled(filter.Id),
                        Tag = filter.Id,
                    };
                    item.Click += FilterMenuItem_Click;
                    groupItem.Items.Add(item);
                }
                filtersMenu.Items.Add(groupItem);
            }
        }

        private void FilterMenuItem_Click(object sender, RoutedEventArgs e)
        {
            var item = (MenuItem)sender;
            filterSettings.SetEnabled((string)item.Tag, item.IsChecked);
            FilterSettingsStore.Save(filterSettings);
        }

        // ---- global hotkeys (logic lives in the Hotkeys folder) ----
        HotkeySettings hotkeySettings = HotkeySettings.Load();
        GlobalHotkeyManager hotkeys;

        /// <summary>Set by the region-OCR overlay work; until then the hotkey uses the Read Image button flow.</summary>
        public IRegionReader RegionReader { get; set; }

        private void SetupHotkeys()
        {
            hotkeys ??= new GlobalHotkeyManager(this);
            hotkeys.UnregisterAll();
            var failed = new List<string>();
            if (!hotkeys.Register(hotkeySettings.GetReadSelection(), () => _ = ReadSelectionHotkey(showWindow: false)))
                failed.Add(hotkeySettings.GetReadSelection().ToString());
            if (!hotkeys.Register(hotkeySettings.GetReadSelectionAndShow(), () => _ = ReadSelectionHotkey(showWindow: true)))
                failed.Add(hotkeySettings.GetReadSelectionAndShow().ToString());
            if (!hotkeys.Register(hotkeySettings.GetSummonWindow(), BringToFront))
                failed.Add(hotkeySettings.GetSummonWindow().ToString());
            if (!hotkeys.Register(hotkeySettings.GetReadRegion(), () => _ = ReadRegionHotkey()))
                failed.Add(hotkeySettings.GetReadRegion().ToString());
            if (failed.Count > 0)
            {
                Title = "Text to Speech (hotkey in use by another app: " + string.Join(", ", failed) + ")";
            }
            else
            {
                Title = "Text to Speech";
            }
        }

        private void BringToFront()
        {
            if (WindowState == System.Windows.WindowState.Minimized)
                WindowState = System.Windows.WindowState.Normal;
            Show();
            Topmost = true;   // reliable way to beat foreground-lock rules
            Topmost = false;
            Activate();
            Focus();
        }

        // Set when a hotkey brought a hidden window up; the window goes back down when reading ends.
        bool minimizeWhenDone = false;

        private async Task ReadSelectionHotkey(bool showWindow)
        {
            string text = await SelectionCapture.GetSelectedTextAsync();
            if (text == null) return;
            if (showWindow)
            {
                // Only re-minimize if the window wasn't already up (if the user had it open, leave it alone).
                minimizeWhenDone = WindowState == System.Windows.WindowState.Minimized || !IsVisible;
                ignoreClipboardGrabUntil = DateTime.UtcNow.AddSeconds(2);
                BringToFront();
            }
            QueueTextAndRead(text);
        }

        private async Task ReadRegionHotkey()
        {
            if (RegionReader == null)
            {
                BringToFront(); // fallback: the existing Read Image flow captures the screen behind the window
                ImageRead(this, new RoutedEventArgs());
                return;
            }
            string text = await RegionReader.SelectRegionAndReadAsync();
            if (string.IsNullOrWhiteSpace(text)) return;
            QueueTextAndRead(text);
        }

        private void QueueTextAndRead(string text)
        {
            Filters.ApplyClipboardFilters(ref text, filterSettings);
            textbox.Text = text;
            ReadText(this, new RoutedEventArgs());
        }

        private void HotkeySettings_Click(object sender, RoutedEventArgs e)
        {
            var dlg = new HotkeySettingsWindow(hotkeySettings) { Owner = this };
            if (dlg.ShowDialog() == true) SetupHotkeys();
        }

        private void ReadText(object sender, RoutedEventArgs e)
        {
            if (this.textbox.Text != "")
            {
                string temp = textbox.Text;
                Filters.CombineLines(ref temp);
                // cut out "read more at"
                document.Append(temp, s => !s.ToLower().Contains("read more at"));
                textbox.Text = "";
            }
            paused = false;
            if (!IsSpeaking) // if we aren't already reading
            {
                SpeakCurrent();
            }
        }

        /// <summary>
        /// Speaks the document from its current position (sentence and word), cancelling
        /// anything already being spoken. Calls itself when a sentence completes. UI thread only.
        /// </summary>
        private void SpeakCurrent()
        {
            CancelSpeech();

            string? text = document.GetTextToSpeak(out spokenOffset);
            if (text == null)
            {
                if (minimizeWhenDone)
                {
                    // The hotkey brought the window up for this read; put it away again.
                    minimizeWhenDone = false;
                    this.WindowState = System.Windows.WindowState.Minimized;
                }
                // Done: maximize so next image can be scanned or line can be copied
                else if (this.WindowState == System.Windows.WindowState.Minimized)
                {
                    this.WindowState = System.Windows.WindowState.Normal;
                }
                UpdatePauseButton();
                return;
            }

            int generation = ++speakGeneration;
            var cts = new CancellationTokenSource();
            speechCts = cts;
            string sentenceText = text;
            int offset = spokenOffset;
            _ = SpeakSentenceAsync(sentenceText, offset, generation, cts);
            UpdatePauseButton();
        }

        /// <summary>
        /// Speaks one chunk of text with the chosen voice, then moves the document on and speaks the next.
        /// If a neural voice fails, falls back to a Windows voice instead of going silent.
        /// </summary>
        private async Task SpeakSentenceAsync(string text, int offset, int generation, CancellationTokenSource cts)
        {
            VoiceInfo? voice = selectedVoice;
            try
            {
                try
                {
                    await SpeakWith(voice, text, offset, generation, cts.Token);
                }
                catch (OperationCanceledException)
                {
                    throw;
                }
                catch (Exception ex) when (voice != null && voice.IsNeural && catalog.DefaultFallbackVoice != null)
                {
                    VoiceInfo fallback = catalog.DefaultFallbackVoice!;
                    statusText.Text = "Neural voice failed (" + ex.Message + "); using " + fallback.DisplayName;
                    selectedVoice = fallback;
                    BuildVoiceMenu();
                    await SpeakWith(fallback, text, offset, generation, cts.Token);
                }
            }
            catch (OperationCanceledException)
            {
                return; // paused, stopped, or moved: whoever cancelled decides what happens next
            }
            catch (Exception ex)
            {
                if (generation == speakGeneration)
                {
                    statusText.Text = "Speech failed: " + ex.Message;
                    CancelSpeech();
                    paused = true;
                    UpdatePauseButton();
                }
                return;
            }

            if (generation != speakGeneration || cts.IsCancellationRequested)
            {
                return;
            }
            document.CompleteSentence();
            SpeakCurrent();
        }

        private Task SpeakWith(VoiceInfo? voice, string text, int offset, int generation, CancellationToken token) =>
            catalog.EngineFor(voice).SpeakAsync(text, voice, rate, boundary =>
            {
                // engines report words off the UI thread
                Dispatcher.BeginInvoke(() =>
                {
                    if (generation == speakGeneration)
                    {
                        document.ReportSpokenCharacter(offset + boundary.CharOffset);
                    }
                });
            }, token);

        /// <summary>Stops speaking without moving the reading position.</summary>
        private void CancelSpeech()
        {
            speakGeneration++; // ignore events from the utterance being cancelled
            var cts = speechCts;
            speechCts = null;
            cts?.Cancel();
        }

        /// <summary>After the position moved: keep reading from there, unless the user is paused.</summary>
        private void AfterMove()
        {
            if (paused)
            {
                CancelSpeech();
                UpdatePauseButton();
            }
            else
            {
                SpeakCurrent();
            }
        }

        private void JumpTo(int sentence, int word)
        {
            document.JumpTo(sentence, word);
            // Clicking a word means "read from here", even when paused or stopped.
            paused = false;
            SpeakCurrent();
        }

        private void Rewind_Click(object sender, RoutedEventArgs e) => Rewind();

        private void Skip_Click(object sender, RoutedEventArgs e) => Skip();

        private void PlayPause_Click(object sender, RoutedEventArgs e) => PlayOrPause();

        /// <summary>
        /// The one Play/Pause button (and Space): while reading it pauses; otherwise it starts reading,
        /// first adding whatever is in the text box, or resumes from the saved position if the box is empty.
        /// </summary>
        private void PlayOrPause()
        {
            if (IsSpeaking && !paused)
            {
                Pause();
            }
            else if (textbox.Text != "")
            {
                ReadText(this, new RoutedEventArgs());
            }
            else
            {
                TogglePlayPause();
            }
        }

        private void Rewind()
        {
            document.Rewind();
            AfterMove();
        }

        private void Skip()
        {
            if (document.Skip())
            {
                AfterMove();
            }
        }

        /// <summary>
        ///  clears the list of items to be read
        /// </summary>
        private void ClearList(object? sender = null, RoutedEventArgs? e = null)
        {
            CancelSpeech();
            document.Clear();
            paused = false;
            UpdatePauseButton();
        }

        /// <summary>Stops speaking. The position is kept, so Pause/Play (or clicking a word) continues from here.</summary>
        private void StopRead(object? sender = null, RoutedEventArgs? e = null)
        {
            Pause();
        }

        private void Pause()
        {
            paused = true;
            CancelSpeech();
            document.MarkStoppedAtSpokenWord(); // resume from the word we were on
            UpdatePauseButton();
        }

        private void TogglePlayPause()
        {
            if (IsSpeaking && !paused)
            {
                Pause();
            }
            else if (!document.IsAtEnd)
            {
                paused = false;
                SpeakCurrent();
            }
        }

        private void UpdatePauseButton()
        {
            bool playing = IsSpeaking && !paused;
            pauseGlyph.Text = playing ? "\uE769" : "\uE768";        // pause / play
            pauseGlyph.Foreground = new System.Windows.Media.SolidColorBrush(
                (System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString(playing ? "#E0A010" : "#2E9E4F"));
            pauseLabel.Text = playing ? "Pause" : "Play";
        }

        // After a read-selection hotkey brings the window up, don't let the focus change grab (and clear)
        // the clipboard the hotkey just restored.
        DateTime ignoreClipboardGrabUntil = DateTime.MinValue;

        private void Window_GotFocus_1(object sender, RoutedEventArgs e)
        {
            if (DateTime.UtcNow < ignoreClipboardGrabUntil)
            {
                return;
            }
            // Retrieves data
            IDataObject iData = Clipboard.GetDataObject();
            // Is Data Text?
            if (iData.GetDataPresent(DataFormats.Text))
            {
                string[] textList = textbox.Text.Split('\n');
                string copyData = GetLongest((String)iData.GetData(DataFormats.Text));

                if (!(textList.Contains(copyData) || document.ContainsUnread(copyData)))
                {
                    string preFiltered = (String)iData.GetData(DataFormats.Text) + "\n";

                    Filters.ApplyClipboardFilters(ref preFiltered, filterSettings);

                    textbox.Text = textbox.Text + "\n" + preFiltered;
                    Clipboard.Clear();
                }
            }
        }

        /// <summary>
        /// Current repeat prevention technique (not great).
        /// More complex logic needed i.e. for each split if not in list insert (but where? after one that was?)
        /// </summary>
        private string GetLongest(string p)
        {
            string longest = "";
            foreach (string item in SentenceSplitter.Split(p))
            {
                if (item.Length > longest.Length)
                {
                    longest = item;
                }
            }
            return longest;
        }

        // ---- speaking speed: SAPI scale, -2 (slow) to 10 (fast); one step per +/- click ----
        private const int MinRate = -2, MaxRate = 10, RateStep = 1;
        private bool updatingSpeedUi = false;

        private void SetSpeed(int value, bool save = true)
        {
            rate = Math.Clamp(value, MinRate, MaxRate);
            catalog.EngineFor(selectedVoice).SetRate(rate); // GUESS: engines apply it from the next sentence
            if (save)
            {
                settings.Rate = rate;
                settings.Save();
            }
            updatingSpeedUi = true;
            speedText.Text = rate.ToString();
            speedSlider.Value = rate;
            speedButton.ToolTip = "Speaking speed: " + rate + " (click to change)";
            updatingSpeedUi = false;
        }

        private void SpeedButton_Click(object sender, RoutedEventArgs e) => speedPopup.IsOpen = true;

        private void SpeedMinus_Click(object sender, RoutedEventArgs e) => SetSpeed(rate - RateStep);

        private void SpeedPlus_Click(object sender, RoutedEventArgs e) => SetSpeed(rate + RateStep);

        private OcrOptions selectedOCROption = OcrOptions.Windows;

        /// <summary>One checkable item per OCR engine; the gear button next to Read Region opens it.</summary>
        private void BuildOcrMenu()
        {
            ocrMenu.Items.Clear();
            AddMenuHeading(ocrMenu, "OCR engine");
            foreach (OcrOptions option in Enum.GetValues<OcrOptions>())
            {
                var item = new MenuItem
                {
                    Header = option.ToString(),
                    IsCheckable = true,
                    IsChecked = option == selectedOCROption,
                    Tag = option,
                };
                item.Click += OcrMenuItem_Click;
                ocrMenu.Items.Add(item);
            }
            ocrButton.ToolTip = "OCR engine for Read Region: " + selectedOCROption;
        }

        private void OcrButton_Click(object sender, RoutedEventArgs e)
        {
            ocrMenu.PlacementTarget = ocrButton;
            ocrMenu.Placement = System.Windows.Controls.Primitives.PlacementMode.Bottom;
            ocrMenu.IsOpen = true;
        }

        private void OcrMenuItem_Click(object sender, RoutedEventArgs e)
        {
            selectedOCROption = (OcrOptions)((MenuItem)sender).Tag;
            BuildOcrMenu(); // refresh the check mark
        }

        private bool isCapturing;

        /// <summary>"Read Region": let the user drag a box around text, then OCR and read it.</summary>
        private async void ImageRead(object sender, RoutedEventArgs e)
        {
            if (isCapturing)
            {
                return;
            }
            isCapturing = true;
            try
            {
                // Get our own window out of the way while the user picks a region.
                var previousState = WindowState;
                WindowState = WindowState.Minimized;
                await Task.Delay(200); // let the minimize animation finish

                var region = RegionSelectWindow.Pick();
                if (region == null)
                {
                    WindowState = previousState == WindowState.Minimized ? WindowState.Normal : previousState;
                    return;
                }

                await CaptureAndReadAsync(region.Value);
            }
            finally
            {
                isCapturing = false;
            }
        }

        /// <summary>
        /// Lets the user drag a region, OCRs it and returns the recognized text (not yet queued for reading).
        /// Returns null if the user cancels or nothing was recognized. Used by other entry points such as hotkeys.
        /// </summary>
        public async Task<string?> SelectRegionAndReadAsync()
        {
            if (isCapturing)
            {
                return null;
            }
            isCapturing = true;
            var previousState = WindowState;
            try
            {
                WindowState = WindowState.Minimized;
                await Task.Delay(200);
                var region = RegionSelectWindow.Pick();
                if (region == null)
                {
                    return null;
                }
                await Task.Delay(150);
                using Bitmap bitmap = Utilities.CaptureScreenInArea(region.Value.Location, region.Value.Size);
                var outcome = await Ocr.OcrEngines.For(selectedOCROption).RecognizeAsync(bitmap);
                return outcome.Succeeded && !string.IsNullOrWhiteSpace(outcome.Text) ? outcome.Text : null;
            }
            finally
            {
                WindowState = previousState == WindowState.Minimized ? WindowState.Normal : previousState;
                isCapturing = false;
            }
        }

        private async Task CaptureAndReadAsync(System.Drawing.Rectangle region)
        {
            // Give the desktop a moment to repaint after the overlay closes, so it isn't captured.
            await Task.Delay(150);
            using Bitmap bitmap = Utilities.CaptureScreenInArea(region.Location, region.Size);

            var outcome = await Ocr.OcrEngines.For(selectedOCROption).RecognizeAsync(bitmap);
            if (!outcome.Succeeded || string.IsNullOrWhiteSpace(outcome.Text))
            {
                WindowState = WindowState.Normal;
                textbox.Text = outcome.ErrorMessage ?? "No text found in that region. Try a bigger box, or switch the OCR engine.";
                return;
            }

            string readText = outcome.Text;
            // A sentence cut off at the bottom of the last capture continues at the top of this one.
            // GUESS: only joins when we are on or past the last sentence, as the old savedEnd did.
            string? tail = document.TakeIncompleteTail();
            if (tail != null)
            {
                readText = tail + " " + readText;
            }

            Filters.CombineLines(ref readText);
            readText = readText.Replace('\u00A1', 'i');

            Filters.ApplyOcrFilters(ref readText, filterSettings);

            textbox.Text = readText;

            // Stay minimized while reading; ReadList restores the window when it finishes.
            this.ReadText(this, new RoutedEventArgs());
        }

        /// <summary>
        /// Keyboard shortcuts. Handled in the preview (tunnelling) event so a focused button
        /// never also "clicks" on Space, which is what stopped the old hotkey from working.
        ///   Space                  play / pause (unless typing in the text box)
        ///   Ctrl+Space             play / pause, even while typing
        ///   Left / Right           rewind / skip (unless typing in the text box)
        ///   Ctrl+Left / Ctrl+Right rewind / skip, even while typing
        /// </summary>
        private void Window_PreviewKeyDown(object sender, KeyEventArgs e)
        {
            bool ctrl = (Keyboard.Modifiers & ModifierKeys.Control) != 0;
            bool typing = Keyboard.FocusedElement is TextBox && !ctrl;
            if (typing)
            {
                return;
            }

            switch (e.Key)
            {
                case Key.Space:
                    PlayOrPause();
                    e.Handled = true;
                    break;
                case Key.Left:
                    Rewind();
                    e.Handled = true;
                    break;
                case Key.Right:
                    Skip();
                    e.Handled = true;
                    break;
            }
        }
    }
}
