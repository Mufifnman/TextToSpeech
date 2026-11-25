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
        readonly ReadingDocument document = new ReadingDocument();

        // Reading itself (play, pause, jump, voices, the pause between sentences) lives in ReadingController
        // (TextToSpeech.Core). The window only turns clicks into commands and shows the state.
        readonly ReadingController reading;

        VoiceInfo? selectedVoice
        {
            get => reading.Voice;
            set => reading.Voice = value;
        }

        int rate = 4;
        FilterSettings filterSettings = FilterSettingsStore.Load();

        public MainWindow()
        {
            this.FontFamily = new System.Windows.Media.FontFamily("Segoe UI");
            InitializeComponent();

            reading = new ReadingController(
                document,
                engineFor: catalog.EngineFor,
                fallbackVoice: () => catalog.DefaultFallbackVoice,
                clearPreparedAudio: catalog.ClearUpcoming,
                post: action => Dispatcher.BeginInvoke(action),
                delay: Task.Delay)
            {
                GapMs = settings.SentenceGapMs,
            };
            reading.StateChanged += UpdatePauseButton;
            reading.SentenceStarting += OnSentenceStarting;
            reading.Finished += OnReadingFinished;
            reading.Notice += message => statusText.Text = message;
            reading.VoiceFellBack += _ => BuildVoiceMenu();

            SetSpeed(settings.Rate, save: false);
            SetVolume(settings.Volume, save: false);
            speedSlider.ValueChanged += (_, e) => { if (!updatingSpeedUi) SetSpeed((int)Math.Round(e.NewValue)); };
            volumeSlider.PreviewMouseWheel += (_, e) => { SetVolume(volume + (e.Delta > 0 ? VolumeStep : -VolumeStep)); e.Handled = true; };
            volumeSlider.ValueChanged += (_, e) => { if (!updatingVolumeUi) SetVolume((int)Math.Round(e.NewValue)); };
            RefreshVoices();
            __ReadListDisplay__.Document = document;
            document.ContentChanged += (_, _) => UpdateBringBackVisibility();
            UpdateBringBackVisibility();
            __ReadListDisplay__.WordClicked += reading.JumpTo;

            BuildFiltersMenu();
            RegionReader = this;
            SourceInitialized += (_, _) => { SetupHotkeys(); ApplyWindowSettings(); };
            StateChanged += Window_StateChanged;
            Activated += (_, _) => raise.Activated();
            PreviewMouseDown += (_, _) => raise.UserInteracted();
            ThemeManager.Changed += () => __ReadListDisplay__.RefreshTheme();
            Closed += (_, _) => { reading.Dispose(); hotkeys?.Dispose(); catalog.Dispose(); };
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
            PopupMenus.AddHeading(voiceMenu, "Voice");
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

        private void FiltersButton_Click(object sender, RoutedEventArgs e) =>
            PopupMenus.Open(filtersMenu, filtersButton, System.Windows.Controls.Primitives.PlacementMode.Top);

        private void VoiceButton_Click(object sender, RoutedEventArgs e) =>
            PopupMenus.Open(voiceMenu, voiceButton, System.Windows.Controls.Primitives.PlacementMode.Top);

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
            PopupMenus.AddHeading(filtersMenu, "Filters");
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
            if (!hotkeys.Register(hotkeySettings.GetReadRegion(), () => _ = ReadRegionHotkey(showWindow: true)))
                failed.Add(hotkeySettings.GetReadRegion().ToString());
            if (!hotkeys.Register(hotkeySettings.GetReadRegionQuiet(), () => _ = ReadRegionHotkey(showWindow: false)))
                failed.Add(hotkeySettings.GetReadRegionQuiet().ToString());
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

        private async Task ReadRegionHotkey(bool showWindow)
        {
            if (RegionReader == null)
            {
                ImageRead(this, new RoutedEventArgs());
                return;
            }
            // The overlay shows alone during the drag; the window policy applies only once there is text to read.
            string text = await RegionReader.SelectRegionAndReadAsync();
            if (string.IsNullOrWhiteSpace(text)) return;
            if (showWindow)
            {
                // Same as the read-selection hotkey: only put the window away afterwards if the app brought it up.
                if (WindowState == System.Windows.WindowState.Minimized || !IsVisible) raise.AppRaised();
                ignoreClipboardGrabUntil = DateTime.UtcNow.AddSeconds(2);
                BringToFront();
            }
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
            reading.Interrupt();

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
            reading.Play();
        }

        /// <summary>Reading is about to speak a sentence: bring the window up if the setting says so.</summary>
        private void OnSentenceStarting()
        {
            if (settings.AutoShowWhilePlaying && WindowState == System.Windows.WindowState.Minimized)
            {
                raise.AppRaised();
                WindowState = System.Windows.WindowState.Normal;
            }
        }

        /// <summary>Reading reached the end: put the window away only if the app raised it and nobody touched it.</summary>
        private void OnReadingFinished()
        {
            if (raise.TakeMinimize())
            {
                WindowState = System.Windows.WindowState.Minimized;
            }
        }

        private void Rewind_Click(object sender, RoutedEventArgs e) => reading.Rewind();

        private void Skip_Click(object sender, RoutedEventArgs e) => reading.Skip();

        private void PlayPause_Click(object sender, RoutedEventArgs e) => PlayOrPause();

        /// <summary>
        /// The one Play/Pause button (and Space): while reading it pauses; otherwise it starts reading,
        /// first adding whatever is in the text box, or resumes from the saved position if the box is empty.
        /// </summary>
        private void PlayOrPause()
        {
            if (reading.IsPlaying)
            {
                reading.Pause();
            }
            else if (textbox.Text != "")
            {
                ReadText(this, new RoutedEventArgs());
            }
            else
            {
                reading.TogglePlayPause();
            }
        }

        private void ClearList(object? sender = null, RoutedEventArgs? e = null) => reading.Clear();

        /// <summary>Stops speaking. The position is kept, so Pause/Play (or clicking a word) continues from here.</summary>
        private void StopRead(object? sender = null, RoutedEventArgs? e = null) => reading.Pause();

        private void UpdatePauseButton()
        {
            bool playing = reading.IsPlaying;
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
            IDataObject data = Clipboard.GetDataObject();
            if (data.GetDataPresent(DataFormats.Text))
            {
                string clipboardText = (string)data.GetData(DataFormats.Text);
                string? prepared = ClipboardIntake.Prepare(clipboardText, textbox.Text, document, filterSettings);
                if (prepared != null)
                {
                    textbox.Text = textbox.Text + "\n" + prepared;
                    Clipboard.Clear();
                }
            }
        }

        // ---- speaking speed: SAPI scale, -2 (slow) to 10 (fast) ----
        private const int MinRate = -2, MaxRate = 10, RateStep = 1;
        private bool updatingSpeedUi = false;

        private void SetSpeed(int value, bool save = true)
        {
            rate = Math.Clamp(value, MinRate, MaxRate);
            reading.Rate = rate;
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
            reading.GapMs = settings.SentenceGapMs;
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

            // A sentence cut off at the bottom of the last capture continues at the top of this one.
            // GUESS: only joins when we are on or past the last sentence, as the old savedEnd did.
            string readText = OcrTextProcessor.Process(outcome.Text, document.TakeIncompleteTail(), filterSettings);

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
                    reading.Rewind();
                    e.Handled = true;
                    break;
                case Key.Right:
                    reading.Skip();
                    e.Handled = true;
                    break;
            }
        }
    }
}
