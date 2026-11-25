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
            popupOpacitySlider.Value = Math.Round(settings.PopupOpacity * 100);
            popupOpacityText.Text = (int)popupOpacitySlider.Value + "%";
            popupOpacitySlider.ValueChanged += (_, _) => Changed();
            ocrWindowsRadio.IsChecked = settings.GetOcrOption() == OcrOptions.Windows;
            ocrTesseractRadio.IsChecked = settings.GetOcrOption() == OcrOptions.Tesseract;
            opacitySlider.ValueChanged += (_, _) => Changed();
            autoShowBox.Click += (_, _) => Changed();
            ocrWindowsRadio.Click += (_, _) => Changed();
            ocrTesseractRadio.Click += (_, _) => Changed();

            foreach (TextBox box in HotkeyBoxes())
            {
                box.Text = GetBinding((string)box.Tag).ToString();
            }
            ShowConflicts();

            // Sections that take more than a line are collapsible; the open ones are remembered.
            foreach (Expander section in new[] { ocrExpander, hotkeyExpander })
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
            settings.PopupOpacity = Math.Clamp(popupOpacitySlider.Value / 100.0, 0.3, 1.0);
            popupOpacityText.Text = (int)popupOpacitySlider.Value + "%";
            settings.OcrEngine = ocrTesseractRadio.IsChecked == true ? nameof(OcrOptions.Tesseract) : nameof(OcrOptions.Windows);
            opacityText.Text = (int)opacitySlider.Value + "%";
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
        private IEnumerable<TextBox> HotkeyBoxes() => new[] { readBox, readShowBox, summonBox, regionBox, playPauseBox, clearBox };

        private HotkeyBinding GetBinding(string name) => name switch
        {
            nameof(HotkeySettings.ReadSelection) => hotkeys.GetReadSelection(),
            nameof(HotkeySettings.ReadSelectionAndShow) => hotkeys.GetReadSelectionAndShow(),
            nameof(HotkeySettings.SummonWindow) => hotkeys.GetSummonWindow(),
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
