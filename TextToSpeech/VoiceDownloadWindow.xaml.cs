using System.Windows;
using TextToSpeech.Speech;

namespace TextToSpeech
{
    public partial class VoiceDownloadWindow : Window
    {
        private CancellationTokenSource? cts;

        public VoiceDownloadWindow()
        {
            InitializeComponent();
            voiceList.ItemsSource = VoiceDownloader.Catalog;
            voiceList.SelectedIndex = 0;
            Closing += (_, _) => cts?.Cancel();
        }

        private async void Download_Click(object sender, RoutedEventArgs e)
        {
            if (voiceList.SelectedItem is not DownloadableVoice voice) return;
            if (VoiceDownloader.IsInstalled(voice))
            {
                statusText.Text = "Already installed.";
                return;
            }

            downloadButton.IsEnabled = false;
            voiceList.IsEnabled = false;
            statusText.Text = "Downloading " + voice.Name + "...";
            cts = new CancellationTokenSource();
            try
            {
                await VoiceDownloader.DownloadAsync(voice, new Progress<double>(p => progress.Value = p), cts.Token);
                statusText.Text = "Done. It is now in the voice list.";
            }
            catch (OperationCanceledException)
            {
                statusText.Text = "Cancelled.";
            }
            catch (Exception ex)
            {
                statusText.Text = "Download failed: " + ex.Message;
            }
            finally
            {
                downloadButton.IsEnabled = true;
                voiceList.IsEnabled = true;
            }
        }

        private void Close_Click(object sender, RoutedEventArgs e) => Close();
    }
}
