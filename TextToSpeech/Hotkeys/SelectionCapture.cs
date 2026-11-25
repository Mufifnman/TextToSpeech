using System.Runtime.InteropServices;
using System.Windows;

namespace TextToSpeech.Hotkeys
{
    /// <summary>
    /// Grabs the text currently selected in whatever app has focus by simulating Ctrl+C,
    /// then restores the previous clipboard contents.
    /// </summary>
    public static class SelectionCapture
    {
        [DllImport("user32.dll")] private static extern uint GetClipboardSequenceNumber();
        [DllImport("user32.dll")] private static extern short GetAsyncKeyState(int vKey);
        [DllImport("user32.dll", SetLastError = true)]
        private static extern uint SendInput(uint nInputs, INPUT[] pInputs, int cbSize);

        private const int VK_SHIFT = 0x10, VK_CONTROL = 0x11, VK_MENU = 0x12, VK_LWIN = 0x5B, VK_RWIN = 0x5C, VK_C = 0x43;
        private const uint INPUT_KEYBOARD = 1, KEYEVENTF_KEYUP = 2;

        [StructLayout(LayoutKind.Sequential)]
        private struct INPUT { public uint type; public InputUnion u; }

        [StructLayout(LayoutKind.Explicit)]
        private struct InputUnion
        {
            [FieldOffset(0)] public MOUSEINPUT mi;   // present so the union has the correct native size
            [FieldOffset(0)] public KEYBDINPUT ki;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct MOUSEINPUT { public int dx, dy; public uint mouseData, dwFlags, time; public IntPtr dwExtraInfo; }

        [StructLayout(LayoutKind.Sequential)]
        private struct KEYBDINPUT { public ushort wVk, wScan; public uint dwFlags, time; public IntPtr dwExtraInfo; }

        private static INPUT Key(int vk, bool up) => new()
        {
            type = INPUT_KEYBOARD,
            u = new InputUnion { ki = new KEYBDINPUT { wVk = (ushort)vk, dwFlags = up ? KEYEVENTF_KEYUP : 0 } }
        };

        private static bool IsDown(int vk) => (GetAsyncKeyState(vk) & 0x8000) != 0;

        /// <summary>Must be called from the UI (STA) thread. Returns null if nothing was selected.</summary>
        public static async Task<string?> GetSelectedTextAsync()
        {
            // The hotkey's own modifiers are usually still held; wait so our Ctrl+C isn't Ctrl+Alt+C etc.
            for (int i = 0; i < 40 && (IsDown(VK_SHIFT) || IsDown(VK_CONTROL) || IsDown(VK_MENU) || IsDown(VK_LWIN) || IsDown(VK_RWIN)); i++)
                await Task.Delay(25);

            IDataObject? saved = SnapshotClipboard();
            uint before = GetClipboardSequenceNumber();

            SendInput(4, new[] { Key(VK_CONTROL, false), Key(VK_C, false), Key(VK_C, true), Key(VK_CONTROL, true) },
                      Marshal.SizeOf<INPUT>());

            // Wait for the target app to publish the copy (up to ~500ms).
            for (int i = 0; i < 20 && GetClipboardSequenceNumber() == before; i++)
                await Task.Delay(25);

            string? text = null;
            if (GetClipboardSequenceNumber() != before)
            {
                text = TryGetClipboardText();
            }

            RestoreClipboard(saved);
            return string.IsNullOrWhiteSpace(text) ? null : text;
        }

        private static IDataObject? SnapshotClipboard()
        {
            try
            {
                var current = Clipboard.GetDataObject();
                if (current == null) return null;
                // Copy so the snapshot survives the clipboard being overwritten.
                var copy = new DataObject();
                foreach (var format in current.GetFormats(false))
                {
                    try { var d = current.GetData(format, false); if (d != null) copy.SetData(format, d); } catch { }
                }
                return copy;
            }
            catch { return null; }
        }

        private static void RestoreClipboard(IDataObject? saved)
        {
            for (int i = 0; i < 5; i++)
            {
                try
                {
                    if (saved != null) Clipboard.SetDataObject(saved, true); else Clipboard.Clear();
                    return;
                }
                catch (COMException) { Thread.Sleep(20); }
            }
        }

        private static string? TryGetClipboardText()
        {
            for (int i = 0; i < 5; i++)
            {
                try { return Clipboard.ContainsText() ? Clipboard.GetText() : null; }
                catch (COMException) { Thread.Sleep(20); }
            }
            return null;
        }
    }
}
