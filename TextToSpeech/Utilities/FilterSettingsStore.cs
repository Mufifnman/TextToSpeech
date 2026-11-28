using System.IO;

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

        private static readonly global::TextToSpeech.Core.IO.JsonFileStore<FilterSettings> Store = new(SettingsPath);

        internal static FilterSettings Load() => Store.Load(); // a corrupt file is kept as filters.json.bad

        internal static void Save(FilterSettings settings)
        {
            try
            {
                Store.Save(settings);
            }
            catch (Exception)
            {
                // Not being able to save filter choices shouldn't interrupt reading.
            }
        }
    }
}
