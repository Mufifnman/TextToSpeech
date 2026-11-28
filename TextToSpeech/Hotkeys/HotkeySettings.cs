using System.IO;
using System.Text.Json;
using System.Windows.Input;

namespace TextToSpeech.Hotkeys
{
    /// <summary>User-configurable hotkeys, persisted as JSON in %AppData%\TextToSpeech\hotkeys.json.</summary>
    public class HotkeySettings
    {
        // Strings (not HotkeyBinding) so the JSON file is hand-editable, e.g. "Ctrl+Alt+R".
        /// <summary>Reads the selected text without showing the window.</summary>
        public string ReadSelection { get; set; } = "Alt+R";
        /// <summary>Reads the selected text and brings the window up (minimized again afterwards if it was hidden).</summary>
        public string ReadSelectionAndShow { get; set; } = "Ctrl+Alt+R";
        // GUESS: summon-window and region-select keep their original shortcuts.
        public string SummonWindow { get; set; } = "Ctrl+Alt+W";
        /// <summary>Read a screen region and bring the window up once the selection is done (minimized again afterwards if the app raised it).</summary>
        public string ReadRegion { get; set; } = "Ctrl+Alt+O";
        /// <summary>Read a screen region and leave the window wherever it is.</summary>
        public string ReadRegionQuiet { get; set; } = "Alt+O";
        /// <summary>Play / pause, from anywhere. Alt+Space normally opens a window's system menu; registered globally it takes that over while the app runs.</summary>
        public string PlayPause { get; set; } = "Alt+Space";
        /// <summary>Clears everything queued to read.</summary>
        public string ClearAll { get; set; } = "Alt+Delete";

        public static HotkeyBinding Default(string name) => name switch
        {
            nameof(ReadSelection) => new(ModifierKeys.Alt, Key.R),
            nameof(ReadSelectionAndShow) => new(ModifierKeys.Control | ModifierKeys.Alt, Key.R),
            nameof(SummonWindow) => new(ModifierKeys.Control | ModifierKeys.Alt, Key.W),
            nameof(ReadRegionQuiet) => new(ModifierKeys.Alt, Key.O),
            nameof(PlayPause) => new(ModifierKeys.Alt, Key.Space),
            nameof(ClearAll) => new(ModifierKeys.Alt, Key.Delete),
            _ => new(ModifierKeys.Control | ModifierKeys.Alt, Key.O),
        };

        public HotkeyBinding GetReadSelection() => Resolve(ReadSelection, nameof(ReadSelection));
        public HotkeyBinding GetReadSelectionAndShow() => Resolve(ReadSelectionAndShow, nameof(ReadSelectionAndShow));
        public HotkeyBinding GetSummonWindow() => Resolve(SummonWindow, nameof(SummonWindow));
        public HotkeyBinding GetReadRegionQuiet() => Resolve(ReadRegionQuiet, nameof(ReadRegionQuiet));
        public HotkeyBinding GetPlayPause() => Resolve(PlayPause, nameof(PlayPause));
        public HotkeyBinding GetClearAll() => Resolve(ClearAll, nameof(ClearAll));
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
                {
                    var loaded = JsonSerializer.Deserialize<HotkeySettings>(File.ReadAllText(FilePath)) ?? new();
                    loaded.FixOldDefaults();
                    return loaded;
                }
            }
            catch { /* corrupt file: fall back to defaults */ }
            return new();
        }

        /// <summary>
        /// Older builds saved Ctrl+Alt+R as the plain read-selection key, which now belongs to
        /// read-and-show. Put read-selection back to its default so the two never collide.
        /// </summary>
        public void FixOldDefaults()
        {
            if (GetReadSelection() == GetReadSelectionAndShow())
                ReadSelection = Default(nameof(ReadSelection)).ToString();
        }

        // Relaxed escaping so '+' stays '+' (not +) and the file is easy to edit by hand.
        public static readonly JsonSerializerOptions WriteOptions = new()
        {
            WriteIndented = true,
            Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping
        };

        public void Save()
        {
            try
            {
                global::TextToSpeech.Core.IO.AtomicFile.WriteAllText(FilePath, JsonSerializer.Serialize(this, WriteOptions));
            }
            catch { /* best effort */ }
        }
    }
}
