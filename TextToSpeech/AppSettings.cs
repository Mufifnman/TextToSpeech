using System.IO;
using TextToSpeech.Core.IO;

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

        /// <summary>
        /// Seconds the popup stays open after reading finishes (when "Hide popup when reading finishes" is on). It never
        /// hides before Auto clear clears the text, so the effective time is the longer of this and the Auto clear delay.
        /// 0 means just follow Auto clear. Range 0 to 120.
        /// </summary>
        public int PopupStayOpenSeconds { get; set; } = 0;

        /// <summary>Show markdown (lists, headings, code) laid out, with the markup neither shown nor spoken.</summary>
        public bool FormatMarkdown { get; set; } = true;

        /// <summary>Read through question pauses without stopping, from the start. Off: reading stops after each marked question until you continue.</summary>
        public bool PlayAllByDefault { get; set; } = false;

        /// <summary>Font size of the reading text in the main window, 10 to 40.</summary>
        public double MainTextSize { get; set; } = 14;

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

        // ---- Agents (Claude Code and other programs that read through the local API) ----

        /// <summary>Agents may read aloud at all. Off silences everything that comes through the API with an agent id.</summary>
        public bool AgentReadingEnabled { get; set; } = true;

        /// <summary>What the Claude Code plugin reads when a reply finishes: off, announce ("Claude Code finished") or reply.</summary>
        public string AgentAutoRead { get; set; } = "off";

        /// <summary>Agents read only while headphones or a headset are the audio output.</summary>
        public bool AgentRequireHeadphones { get; set; } = false;

        /// <summary>Quiet hours "HH:mm" to "HH:mm" (local time, may cross midnight); empty means none.</summary>
        public string AgentQuietFrom { get; set; } = "";
        public string AgentQuietTo { get; set; } = "";

        /// <summary>Switching to an agent's window that has waiting text: off, next (read after the current one) or now (interrupt).</summary>
        public string AgentFocusMode { get; set; } = "next";

        /// <summary>Bring the reading agent's window to the front when its turn starts. Off: nothing on screen is moved.</summary>
        public bool AgentBringToFront { get; set; } = false;

        /// <summary>Waiting agent text older than this many minutes is dropped; 0 never. Default 4 hours.</summary>
        public int AgentPendingExpiryMinutes { get; set; } = 240;

        /// <summary>Typing a prompt in an agent's window drops the text that was waiting for it.</summary>
        public bool AgentExpireOnPrompt { get; set; } = true;

        public TextToSpeech.Api.Arbitration.ReadPolicy ToReadPolicy() => new()
        {
            Enabled = AgentReadingEnabled,
            AutoRead = AgentAutoRead is "announce" or "reply" ? AgentAutoRead : "off",
            RequireHeadphones = AgentRequireHeadphones,
            QuietFrom = string.IsNullOrWhiteSpace(AgentQuietFrom) ? null : AgentQuietFrom,
            QuietTo = string.IsNullOrWhiteSpace(AgentQuietTo) ? null : AgentQuietTo,
            FocusMode = AgentFocusMode is "off" or "now" ? AgentFocusMode : "next",
            BringToFront = AgentBringToFront,
            PendingExpiryMinutes = Math.Clamp(AgentPendingExpiryMinutes, 0, 10080),
            ExpireOnPrompt = AgentExpireOnPrompt,
        };

        /// <summary>The one-time "still running in the tray" hint has been shown.</summary>
        public bool TrayHintShown { get; set; } = false;

        /// <summary>Save somewhere else (used by tests so they never touch the real settings file).</summary>
        [System.Text.Json.Serialization.JsonIgnore]
        public string? PathOverride { get; set; }

        private string FilePathFor() => PathOverride ?? FilePath;

        private static string FilePath =>
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "TextToSpeech", "settings.json");

        // Remembers the file and version that were loaded so Save can keep a backup of a newer file.
        private JsonFileStore<AppSettings>? store;

        public static AppSettings Load()
        {
            // A corrupt file is kept as settings.json.bad and defaults are used; an unreadable one is left alone.
            var store = new JsonFileStore<AppSettings>(FilePath);
            var settings = store.Load();
            settings.store = store;
            return settings;
        }

        public void Save()
        {
            try
            {
                string path = FilePathFor();
                var target = store != null && store.FilePath == path ? store : new JsonFileStore<AppSettings>(path);
                target.Save(this);
            }
            catch (Exception) { /* settings are a convenience; never crash over them */ }
        }
    }
}
