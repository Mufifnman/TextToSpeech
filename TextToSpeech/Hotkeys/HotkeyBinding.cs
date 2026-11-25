using System.Text.Json.Serialization;
using System.Windows.Input;

namespace TextToSpeech.Hotkeys
{
    /// <summary>A key plus modifiers, e.g. Ctrl+Alt+R. Serialized as text so settings stay readable.</summary>
    public readonly record struct HotkeyBinding(ModifierKeys Modifiers, Key Key)
    {
        [JsonIgnore]
        public bool IsValid => Key != Key.None && Modifiers != ModifierKeys.None;

        public override string ToString()
        {
            var parts = new List<string>();
            if (Modifiers.HasFlag(ModifierKeys.Control)) parts.Add("Ctrl");
            if (Modifiers.HasFlag(ModifierKeys.Alt)) parts.Add("Alt");
            if (Modifiers.HasFlag(ModifierKeys.Shift)) parts.Add("Shift");
            if (Modifiers.HasFlag(ModifierKeys.Windows)) parts.Add("Win");
            if (Key != Key.None) parts.Add(Key.ToString());
            return string.Join("+", parts);
        }

        public static bool TryParse(string? text, out HotkeyBinding binding)
        {
            binding = default;
            if (string.IsNullOrWhiteSpace(text)) return false;
            var mods = ModifierKeys.None;
            var key = Key.None;
            foreach (var raw in text.Split('+', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries))
            {
                switch (raw.ToLowerInvariant())
                {
                    case "ctrl": case "control": mods |= ModifierKeys.Control; break;
                    case "alt": mods |= ModifierKeys.Alt; break;
                    case "shift": mods |= ModifierKeys.Shift; break;
                    case "win": case "windows": mods |= ModifierKeys.Windows; break;
                    default:
                        if (!Enum.TryParse(raw, true, out key)) return false;
                        break;
                }
            }
            binding = new HotkeyBinding(mods, key);
            return binding.IsValid;
        }
    }
}
