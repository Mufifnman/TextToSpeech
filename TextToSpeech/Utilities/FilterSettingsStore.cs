using System.IO;
using System.Text.Json;

namespace TextToSpeech
{
    /// <summary>
    /// Saves and loads <see cref="FilterSettings"/> as JSON under %LocalAppData%\TextToSpeech.
    /// Failures are ignored so a bad or missing file never stops the app from starting.
    /// </summary>
    internal static class FilterSettingsStore
    {
        private static readonly string SettingsPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "TextToSpeech",
            "filters.json");

        internal static FilterSettings Load()
        {
            try
            {
                if (File.Exists(SettingsPath))
                {
                    return JsonSerializer.Deserialize<FilterSettings>(File.ReadAllText(SettingsPath)) ?? new FilterSettings();
                }
            }
            catch (Exception)
            {
                // Fall back to defaults.
            }
            return new FilterSettings();
        }

        internal static void Save(FilterSettings settings)
        {
            try
            {
                global::TextToSpeech.Core.IO.AtomicFile.WriteAllText(SettingsPath, JsonSerializer.Serialize(settings, new JsonSerializerOptions { WriteIndented = true }));
            }
            catch (Exception)
            {
                // Not being able to save filter choices shouldn't interrupt reading.
            }
        }
    }
}
