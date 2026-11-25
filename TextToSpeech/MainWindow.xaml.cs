using System.Drawing;
using System.Windows;
using System.Windows.Input;
using System.Speech.Synthesis;
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
        SpeechSynthesizer? reader = null;
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
            __ReadListDisplay__.Document = document;
            __ReadListDisplay__.WordClicked += JumpTo;

            wikipediaFilterItem.IsChecked = filterSettings.WikipediaCitations;
            psychologyFilterItem.IsChecked = filterSettings.PsychologyCitations;
            governmentFilterItem.IsChecked = filterSettings.GovernmentCitations;
            ahitFilterItem.IsChecked = filterSettings.AhitStarredQuestions;
            RegionReader = this;
            SourceInitialized += (_, _) => SetupHotkeys();
            Closed += (_, _) => hotkeys?.Dispose();
        }

        private void FilterMenuItem_Click(object sender, RoutedEventArgs e)
        {
            filterSettings.WikipediaCitations = wikipediaFilterItem.IsChecked;
            filterSettings.PsychologyCitations = psychologyFilterItem.IsChecked;
            filterSettings.GovernmentCitations = governmentFilterItem.IsChecked;
            filterSettings.AhitStarredQuestions = ahitFilterItem.IsChecked;
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
            if (!hotkeys.Register(hotkeySettings.GetReadSelection(), () => _ = ReadSelectionHotkey()))
                failed.Add(hotkeySettings.GetReadSelection().ToString());
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

        private async Task ReadSelectionHotkey()
        {
            string text = await SelectionCapture.GetSelectedTextAsync();
            if (text == null) return;
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
            if (hotkeySettings.ReadingSummonsWindow) BringToFront();
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
            if (reader == null) // if we aren't already reading
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
                // Done: maximize so next image can be scanned or line can be copied
                if (this.WindowState == System.Windows.WindowState.Minimized)
                {
                    this.WindowState = System.Windows.WindowState.Normal;
                }
                UpdatePauseButton();
                return;
            }

            int generation = ++speakGeneration;
            var synth = new SpeechSynthesizer { Rate = rate };
            reader = synth;
            synth.SpeakProgress += (s, e) =>
            {
                // SAPI raises this off the UI thread
                Dispatcher.BeginInvoke(() =>
                {
                    if (generation == speakGeneration)
                    {
                        document.ReportSpokenCharacter(spokenOffset + e.CharacterPosition);
                    }
                });
            };
            synth.SpeakCompleted += (s, e) =>
            {
                Dispatcher.BeginInvoke(() =>
                {
                    if (generation != speakGeneration || e.Cancelled)
                    {
                        return;
                    }
                    document.CompleteSentence();
                    SpeakCurrent();
                });
            };
            synth.SpeakAsync(text);
            UpdatePauseButton();
        }

        /// <summary>Stops speaking without moving the reading position.</summary>
        private void CancelSpeech()
        {
            speakGeneration++; // ignore events from the utterance being cancelled
            if (reader != null)
            {
                reader.Dispose();
                reader = null;
            }
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

        private void PlayPause_Click(object sender, RoutedEventArgs e) => TogglePlayPause();

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
            if (reader != null && !paused)
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
            ___PauseButton_.Content = (reader != null && !paused) ? "\u23F8 Pause" : "\u25B6 Play";
        }

        private void Window_GotFocus_1(object sender, RoutedEventArgs e)
        {
            // Retrieves data
            IDataObject iData = Clipboard.GetDataObject();
            // Is Data Text?
            if (iData.GetDataPresent(DataFormats.Text))
            {
                string[] textList = textbox.Text.Split('\n');
                string copyData = GetLongest((String)iData.GetData(DataFormats.Text));

                if (!(textList.Contains(copyData) || document.Contains(copyData)))
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

        private void SpeedChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (e.NewValue == e.OldValue)
            {
                return;
            }
            rate = (int)e.NewValue;

            // GUESS: SAPI applies the new rate to the next sentence, not mid-sentence.
            if (reader != null)
            {
                reader.Rate = rate;
            }
        }

        private OcrOptions selectedOCROption = new OcrOptions();

        private void OcrOptionsComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            var comboBox = sender as ComboBox;
            var selectedItem = comboBox.SelectedItem;

            if (selectedItem != null)
            {
                selectedOCROption = (OcrOptions)comboBox.SelectedItem;
            }
        }

        /// <summary>Screen region last picked by the user, so "Again" can re-read it (e.g. after scrolling).</summary>
        private System.Drawing.Rectangle? lastRegion;

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

                lastRegion = region;
                __ReadAgain.IsEnabled = true;
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
                lastRegion = region;
                __ReadAgain.IsEnabled = true;
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

        /// <summary>"Again": re-capture the last region without picking it again.</summary>
        private async void ReadSameRegion(object sender, RoutedEventArgs e)
        {
            if (isCapturing || lastRegion == null)
            {
                return;
            }
            isCapturing = true;
            try
            {
                WindowState = WindowState.Minimized;
                await Task.Delay(200);
                await CaptureAndReadAsync(lastRegion.Value);
            }
            finally
            {
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
                    TogglePlayPause();
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
