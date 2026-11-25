using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using TextToSpeech;
using Xunit;

namespace TextToSpeech.Windows.Tests;

public class ThemeTests
{
    // WPF allows one Application per process and ties it to one thread, so every test runs on the
    // same background STA thread with its own dispatcher.
    private static readonly Lazy<System.Windows.Threading.Dispatcher> Ui = new(() =>
    {
        System.Windows.Threading.Dispatcher? dispatcher = null;
        var ready = new ManualResetEventSlim();
        var thread = new Thread(() =>
        {
            dispatcher = System.Windows.Threading.Dispatcher.CurrentDispatcher;
            ready.Set();
            System.Windows.Threading.Dispatcher.Run();
        }) { IsBackground = true };
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        ready.Wait();
        return dispatcher!;
    });

    private static void RunSta(Action action)
    {
        Exception? failure = null;
        Ui.Value.Invoke(() =>
        {
            try { action(); } catch (Exception ex) { failure = ex; }
        });
        if (failure != null) throw new Exception("UI test failed: " + failure, failure);
    }

    private static Application LoadStyles()
    {
        var app = Application.Current ?? new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
        if (app.Resources.MergedDictionaries.Count == 0)
        {
            app.Resources.MergedDictionaries.Add(new ResourceDictionary
            {
                Source = new Uri("pack://application:,,,/TextToSpeech;component/Styles.xaml")
            });
        }
        ThemeManager.Apply();
        return app;
    }

    [Fact]
    public void StylesFileParses_AndThemedControlsBuildTheirTemplates()
    {
        RunSta(() =>
        {
            LoadStyles();

            var button = new Button { Content = "x" };
            var textBox = new TextBox { Text = "x" };
            var item = new MenuItem { Header = "item", IsCheckable = true, IsChecked = true };
            item.Items.Add(new MenuItem { Header = "sub" });   // submenu header with a popup
            var menu = new ContextMenu();
            menu.Items.Add(item);
            menu.Items.Add(new Separator());

            foreach (FrameworkElement element in new FrameworkElement[] { button, textBox, item, menu })
            {
                element.ApplyTemplate();
                element.Measure(new Size(300, 100));
            }
            Assert.True(button.DesiredSize.Width > 0);
        });
    }

    [Fact]
    public void ThemeDefinesEveryBrushTheWindowsUse()
    {
        RunSta(() =>
        {
            var app = LoadStyles();
            string[] keys =
            {
                "ThemeBackground", "ThemeSurface", "ThemeText", "ThemeSubtle", "ThemeBorder", "ThemeAccent",
                "ThemeButton", "ThemeButtonHover", "ThemeButtonPressed", "ThemeFlatHover", "ThemeFlatPressed", "ThemeOverlay",
                "ReadSentenceBrush", "ReadWordBrush", "ReadHoverBrush", "ReadReadBrush",
            };
            foreach (string key in keys)
            {
                Assert.True(app.Resources[key] is Brush, "missing theme brush " + key);
            }
        });
    }

    // Catches XAML that compiles but fails when the window is built (missing resources, bad templates).
    [Fact]
    public void MainWindowAndDialogsBuildWithTheThemedStyles()
    {
        RunSta(() =>
        {
            LoadStyles();

            var main = new MainWindow();
            main.ApplyTemplate();
            main.Measure(new Size(780, 400));
            main.Arrange(new Rect(0, 0, 780, 400));
            main.UpdateLayout();

            var settings = new SettingsWindow(new AppSettings(), () => { });
            settings.ApplyTemplate();
            settings.Measure(new Size(400, 300));
        });
    }
}
