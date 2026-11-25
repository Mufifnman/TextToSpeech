using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;

namespace TextToSpeech.Hotkeys
{
    /// <summary>System-wide hotkeys via RegisterHotKey, delivered through a window's message loop.</summary>
    public sealed class GlobalHotkeyManager : IDisposable
    {
        private const int WM_HOTKEY = 0x0312;
        private const uint MOD_ALT = 1, MOD_CONTROL = 2, MOD_SHIFT = 4, MOD_WIN = 8, MOD_NOREPEAT = 0x4000;

        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk);
        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool UnregisterHotKey(IntPtr hWnd, int id);

        private readonly HwndSource _source;
        private readonly Dictionary<int, Action> _actions = new();
        private int _nextId = 0x5400;

        public GlobalHotkeyManager(Window window)
        {
            var handle = new WindowInteropHelper(window).EnsureHandle();
            _source = HwndSource.FromHwnd(handle) ?? throw new InvalidOperationException("No window handle");
            _source.AddHook(WndProc);
        }

        /// <summary>Registers a hotkey. Returns false (e.g. already taken by another app) instead of throwing.</summary>
        public bool Register(HotkeyBinding binding, Action action)
        {
            if (!binding.IsValid) return false;
            uint mods = MOD_NOREPEAT;
            if (binding.Modifiers.HasFlag(ModifierKeys.Alt)) mods |= MOD_ALT;
            if (binding.Modifiers.HasFlag(ModifierKeys.Control)) mods |= MOD_CONTROL;
            if (binding.Modifiers.HasFlag(ModifierKeys.Shift)) mods |= MOD_SHIFT;
            if (binding.Modifiers.HasFlag(ModifierKeys.Windows)) mods |= MOD_WIN;
            uint vk = (uint)KeyInterop.VirtualKeyFromKey(binding.Key);

            int id = _nextId++;
            if (!RegisterHotKey(_source.Handle, id, mods, vk)) return false;
            _actions[id] = action;
            return true;
        }

        public void UnregisterAll()
        {
            foreach (var id in _actions.Keys) UnregisterHotKey(_source.Handle, id);
            _actions.Clear();
        }

        private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
        {
            if (msg == WM_HOTKEY && _actions.TryGetValue(wParam.ToInt32(), out var action))
            {
                handled = true;
                action();
            }
            return IntPtr.Zero;
        }

        public void Dispose()
        {
            UnregisterAll();
            _source.RemoveHook(WndProc);
        }
    }
}
