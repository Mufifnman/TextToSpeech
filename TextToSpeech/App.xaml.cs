using System.Configuration;
using System.Data;
using System.Windows;

namespace TextToSpeech
{
    /// <summary>
    /// Interaction logic for App.xaml
    /// </summary>
    public partial class App : Application
    {
        protected override void OnStartup(StartupEventArgs e)
        {
            // Theme brushes must exist before the first window is created.
            ThemeManager.Start();
            base.OnStartup(e);
        }
    }

}
