using System.IO;
using System.Text.Json;

namespace TextToSpeech
{
    /// <summary>Small per-user settings file in %LocalAppData%\TextToSpeech\settings.json.</summary>
    public sealed class AppSettings
    {
        public string? VoiceId { get; set; }
        public int Rate { get; set; } = 4;

        /// <summary>Reading volume in percent, 0 to 120. Only affects the reading, not the system volume.</summary>
        public int Volume { get; set; } = 100;

        /// <summary>Whole-window opacity, 0.3 to 1.0.</summary>
        public double WindowOpacity { get; set; } = 1.0;

        /// <summary>Silence after each sentence, in milliseconds (0 to 2000). Applies to every voice.</summary>
        public int SentenceGapMs { get; set; } = SentencePacing.DefaultGapMs;

        /// <summary>Neural voices: cut long sentences at commas into pieces made one after another, so speech starts sooner and has no gap before long sentences. Off: each sentence is made in one go. (Edit settings.json; no switch in Settings yet.)</summary>
        public bool ChunkLongSentences { get; set; } = true;

        /// <summary>Opacity of the pop-up panels (speed and volume sliders now; the reading pop-up when it exists), 0.3 to 1.0.</summary>
        public double PopupOpacity { get; set; } = 1.0;

        /// <summary>Which Settings sections are expanded, remembered between visits.</summary>
        public List<string> ExpandedSections { get; set; } = new();

        /// <summary>OCR engine used by Read Region: "Windows" or "Tesseract".</summary>
        public string OcrEngine { get; set; } = "Windows";

        public OcrOptions GetOcrOption() =>
            Enum.TryParse<OcrOptions>(OcrEngine, ignoreCase: true, out var option) && Enum.IsDefined(option) ? option : OcrOptions.Windows;

        /// <summary>Bring the window up when reading starts and minimize it when reading ends (only when the app raised the window itself).</summary>
        public bool AutoShowWhilePlaying { get; set; } = false;

        // ---- always-on mode (tray, popup, local API) ----

        /// <summary>Show the small reading popup above the tray when reading starts and the full window is hidden.</summary>
        public bool ShowReadingPopup { get; set; } = true;

        /// <summary>Hide the popup a moment after reading finishes (off: it stays until dismissed).</summary>
        public bool PopupAutoHide { get; set; } = true;

        /// <summary>Keep the popup open while reading is paused or stopped (off: it hides until reading resumes).</summary>
        public bool PopupKeepOpenWhenPaused { get; set; } = true;

        /// <summary>Clear the text a few seconds after reading finishes (like the Clear button).</summary>
        public bool AutoClearAfterReading { get; set; } = true;

        /// <summary>Seconds to wait after reading finishes before clearing, 1 to 60.</summary>
        public int AutoClearDelaySeconds { get; set; } = 3;

        /// <summary>Also show the popup while the full window is open but not the window being used.</summary>
        public bool ShowPopupWhenWindowUnfocused { get; set; } = false;

        /// <summary>Popup width in pixels, 280 to 900.</summary>
        public double PopupMaxWidth { get; set; } = 420;

        /// <summary>Largest popup height in pixels, 90 to 700 (it is shorter when the text is short).</summary>
        public double PopupMaxHeight { get; set; } = 260;

        /// <summary>Font size of the text in the reading popup, 10 to 32.</summary>
        public double PopupTextSize { get; set; } = 14;

        /// <summary>The window's close button hides it to the tray instead of exiting (Exit is in the tray menu).</summary>
        public bool CloseToTray { get; set; } = true;

        /// <summary>Start hidden in the tray when Windows starts (a value in the current user's Run key).</summary>
        public bool StartWithWindows { get; set; } = false;

        /// <summary>Local API on 127.0.0.1 for other programs. Off until switched on.</summary>
        public bool ApiEnabled { get; set; } = false;

        /// <summary>Preferred API port; a free one is used if it is taken (api.json always says which).</summary>
        public int ApiPort { get; set; } = 47821;

        /// <summary>The one-time "still running in the tray" hint has been shown.</summary>
        public bool TrayHintShown { get; set; } = false;

        /// <summary>Save somewhere else (used by tests so they never touch the real settings file).</summary>
        [System.Text.Json.Serialization.JsonIgnore]
        public string? PathOverride { get; set; }

        private string FilePathFor() => PathOverride ?? FilePath;

        private static string FilePath =>
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "TextToSpeech", "settings.json");

        public static AppSettings Load()
        {
            try
            {
                if (File.Exists(FilePath))
                    return JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(FilePath)) ?? new AppSettings();
            }
            catch (Exception) { /* corrupt or unreadable: start fresh */ }
            return new AppSettings();
        }

        public void Save()
        {
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(FilePathFor())!);
                File.WriteAllText(FilePathFor(), JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true }));
            }
            catch (Exception) { /* settings are a convenience; never crash over them */ }
        }
    }
}
