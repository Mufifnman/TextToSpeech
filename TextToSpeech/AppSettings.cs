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

        /// <summary>Bring the window up when reading starts and minimize it when reading ends.</summary>
        public bool AutoShowWhilePlaying { get; set; } = false;

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
                Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
                File.WriteAllText(FilePath, JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true }));
            }
            catch (Exception) { /* settings are a convenience; never crash over them */ }
        }
    }
}
