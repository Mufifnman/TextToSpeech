using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using TextToSpeech.Hotkeys;

namespace TextToSpeech
{
    /// <summary>
    /// Settings: window opacity, bring-up while playing, OCR engine and the hotkeys. Every change is applied
    /// and saved straight away, so there is no Save button.
    /// </summary>
    public partial class SettingsWindow : Window
    {
        private readonly AppSettings settings;
        private readonly HotkeySettings hotkeys;
        private readonly Action onChanged;
        private readonly Action onHotkeysChanged;
        private readonly Func<string?> hotkeyConflicts;
        private bool loading = true;

        /// <summary>Set by the main window: whether the local API is running, its port and token (for the connection line).</summary>
        public Func<(bool Running, int Port, string? Token)>? ApiInfo { get; set; }

        public SettingsWindow(AppSettings settings, Action onChanged)
            : this(settings, new HotkeySettings(), onChanged, () => { }, () => null)
        {
        }

        public SettingsWindow(AppSettings settings, HotkeySettings hotkeys, Action onChanged, Action onHotkeysChanged, Func<string?> hotkeyConflicts)
        {
            InitializeComponent();
            this.settings = settings;
            this.hotkeys = hotkeys;
            this.onChanged = onChanged;
            this.onHotkeysChanged = onHotkeysChanged;
            this.hotkeyConflicts = hotkeyConflicts;

            opacitySlider.Value = Math.Round(settings.WindowOpacity * 100);
            opacityText.Text = (int)opacitySlider.Value + "%";
            autoShowBox.IsChecked = settings.AutoShowWhilePlaying;
            startWithWindowsBox.IsChecked = settings.StartWithWindows;
            closeToTrayBox.IsChecked = settings.CloseToTray;
            showPopupBox.IsChecked = settings.ShowReadingPopup;
            popupAutoHideBox.IsChecked = settings.PopupAutoHide;
            popupKeepBox.IsChecked = settings.PopupKeepOpenWhenPaused;
            apiBox.IsChecked = settings.ApiEnabled;
            foreach (CheckBox box in new[] { startWithWindowsBox, closeToTrayBox, showPopupBox, popupAutoHideBox, popupKeepBox, apiBox })
            {
                box.Click += (_, _) => Changed();
            }
            popupAutoHideBox.IsEnabled = popupKeepBox.IsEnabled = showPopupBox.IsChecked == true;
            ContentRendered += (_, _) => ShowApiInfo();
            gapSlider.Value = Math.Clamp(settings.SentenceGapMs, SentencePacing.MinGapMs, SentencePacing.MaxGapMs);
            gapText.Text = (int)gapSlider.Value + " ms";
            gapSlider.ValueChanged += (_, _) => Changed();
            popupOpacitySlider.Value = Math.Round(settings.PopupOpacity * 100);
            popupOpacityText.Text = (int)popupOpacitySlider.Value + "%";
            popupOpacitySlider.ValueChanged += (_, _) => Changed();
            popupTextSizeSlider.Value = Math.Clamp(settings.PopupTextSize, 10, 32);
            popupPreviewText.FontSize = popupTextSizeSlider.Value;
            popupTextSizeText.Text = (int)popupTextSizeSlider.Value + " pt";
            popupTextSizeSlider.ValueChanged += (_, _) => Changed();
            BuildOcrMenu();
            opacitySlider.ValueChanged += (_, _) => Changed();
            autoShowBox.Click += (_, _) => Changed();

            foreach (TextBox box in HotkeyBoxes())
            {
                box.Text = GetBinding((string)box.Tag).ToString();
            }
            ShowConflicts();

            // Sections that take more than a line are collapsible; the open ones are remembered.
            foreach (Expander section in new[] { shellExpander, popupExpander, opacityExpander, hotkeyExpander })
            {
                section.IsExpanded = settings.ExpandedSections.Contains((string)section.Tag);
                section.Expanded += (_, _) => RememberSection(section, true);
                section.Collapsed += (_, _) => RememberSection(section, false);
            }
            loading = false;
        }

        private void Changed()
        {
            if (loading) return;
            settings.WindowOpacity = Math.Clamp(opacitySlider.Value / 100.0, 0.3, 1.0);
            settings.AutoShowWhilePlaying = autoShowBox.IsChecked == true;
            settings.StartWithWindows = startWithWindowsBox.IsChecked == true;
            settings.CloseToTray = closeToTrayBox.IsChecked == true;
            settings.ShowReadingPopup = showPopupBox.IsChecked == true;
            settings.PopupAutoHide = popupAutoHideBox.IsChecked == true;
            settings.PopupKeepOpenWhenPaused = popupKeepBox.IsChecked == true;
            settings.ApiEnabled = apiBox.IsChecked == true;
            popupAutoHideBox.IsEnabled = popupKeepBox.IsEnabled = showPopupBox.IsChecked == true;
            settings.SentenceGapMs = (int)Math.Round(gapSlider.Value);
            gapText.Text = settings.SentenceGapMs + " ms";
            settings.PopupOpacity = Math.Clamp(popupOpacitySlider.Value / 100.0, 0.3, 1.0);
            popupOpacityText.Text = (int)popupOpacitySlider.Value + "%";
            opacityText.Text = (int)opacitySlider.Value + "%";
            settings.PopupTextSize = popupTextSizeSlider.Value;
            popupPreviewText.FontSize = popupTextSizeSlider.Value;
            popupTextSizeText.Text = (int)popupTextSizeSlider.Value + " pt";
            settings.Save();
            onChanged();
            ShowApiInfo();
        }

        private void ShowApiInfo()
        {
            var info = ApiInfo?.Invoke();
            if (settings.ApiEnabled && info is { Running: true })
            {
                apiInfoText.Text = $"Running on 127.0.0.1:{info.Value.Port}. Programs read the port and token from %LocalAppData%\\TextToSpeech\\api.json.";
                copyTokenButton.Visibility = Visibility.Visible;
            }
            else
            {
                apiInfoText.Text = settings.ApiEnabled ? "Starting... (if this stays, the port could not be opened; see the status line in the main window)" : "Off.";
                copyTokenButton.Visibility = Visibility.Collapsed;
            }
        }

        /// <summary>
        /// Opens the folder with api.json, selected. The token is deliberately not put on the clipboard: the main window
        /// reads new clipboard text aloud, which would speak (and keep) the token.
        /// </summary>
        private void ShowConnectionFile_Click(object sender, RoutedEventArgs e)
        {
            string path = TextToSpeech.Api.ConnectionInfo.DefaultPath;
            try
            {
                if (System.IO.File.Exists(path))
                {
                    System.Diagnostics.Process.Start("explorer.exe", $"/select,\"{path}\"");
                }
            }
            catch (Exception) { /* nothing useful to do if Explorer cannot start */ }
        }

        // ---- OCR engine: a small drop-down (a button that opens a themed menu) ----
        private void BuildOcrMenu()
        {
            var menu = new ContextMenu();
            foreach (OcrOptions option in Enum.GetValues<OcrOptions>())
            {
                var item = new MenuItem { Header = option.ToString(), IsCheckable = true, IsChecked = option == settings.GetOcrOption(), Tag = option };
                item.Click += (_, _) => ChooseOcr(option);
                menu.Items.Add(item);
            }
            ocrButton.ContextMenu = menu;
            ocrButtonText.Text = settings.GetOcrOption().ToString();
        }

        private void OcrButton_Click(object sender, RoutedEventArgs e)
        {
            if (ocrButton.ContextMenu != null)
            {
                PopupMenus.Open(ocrButton.ContextMenu, ocrButton, System.Windows.Controls.Primitives.PlacementMode.Bottom);
            }
        }

        private void ChooseOcr(OcrOptions option)
        {
            settings.OcrEngine = option.ToString();
            BuildOcrMenu();   // refresh the check mark and the button text
            settings.Save();
            onChanged();
        }

        private void RememberSection(Expander section, bool open)
        {
            if (loading) return;
            string name = (string)section.Tag;
            settings.ExpandedSections.Remove(name);
            if (open) settings.ExpandedSections.Add(name);
            settings.Save();
        }

        // ---- hotkeys ----
        private IEnumerable<TextBox> HotkeyBoxes() => new[] { readBox, readShowBox, summonBox, regionBox, regionQuietBox, playPauseBox, clearBox };

        private HotkeyBinding GetBinding(string name) => name switch
        {
            nameof(HotkeySettings.ReadSelection) => hotkeys.GetReadSelection(),
            nameof(HotkeySettings.ReadSelectionAndShow) => hotkeys.GetReadSelectionAndShow(),
            nameof(HotkeySettings.SummonWindow) => hotkeys.GetSummonWindow(),
            nameof(HotkeySettings.ReadRegionQuiet) => hotkeys.GetReadRegionQuiet(),
            nameof(HotkeySettings.PlayPause) => hotkeys.GetPlayPause(),
            nameof(HotkeySettings.ClearAll) => hotkeys.GetClearAll(),
            _ => hotkeys.GetReadRegion(),
        };

        private void SetBinding(string name, HotkeyBinding binding)
        {
            string text = binding.ToString();
            switch (name)
            {
                case nameof(HotkeySettings.ReadSelection): hotkeys.ReadSelection = text; break;
                case nameof(HotkeySettings.ReadSelectionAndShow): hotkeys.ReadSelectionAndShow = text; break;
                case nameof(HotkeySettings.SummonWindow): hotkeys.SummonWindow = text; break;
                case nameof(HotkeySettings.ReadRegionQuiet): hotkeys.ReadRegionQuiet = text; break;
                case nameof(HotkeySettings.PlayPause): hotkeys.PlayPause = text; break;
                case nameof(HotkeySettings.ClearAll): hotkeys.ClearAll = text; break;
                default: hotkeys.ReadRegion = text; break;
            }
        }

        private void Capture_PreviewKeyDown(object sender, KeyEventArgs e)
        {
            var box = (TextBox)sender;
            string name = (string)box.Tag;
            e.Handled = true;
            var key = e.Key == Key.System ? e.SystemKey : e.Key;

            HotkeyBinding binding;
            if (key == Key.Back)
            {
                binding = HotkeySettings.Default(name);
            }
            else
            {
                // Ignore bare modifier presses; a shortcut needs a modifier plus a key.
                if (key is Key.LeftCtrl or Key.RightCtrl or Key.LeftAlt or Key.RightAlt or Key.LeftShift or Key.RightShift
                    or Key.LWin or Key.RWin or Key.None or Key.DeadCharProcessed) return;
                if (key == Key.Tab && Keyboard.Modifiers == ModifierKeys.None) { e.Handled = false; return; }
                binding = new HotkeyBinding(Keyboard.Modifiers, key);
                if (!binding.IsValid) return;
            }

            // Each action needs its own shortcut.
            foreach (TextBox other in HotkeyBoxes())
            {
                if (other != box && GetBinding((string)other.Tag) == binding)
                {
                    ShowMessage($"{binding} is already used by another action here.");
                    return;
                }
            }

            SetBinding(name, binding);
            box.Text = binding.ToString();
            hotkeys.Save();
            onHotkeysChanged();   // re-registers the keys and refreshes the conflict warning
            ShowConflicts();
        }

        private void ShowConflicts()
        {
            string? conflicts = hotkeyConflicts();
            ShowMessage(conflicts);
        }

        private void ShowMessage(string? text)
        {
            hotkeyMessage.Text = text ?? "";
            hotkeyMessage.Visibility = string.IsNullOrEmpty(text) ? Visibility.Collapsed : Visibility.Visible;
        }

        private void Close_Click(object sender, RoutedEventArgs e) => Close();
    }
}
