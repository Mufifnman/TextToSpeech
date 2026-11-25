using System.Windows;

namespace TextToSpeech
{
    /// <summary>Small settings dialog. Changes are applied live through <paramref name="onChanged"/> and saved.</summary>
    public partial class SettingsWindow : Window
    {
        private readonly AppSettings settings;
        private readonly Action onChanged;
        private bool loading = true;

        public SettingsWindow(AppSettings settings, Action onChanged)
        {
            InitializeComponent();
            this.settings = settings;
            this.onChanged = onChanged;
            opacitySlider.Value = Math.Round(settings.WindowOpacity * 100);
            opacityText.Text = (int)opacitySlider.Value + "%";
            autoShowBox.IsChecked = settings.AutoShowWhilePlaying;
            opacitySlider.ValueChanged += (_, _) => Changed();
            autoShowBox.Click += (_, _) => Changed();
            loading = false;
        }

        private void Changed()
        {
            if (loading) return;
            settings.WindowOpacity = Math.Clamp(opacitySlider.Value / 100.0, 0.3, 1.0);
            settings.AutoShowWhilePlaying = autoShowBox.IsChecked == true;
            opacityText.Text = (int)opacitySlider.Value + "%";
            settings.Save();
            onChanged();
        }

        private void Close_Click(object sender, RoutedEventArgs e) => Close();
    }
}
