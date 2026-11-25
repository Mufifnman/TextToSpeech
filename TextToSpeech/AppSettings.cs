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
