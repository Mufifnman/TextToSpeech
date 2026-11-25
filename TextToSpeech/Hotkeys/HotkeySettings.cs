using System.IO;
using System.Text.Json;
using System.Windows.Input;

namespace TextToSpeech.Hotkeys
{
    /// <summary>User-configurable hotkeys, persisted as JSON in %AppData%\TextToSpeech\hotkeys.json.</summary>
    public class HotkeySettings
    {
        // Strings (not HotkeyBinding) so the JSON file is hand-editable, e.g. "Ctrl+Alt+R".
        public string ReadSelection { get; set; } = "Ctrl+Alt+R";
        public string SummonWindow { get; set; } = "Ctrl+Alt+W";
        public string ReadRegion { get; set; } = "Ctrl+Alt+O";

        /// <summary>When true, the read-selection and region hotkeys also bring the window to the front.</summary>
        public bool ReadingSummonsWindow { get; set; } = false;

        public static HotkeyBinding Default(string name) => name switch
        {
            nameof(ReadSelection) => new(ModifierKeys.Control | ModifierKeys.Alt, Key.R),
            nameof(SummonWindow) => new(ModifierKeys.Control | ModifierKeys.Alt, Key.W),
            _ => new(ModifierKeys.Control | ModifierKeys.Alt, Key.O),
        };

        public HotkeyBinding GetReadSelection() => Resolve(ReadSelection, nameof(ReadSelection));
        public HotkeyBinding GetSummonWindow() => Resolve(SummonWindow, nameof(SummonWindow));
        public HotkeyBinding GetReadRegion() => Resolve(ReadRegion, nameof(ReadRegion));

        private static HotkeyBinding Resolve(string text, string name) =>
            HotkeyBinding.TryParse(text, out var b) ? b : Default(name);

        private static string FilePath => Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "TextToSpeech", "hotkeys.json");

        public static HotkeySettings Load()
        {
            try
            {
                if (File.Exists(FilePath))
                    return JsonSerializer.Deserialize<HotkeySettings>(File.ReadAllText(FilePath)) ?? new();
            }
            catch { /* corrupt file: fall back to defaults */ }
            return new();
        }

        public void Save()
        {
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
                File.WriteAllText(FilePath, JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true }));
            }
            catch { /* best effort */ }
        }
    }
}
