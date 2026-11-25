using System.Drawing;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
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
            SetVolume(settings.Volume, save: false);
            speedSlider.ValueChanged += (_, e) => { if (!updatingSpeedUi) SetSpeed((int)Math.Round(e.NewValue)); };
            volumeSlider.PreviewMouseWheel += (_, e) => { SetVolume(volume + (e.Delta > 0 ? VolumeStep : -VolumeStep)); e.Handled = true; };
            volumeSlider.ValueChanged += (_, e) => { if (!updatingVolumeUi) SetVolume((int)Math.Round(e.NewValue)); };
            RefreshVoices();
            __ReadListDisplay__.Document = document;
            document.ContentChanged += (_, _) => UpdateBringBackVisibility();
            UpdateBringBackVisibility();
            __ReadListDisplay__.WordClicked += JumpTo;

            BuildFiltersMenu();
            RegionReader = this;
            SourceInitialized += (_, _) => { SetupHotkeys(); ApplyWindowSettings(); };
            StateChanged += Window_StateChanged;
            Activated += (_, _) => raise.Activated();
            PreviewMouseDown += (_, _) => raise.UserInteracted();
            ThemeManager.Changed += () => __ReadListDisplay__.RefreshTheme();
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
            catalog.EngineFor(selectedVoice).Prewarm(selectedVoice);
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
            catalog.EngineFor(v).Prewarm(v);
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

        // Hotkeys another app already owns (shown in the title and in Settings).
        private readonly List<string> failedHotkeys = new();

        private string? HotkeyConflicts() =>
            failedHotkeys.Count == 0 ? null : "In use by another app: " + string.Join(", ", failedHotkeys);

        private void SetupHotkeys()
        {
            hotkeys ??= new GlobalHotkeyManager(this);
            hotkeys.UnregisterAll();
            failedHotkeys.Clear();
            var failed = failedHotkeys;
            if (!hotkeys.Register(hotkeySettings.GetReadSelection(), () => _ = ReadSelectionHotkey(showWindow: false)))
                failed.Add(hotkeySettings.GetReadSelection().ToString());
            if (!hotkeys.Register(hotkeySettings.GetReadSelectionAndShow(), () => _ = ReadSelectionHotkey(showWindow: true)))
                failed.Add(hotkeySettings.GetReadSelectionAndShow().ToString());
            if (!hotkeys.Register(hotkeySettings.GetSummonWindow(), BringToFront))
                failed.Add(hotkeySettings.GetSummonWindow().ToString());
            if (!hotkeys.Register(hotkeySettings.GetReadRegion(), () => _ = ReadRegionHotkey()))
                failed.Add(hotkeySettings.GetReadRegion().ToString());
            if (!hotkeys.Register(hotkeySettings.GetPlayPause(), PlayOrPause))
                failed.Add(hotkeySettings.GetPlayPause().ToString());
            if (!hotkeys.Register(hotkeySettings.GetClearAll(), () => ClearList()))
                failed.Add(hotkeySettings.GetClearAll().ToString());
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

        // Set when the app brought a hidden window up for this read (hotkey or "bring up while playing");
        // the window goes back down when reading ends, unless the user took over the window meanwhile.
        readonly RaiseTracker raise = new(() => DateTime.UtcNow);

        private async Task ReadSelectionHotkey(bool showWindow)
        {
            string text = await SelectionCapture.GetSelectedTextAsync();
            if (text == null) return;
            if (showWindow)
            {
                // Only re-minimize if the window wasn't already up (if the user had it open, leave it alone).
                if (WindowState == System.Windows.WindowState.Minimized || !IsVisible) raise.AppRaised();
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


        // ---- the editable box: one line while idle, the whole window while you edit ----
        private const double OneLineHeight = 30;
        private bool inputExpanded = false;

        private void SetInputExpanded(bool expanded)
        {
            if (inputExpanded == expanded)
            {
                return;
            }
            inputExpanded = expanded;
            UpdateBringBackVisibility();
            if (expanded)
            {
                shrinkInput.Visibility = Visibility.Visible;
                readingRow.Height = new GridLength(OneLineHeight); // reading pane keeps one line
                inputRow.Height = new GridLength(1, GridUnitType.Star);
                textbox.MaxHeight = double.PositiveInfinity;
            }
            else
            {
                shrinkInput.Visibility = Visibility.Collapsed;
                readingRow.Height = new GridLength(1, GridUnitType.Star);
                inputRow.Height = GridLength.Auto;
                textbox.MaxHeight = 34;
            }
        }

        /// <summary>
        /// The bring-back arrows sit on the reading pane. They show while the edit box is open (so the
        /// reading pane is the small one) and only when the reading pane has text to bring back.
        /// </summary>
        private void UpdateBringBackVisibility()
        {
            bringBackPanel.Visibility = inputExpanded && document.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
        }

        private void ShrinkInput_Click(object sender, RoutedEventArgs e) => SetInputExpanded(false);

        private void BringBackLeft_Click(object sender, RoutedEventArgs e) => BringBackToEditBox(document.TakeUnread());

        private void BringBackAll_Click(object sender, RoutedEventArgs e) => BringBackToEditBox(document.TakeAll());

        /// <summary>
        /// Puts text taken out of the reading pane at the top of the edit box, stops any speech (nothing
        /// is left to say from what was taken), and opens the box up for editing.
        /// </summary>
        private void BringBackToEditBox(string text)
        {
            if (string.IsNullOrWhiteSpace(text))
            {
                return;
            }
            CancelSpeech();
            paused = true;
            UpdatePauseButton();

            textbox.Text = textbox.Text == "" ? text : text + "\n" + textbox.Text;
            SetInputExpanded(true);

            // Focusing the box must not trigger the clipboard grab.
            ignoreClipboardGrabUntil = DateTime.UtcNow.AddSeconds(1);
            textbox.Focus();
            textbox.CaretIndex = 0;
        }

        private void Textbox_GotKeyboardFocus(object sender, KeyboardFocusChangedEventArgs e) => SetInputExpanded(true);

        // Clicking in the box while it is small (keyboard focus may already be there) also opens it up.
        private void Textbox_PreviewMouseDown(object sender, MouseButtonEventArgs e) => SetInputExpanded(true);

        // Typing or pasting into a small box that still has focus opens it up again.
        private void Textbox_TextChanged(object sender, TextChangedEventArgs e)
        {
            if (textbox.IsKeyboardFocused && textbox.Text != "")
            {
                SetInputExpanded(true);
            }
        }

        private void Textbox_LostKeyboardFocus(object sender, KeyboardFocusChangedEventArgs e) => SetInputExpanded(false);

        private void ReadText(object sender, RoutedEventArgs e)
        {
            if (this.textbox.Text != "")
            {
                string temp = textbox.Text;
                Filters.CombineLines(ref temp);
                // cut out "read more at"
                document.Append(temp, s => !s.ToLower().Contains("read more at"));
                textbox.Text = "";
                SetInputExpanded(false); // the text moved over: back to the small one-line box
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
                if (raise.TakeMinimize())
                {
                    // The app brought the window up for this read and nobody touched it: put it away again.
                    this.WindowState = System.Windows.WindowState.Minimized;
                }
                UpdatePauseButton();
                return;
            }

            if (settings.AutoShowWhilePlaying && WindowState == System.Windows.WindowState.Minimized)
            {
                raise.AppRaised();
                WindowState = System.Windows.WindowState.Normal; // reading started: bring the window up
            }

            // Tell the engine what comes next so it can prepare it while this sentence plays.
            catalog.EngineFor(selectedVoice).SetUpcoming(document.PeekUpcomingTexts(2), selectedVoice, rate);

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
            // Move on first, so pausing during the gap resumes at the next sentence, not the one just read.
            document.CompleteSentence();
            var gap = SentencePacing.GapAfter(document.IsAtEnd, settings.SentenceGapMs);
            if (gap > TimeSpan.Zero)
            {
                try
                {
                    await Task.Delay(gap, cts.Token);   // jump, pause, stop and clear all cancel the wait
                }
                catch (OperationCanceledException)
                {
                    return;
                }
                if (generation != speakGeneration)
                {
                    return;
                }
            }
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
            catalog.ClearUpcoming();
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

        // ---- speaking speed: SAPI scale, -2 (slow) to 10 (fast) ----
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
            updatingSpeedUi = false;
            speedPopupText.Text = rate.ToString();
            speedButton.ToolTip = $"Speed {rate} (range {MinRate} to {MaxRate})";
        }

        private void SpeedButton_Click(object sender, RoutedEventArgs e) => speedPopup.IsOpen = true;

        private void SpeedMinus_Click(object sender, RoutedEventArgs e) => SetSpeed(rate - RateStep);

        private void SpeedPlus_Click(object sender, RoutedEventArgs e) => SetSpeed(rate + RateStep);

        // ---- reading volume: 0 to 120 percent, reading only (not the system volume) ----
        private const int MinVolume = 0, MaxVolume = 120, VolumeStep = 1;
        private int volume = 100;
        private bool updatingVolumeUi = false;

        private void SetVolume(int value, bool save = true)
        {
            volume = Math.Clamp(value, MinVolume, MaxVolume);
            catalog.SetVolume(volume);
            if (save)
            {
                settings.Volume = volume;
                settings.Save();
            }
            updatingVolumeUi = true;
            volumeText.Text = volume + "%";
            volumeSlider.Value = volume;
            updatingVolumeUi = false;
            volumePopupText.Text = volume + "%";
            volumeButton.ToolTip = $"Reading volume {volume}% (0 to {MaxVolume}%). Above 100% only neural voices get louder; Windows voices stay at 100%.";
        }

        private void VolumeButton_Click(object sender, RoutedEventArgs e) => volumePopup.IsOpen = true;

        // ---- big window: show the - and + beside the speed button; small window: hide them (the slider popup always works) ----
        private void Window_SizeChanged(object sender, SizeChangedEventArgs e)
        {
            var visibility = ResponsiveLayout.ShowSpeedStepButtons(ActualWidth) ? Visibility.Visible : Visibility.Collapsed;
            speedMinus.Visibility = visibility;
            speedPlus.Visibility = visibility;
        }

        // ---- title bar (the window draws its own so that the opacity setting can work) ----
        private void TitleMinimize_Click(object sender, RoutedEventArgs e) => WindowState = System.Windows.WindowState.Minimized;

        private void TitleMaximize_Click(object sender, RoutedEventArgs e) =>
            WindowState = WindowState == System.Windows.WindowState.Maximized ? System.Windows.WindowState.Normal : System.Windows.WindowState.Maximized;

        private void TitleClose_Click(object sender, RoutedEventArgs e) => Close();

        private void Window_StateChanged(object? sender, EventArgs e)
        {
            bool maximized = WindowState == System.Windows.WindowState.Maximized;
            maximizeGlyph.Text = maximized ? "\uE923" : "\uE922";          // restore / maximize
            windowBorder.CornerRadius = new CornerRadius(maximized ? 0 : 8);
            // A borderless window overshoots the screen by its resize border when maximized; pull it back in.
            double overshoot = 8 / VisualTreeHelper.GetDpi(this).DpiScaleX;
            windowBorder.Margin = maximized ? new Thickness(overshoot) : new Thickness(0);
        }

        // ---- settings: opacity and "bring up while playing" ----
        private void Settings_Click(object sender, RoutedEventArgs e)
        {
            var dialog = new SettingsWindow(settings, hotkeySettings, ApplyWindowSettings, SetupHotkeys, HotkeyConflicts) { Owner = this };
            dialog.ShowDialog();
        }

        private void ApplyWindowSettings()
        {
            Opacity = Math.Clamp(settings.WindowOpacity, 0.3, 1.0);
            double popup = Math.Clamp(settings.PopupOpacity, 0.3, 1.0);
            if (speedPopup.Child is UIElement speedPanel) speedPanel.Opacity = popup;
            if (volumePopup.Child is UIElement volumePanel) volumePanel.Opacity = popup;
        }

        private OcrOptions selectedOCROption => settings.GetOcrOption();

        private bool isCapturing;

        /// <summary>
        /// Takes the screenshot first (before any of our own UI exists), shows it frozen full screen, lets the user drag
        /// a box on it and returns just that part. The main window is never shown, restored or minimized for this.
        /// Returns null if the user cancels. The caller disposes the bitmap.
        /// </summary>
        private System.Drawing.Bitmap? PickFrozenCrop()
        {
            var bounds = ScreenGeometry.VirtualScreenPixels();
            using System.Drawing.Bitmap frozen = Utilities.CaptureScreenInArea(bounds.Location, bounds.Size);
            var crop = RegionSelectWindow.PickFrozen(frozen);
            return crop == null ? null : frozen.Clone(crop.Value, frozen.PixelFormat);
        }

        /// <summary>"Read Region": freeze the screen, let the user drag a box around text, then OCR and read it.</summary>
        private async void ImageRead(object sender, RoutedEventArgs e)
        {
            if (isCapturing)
            {
                return;
            }
            isCapturing = true;
            try
            {
                using var crop = PickFrozenCrop();
                if (crop == null)
                {
                    return;
                }
                await ReadCropAsync(crop);
            }
            finally
            {
                isCapturing = false;
            }
        }

        /// <summary>
        /// Same as Read Region but returns the recognized text instead of reading it (not yet queued).
        /// Returns null if the user cancels or nothing was recognized. Used by other entry points such as hotkeys.
        /// </summary>
        public async Task<string?> SelectRegionAndReadAsync()
        {
            if (isCapturing)
            {
                return null;
            }
            isCapturing = true;
            try
            {
                using var crop = PickFrozenCrop();
                if (crop == null)
                {
                    return null;
                }
                var outcome = await Ocr.OcrEngines.For(selectedOCROption).RecognizeAsync(crop);
                return outcome.Succeeded && !string.IsNullOrWhiteSpace(outcome.Text) ? outcome.Text : null;
            }
            finally
            {
                isCapturing = false;
            }
        }

        private async Task ReadCropAsync(Bitmap crop)
        {
            var outcome = await Ocr.OcrEngines.For(selectedOCROption).RecognizeAsync(crop);
            if (!outcome.Succeeded || string.IsNullOrWhiteSpace(outcome.Text))
            {
                string message = outcome.ErrorMessage ?? "No text found in that region. Try a bigger box, or switch the OCR engine.";
                textbox.Text = message;
                statusText.Text = message;
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
            raise.UserInteracted();
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
