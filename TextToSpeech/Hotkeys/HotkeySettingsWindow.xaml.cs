using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace TextToSpeech.Hotkeys
{
    public partial class HotkeySettingsWindow : Window
    {
        private readonly HotkeySettings _settings;

        public HotkeySettingsWindow(HotkeySettings settings)
        {
            InitializeComponent();
            _settings = settings;
            readBox.Text = settings.GetReadSelection().ToString();
            readShowBox.Text = settings.GetReadSelectionAndShow().ToString();
            summonBox.Text = settings.GetSummonWindow().ToString();
            regionBox.Text = settings.GetReadRegion().ToString();
        }

        private void Capture_PreviewKeyDown(object sender, KeyEventArgs e)
        {
            var box = (TextBox)sender;
            e.Handled = true;
            var key = e.Key == Key.System ? e.SystemKey : e.Key;

            if (key == Key.Back)
            {
                box.Text = HotkeySettings.Default(DefaultName(box)).ToString();
                return;
            }
            // Ignore bare modifier presses and Tab (so focus can still move on with a modifier-less Tab).
            if (key is Key.LeftCtrl or Key.RightCtrl or Key.LeftAlt or Key.RightAlt or Key.LeftShift or Key.RightShift
                or Key.LWin or Key.RWin or Key.None or Key.DeadCharProcessed) return;
            if (key == Key.Tab && Keyboard.Modifiers == ModifierKeys.None) { e.Handled = false; return; }

            var binding = new HotkeyBinding(Keyboard.Modifiers, key);
            if (binding.IsValid) box.Text = binding.ToString(); // require at least one modifier
        }

        private static string DefaultName(TextBox box) => (string)box.Tag switch
        {
            "Read" => nameof(HotkeySettings.ReadSelection),
            "ReadShow" => nameof(HotkeySettings.ReadSelectionAndShow),
            "Summon" => nameof(HotkeySettings.SummonWindow),
            _ => nameof(HotkeySettings.ReadRegion),
        };

        private void Save_Click(object sender, RoutedEventArgs e)
        {
            var texts = new[] { readBox.Text, readShowBox.Text, summonBox.Text, regionBox.Text };
            if (texts.Distinct().Count() != texts.Length)
            {
                MessageBox.Show(this, "Each action needs a different shortcut.", "Hotkeys");
                return;
            }
            _settings.ReadSelection = readBox.Text;
            _settings.ReadSelectionAndShow = readShowBox.Text;
            _settings.SummonWindow = summonBox.Text;
            _settings.ReadRegion = regionBox.Text;
            _settings.Save();
            DialogResult = true;
        }
    }
}
