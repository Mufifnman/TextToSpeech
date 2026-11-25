using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
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
            var vertical = new ScrollBar { Orientation = Orientation.Vertical };
            var horizontal = new ScrollBar { Orientation = Orientation.Horizontal };
            var sliderH = new Slider { Orientation = Orientation.Horizontal, Minimum = 0, Maximum = 10 };
            var sliderV = new Slider { Orientation = Orientation.Vertical, Minimum = 0, Maximum = 10, Height = 100 };
            var tip = new ToolTip { Content = "tip" };
            var viewer = new ScrollViewer { Content = new TextBlock { Text = "x" } };
            var section = new Expander { Header = "Section", Content = new TextBlock { Text = "inside" }, IsExpanded = true };
            var menu = new ContextMenu();
            menu.Items.Add(item);
            menu.Items.Add(new Separator());

            foreach (FrameworkElement element in new FrameworkElement[] { button, textBox, item, menu, vertical, horizontal, sliderH, sliderV, tip, viewer, section })
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

    // Windows refuses to make a normal WPF window see-through after it exists, so the main window draws
    // its own title bar and allows transparency; that is what lets the opacity setting work.
    [Fact]
    public void MainWindow_AllowsTransparencyAndTakesAnOpacity()
    {
        RunSta(() =>
        {
            LoadStyles();
            var main = new MainWindow();
            Assert.True(main.AllowsTransparency);
            Assert.Equal(WindowStyle.None, main.WindowStyle);
            main.Opacity = 0.5;
            Assert.Equal(0.5, main.Opacity);
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

public class OpacityTests
{

    private static void RunSta(Action action)
    {
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            try { action(); } catch (Exception ex) { failure = ex; }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();
        if (failure != null) throw new Exception("STA test failed: " + failure, failure);
    }

    [Fact]
    public void MovingTheSettingsSlider_AppliesTheValueLive_AndSavesIt()
    {
        string file = Path.Combine(Path.GetTempPath(), "tts-test-settings-" + Guid.NewGuid() + ".json");
        try
        {
            RunSta(() =>
            {
                var settings = new TextToSpeech.AppSettings { PathOverride = file };
                int applied = 0;
                var dialog = new TextToSpeech.SettingsWindow(settings, () => applied++);
                var slider = (System.Windows.Controls.Slider)dialog.FindName("opacitySlider");

                slider.Value = 55;

                Assert.Equal(0.55, settings.WindowOpacity, 3);
                Assert.True(applied >= 1);                       // the window is told right away
                Assert.Contains("0.55", File.ReadAllText(file)); // and it is saved
            });
        }
        finally
        {
            if (File.Exists(file)) File.Delete(file);
        }
    }
}

internal static class Sta
{
    public static void Run(Action action)
    {
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            try { action(); } catch (Exception ex) { failure = ex; }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();
        if (failure != null) throw new Exception("STA test failed: " + failure, failure);
    }
}

public class SettingsSectionTests
{
    [Fact]
    public void SectionsRememberTheirOpenState_AndOpenAgainTheNextTime()
    {
        string file = Path.Combine(Path.GetTempPath(), "tts-test-settings-" + Guid.NewGuid() + ".json");
        try
        {
            Sta.Run(() =>
            {
                var settings = new TextToSpeech.AppSettings { PathOverride = file };
                var first = new TextToSpeech.SettingsWindow(settings, () => { });
                var hotkeys = (System.Windows.Controls.Expander)first.FindName("hotkeyExpander");
                var ocr = (System.Windows.Controls.Expander)first.FindName("ocrExpander");
                Assert.False(hotkeys.IsExpanded);   // sections start collapsed
                Assert.False(ocr.IsExpanded);

                hotkeys.IsExpanded = true;
                Assert.Contains("hotkeys", settings.ExpandedSections);
                Assert.DoesNotContain("ocr", settings.ExpandedSections);

                var second = new TextToSpeech.SettingsWindow(settings, () => { });
                Assert.True(((System.Windows.Controls.Expander)second.FindName("hotkeyExpander")).IsExpanded);
                Assert.False(((System.Windows.Controls.Expander)second.FindName("ocrExpander")).IsExpanded);

                hotkeys.IsExpanded = false;
                Assert.DoesNotContain("hotkeys", settings.ExpandedSections);
            });
        }
        finally
        {
            if (File.Exists(file)) File.Delete(file);
        }
    }

    [Fact]
    public void PopupOpacitySlider_IsSeparateFromTheWindowOpacitySlider()
    {
        string file = Path.Combine(Path.GetTempPath(), "tts-test-settings-" + Guid.NewGuid() + ".json");
        try
        {
            Sta.Run(() =>
            {
                var settings = new TextToSpeech.AppSettings { PathOverride = file };
                var dialog = new TextToSpeech.SettingsWindow(settings, () => { });

                ((System.Windows.Controls.Slider)dialog.FindName("popupOpacitySlider")).Value = 60;

                Assert.Equal(0.6, settings.PopupOpacity, 3);
                Assert.Equal(1.0, settings.WindowOpacity, 3);   // the window's own opacity is untouched
            });
        }
        finally
        {
            if (File.Exists(file)) File.Delete(file);
        }
    }
}

public class OcrSettingTests
{
    [Theory]
    [InlineData("Windows", TextToSpeech.OcrOptions.Windows)]
    [InlineData("Tesseract", TextToSpeech.OcrOptions.Tesseract)]
    [InlineData("tesseract", TextToSpeech.OcrOptions.Tesseract)]
    [InlineData("garbage", TextToSpeech.OcrOptions.Windows)]
    [InlineData("99", TextToSpeech.OcrOptions.Windows)]
    public void SavedOcrEngine_MapsToTheOption_AndBadValuesFallBackToWindows(string saved, TextToSpeech.OcrOptions expected)
    {
        Assert.Equal(expected, new TextToSpeech.AppSettings { OcrEngine = saved }.GetOcrOption());
    }

    [Fact]
    public void ChoosingTesseractInSettings_IsAppliedAndSaved()
    {
        string file = Path.Combine(Path.GetTempPath(), "tts-test-settings-" + Guid.NewGuid() + ".json");
        try
        {
            Sta.Run(() =>
            {
                var settings = new TextToSpeech.AppSettings { PathOverride = file };
                int applied = 0;
                var dialog = new TextToSpeech.SettingsWindow(settings, () => applied++);
                var tesseract = (System.Windows.Controls.RadioButton)dialog.FindName("ocrTesseractRadio");
                tesseract.IsChecked = true;
                tesseract.RaiseEvent(new System.Windows.RoutedEventArgs(System.Windows.Controls.Primitives.ButtonBase.ClickEvent));

                Assert.Equal(TextToSpeech.OcrOptions.Tesseract, settings.GetOcrOption());
                Assert.True(applied >= 1);
                Assert.Contains("Tesseract", File.ReadAllText(file));
            });
        }
        finally
        {
            if (File.Exists(file)) File.Delete(file);
        }
    }
}

public class RaiseTrackerTests
{
    private sealed class Clock
    {
        public DateTime Now = new(2026, 1, 1, 12, 0, 0, DateTimeKind.Utc);
        public void Advance(double seconds) => Now = Now.AddSeconds(seconds);
    }

    private static (TextToSpeech.RaiseTracker tracker, Clock clock) Make()
    {
        var clock = new Clock();
        return (new TextToSpeech.RaiseTracker(() => clock.Now), clock);
    }

    [Fact]
    public void RaisedByApp_ThenReadFinishes_Minimizes()
    {
        var (t, clock) = Make();
        t.AppRaised();
        t.Activated();              // the app's own raise activates the window: ignored
        clock.Advance(20);
        Assert.True(t.TakeMinimize());
        Assert.False(t.TakeMinimize()); // only once
    }

    [Fact]
    public void LosingFocusAndGettingItBackByHand_CancelsTheMinimize()
    {
        var (t, clock) = Make();
        t.AppRaised();
        t.Activated();
        clock.Advance(5);           // user clicks elsewhere, then comes back later
        t.Activated();
        Assert.False(t.TakeMinimize());
    }

    [Fact]
    public void ClickingOrTypingInTheWindow_CancelsTheMinimize()
    {
        var (t, _) = Make();
        t.AppRaised();
        t.UserInteracted();
        Assert.False(t.TakeMinimize());
    }

    [Fact]
    public void NeverRaisedByTheApp_NeverMinimizes()
    {
        var (t, clock) = Make();
        t.Activated();
        clock.Advance(30);
        Assert.False(t.TakeMinimize());
    }

    [Fact]
    public void ActivationRightAfterTheRaise_IsNotAManualFocus()
    {
        var (t, clock) = Make();
        t.AppRaised();
        clock.Advance(0.5);         // inside the grace period
        t.Activated();
        Assert.True(t.RaisedByApp);
    }
}
