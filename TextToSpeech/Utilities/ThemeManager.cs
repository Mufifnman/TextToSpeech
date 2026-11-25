using System.Windows;
using System.Windows.Media;
using Microsoft.Win32;

namespace TextToSpeech
{
    /// <summary>
    /// Follows the Windows light/dark app setting and accent color. Colors live in application
    /// resources (ThemeBackground, ThemeText, ...) that the styles reference with DynamicResource,
    /// so switching theme only means replacing those brushes.
    /// </summary>
    public static class ThemeManager
    {
        public static bool IsDark { get; private set; }

        /// <summary>Raised after the brushes were replaced (on the UI thread).</summary>
        public static event Action? Changed;

        private static bool started;

        public static void Start()
        {
            if (started) return;
            started = true;
            Apply();
            SystemEvents.UserPreferenceChanged += (_, e) =>
            {
                if (e.Category is UserPreferenceCategory.General or UserPreferenceCategory.Color or UserPreferenceCategory.VisualStyle)
                {
                    Application.Current?.Dispatcher.BeginInvoke(Apply);
                }
            };
            // Title bar follows the theme for every window as it opens.
            EventManager.RegisterClassHandler(typeof(Window), FrameworkElement.LoadedEvent,
                new RoutedEventHandler((s, _) => StyleWindow((Window)s)));
        }

        public static void StyleWindow(Window window) => WindowAppearance.SetDarkTitleBar(window, IsDark);

        private static bool WindowsUsesLightApps()
        {
            try
            {
                using var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");
                return key?.GetValue("AppsUseLightTheme") is not int v || v != 0;
            }
            catch (Exception)
            {
                return true;
            }
        }

        private static SolidColorBrush Brush(string hex)
        {
            var brush = new SolidColorBrush((Color)ColorConverter.ConvertFromString(hex));
            brush.Freeze();
            return brush;
        }

        public static void Apply()
        {
            var resources = Application.Current?.Resources;
            if (resources == null) return;

            IsDark = !WindowsUsesLightApps();
            Color accentColor = SystemParameters.WindowGlassColor;
            if (accentColor.A == 0) accentColor = Color.FromRgb(0x00, 0x78, 0xD7);
            accentColor.A = 255;
            var accent = new SolidColorBrush(accentColor);
            accent.Freeze();

            void Set(string key, Brush brush) => resources[key] = brush;
            Set("ThemeAccent", accent);

            if (IsDark)
            {
                Set("ThemeBackground", Brush("#202020"));
                Set("ThemeSurface", Brush("#2B2B2B"));
                Set("ThemeText", Brush("#FFFFFF"));
                Set("ThemeSubtle", Brush("#A0A0A0"));
                Set("ThemeBorder", Brush("#404040"));
                Set("ThemeButton", Brush("#323232"));
                Set("ThemeButtonHover", Brush("#3C3C3C"));
                Set("ThemeButtonPressed", Brush("#2A2A2A"));
                Set("ThemeFlatHover", Brush("#26FFFFFF"));
                Set("ThemeFlatPressed", Brush("#40FFFFFF"));
                Set("ThemeOverlay", Brush("#D92B2B2B"));
                Set("ReadSentenceBrush", Brush("#4A4220"));
                Set("ReadWordBrush", Brush("#B86E00"));
                Set("ReadHoverBrush", Brush("#2F4A66"));
                Set("ReadReadBrush", Brush("#8A8A8A"));
            }
            else
            {
                Set("ThemeBackground", Brush("#F3F3F3"));
                Set("ThemeSurface", Brush("#FFFFFF"));
                Set("ThemeText", Brush("#1B1B1B"));
                Set("ThemeSubtle", Brush("#6B6B6B"));
                Set("ThemeBorder", Brush("#D6D6D6"));
                Set("ThemeButton", Brush("#FDFDFD"));
                Set("ThemeButtonHover", Brush("#F0F0F0"));
                Set("ThemeButtonPressed", Brush("#E6E6E6"));
                Set("ThemeFlatHover", Brush("#1F000000"));
                Set("ThemeFlatPressed", Brush("#38000000"));
                Set("ThemeOverlay", Brush("#D9FFFFFF"));
                Set("ReadSentenceBrush", Brush("#FFF3B0"));
                Set("ReadWordBrush", Brush("#FFA000"));
                Set("ReadHoverBrush", Brush("#CFE8FF"));
                Set("ReadReadBrush", Brush("#707070"));
            }

            foreach (Window window in Application.Current!.Windows)
            {
                StyleWindow(window);
            }
            Changed?.Invoke();
        }
    }
}
