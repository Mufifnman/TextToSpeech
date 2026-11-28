using System.Globalization;
using System.Windows;
using System.Windows.Controls;

namespace TextToSpeech
{
    /// <summary>
    /// The reading rules for agents (Claude Code and other API clients): whether they may read, turn taking and expiry.
    /// A window of its own, apart from the general settings. Every change is applied and saved straight away.
    /// </summary>
    public partial class AgentSettingsWindow : Window
    {
        private static readonly (string Value, string Label)[] AutoReadChoices =
        {
            ("off", "Off"),
            ("announce", "Say that it finished"),
            ("reply", "Read the reply"),
        };

        private static readonly (string Value, string Label)[] FocusChoices =
        {
            ("off", "Do nothing"),
            ("next", "Read it next"),
            ("now", "Read it now (interrupts)"),
        };

        private readonly AppSettings settings;
        private readonly bool loading = true;

        public AgentSettingsWindow(AppSettings settings)
        {
            InitializeComponent();
            this.settings = settings;

            enabledBox.IsChecked = settings.AgentReadingEnabled;
            headphonesBox.IsChecked = settings.AgentRequireHeadphones;
            frontBox.IsChecked = settings.AgentBringToFront;
            expireOnPromptBox.IsChecked = settings.AgentExpireOnPrompt;
            quietFromBox.Text = settings.AgentQuietFrom;
            quietToBox.Text = settings.AgentQuietTo;
            expireBox.IsChecked = settings.AgentPendingExpiryMinutes > 0;
            expiryHoursBox.Text = (settings.AgentPendingExpiryMinutes > 0 ? settings.AgentPendingExpiryMinutes / 60.0 : 4.0)
                .ToString("0.##", CultureInfo.CurrentCulture);
            expiryHoursBox.IsEnabled = expireBox.IsChecked == true;
            foreach (var c in AutoReadChoices) autoReadBox.Items.Add(c.Label);
            foreach (var c in FocusChoices) focusBox.Items.Add(c.Label);
            autoReadBox.SelectedIndex = Math.Max(0, Array.FindIndex(AutoReadChoices, c => c.Value == settings.AgentAutoRead));
            focusBox.SelectedIndex = Math.Max(0, Array.FindIndex(FocusChoices, c => c.Value == settings.AgentFocusMode));

            foreach (CheckBox box in new[] { enabledBox, headphonesBox, frontBox, expireOnPromptBox, expireBox }) box.Click += (_, _) => Changed();
            foreach (TextBox box in new[] { quietFromBox, quietToBox, expiryHoursBox }) box.LostFocus += (_, _) => Changed();
            autoReadBox.SelectionChanged += (_, _) => Changed();
            focusBox.SelectionChanged += (_, _) => Changed();
            loading = false;
        }

        private void Changed()
        {
            if (loading) return;
            settings.AgentReadingEnabled = enabledBox.IsChecked == true;
            settings.AgentRequireHeadphones = headphonesBox.IsChecked == true;
            settings.AgentBringToFront = frontBox.IsChecked == true;
            settings.AgentExpireOnPrompt = expireOnPromptBox.IsChecked == true;
            settings.AgentQuietFrom = quietFromBox.Text.Trim();
            settings.AgentQuietTo = quietToBox.Text.Trim();
            if (autoReadBox.SelectedIndex >= 0) settings.AgentAutoRead = AutoReadChoices[autoReadBox.SelectedIndex].Value;
            if (focusBox.SelectedIndex >= 0) settings.AgentFocusMode = FocusChoices[focusBox.SelectedIndex].Value;

            expiryHoursBox.IsEnabled = expireBox.IsChecked == true;
            if (expireBox.IsChecked != true)
            {
                settings.AgentPendingExpiryMinutes = 0;
            }
            else if (double.TryParse(expiryHoursBox.Text.Trim(), NumberStyles.Float, CultureInfo.CurrentCulture, out double hours) && hours >= 0.1 && hours <= 168)
            {
                settings.AgentPendingExpiryMinutes = (int)Math.Round(hours * 60);
            }
            else
            {
                // Not a usable number: keep the old value and show it again.
                int keep = settings.AgentPendingExpiryMinutes > 0 ? settings.AgentPendingExpiryMinutes : 240;
                settings.AgentPendingExpiryMinutes = keep;
                expiryHoursBox.Text = (keep / 60.0).ToString("0.##", CultureInfo.CurrentCulture);
            }
            settings.Save();
        }

        private void Close_Click(object sender, RoutedEventArgs e) => Close();
    }
}
