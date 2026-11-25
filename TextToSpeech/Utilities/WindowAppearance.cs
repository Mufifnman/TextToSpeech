using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;

namespace TextToSpeech
{
    /// <summary>Small Win32 touches WPF does not expose for a normal window: opacity and the dark title bar.</summary>
    internal static class WindowAppearance
    {
        private const int GWL_EXSTYLE = -20;
        private const int WS_EX_LAYERED = 0x80000;
        private const uint LWA_ALPHA = 2;
        private const int DWMWA_USE_IMMERSIVE_DARK_MODE = 20;

        [DllImport("user32.dll", EntryPoint = "GetWindowLongW")] private static extern int GetWindowLong(IntPtr hWnd, int index);
        [DllImport("user32.dll", EntryPoint = "SetWindowLongW")] private static extern int SetWindowLong(IntPtr hWnd, int index, int value);
        [DllImport("user32.dll")] private static extern bool SetLayeredWindowAttributes(IntPtr hWnd, uint key, byte alpha, uint flags);
        [DllImport("dwmapi.dll")] private static extern int DwmSetWindowAttribute(IntPtr hWnd, int attribute, ref int value, int size);

        /// <summary>Whole-window opacity, 0.3 to 1.0. Works on a normal (non-transparent) window via a layered window.</summary>
        public static void SetOpacity(Window window, double opacity)
        {
            IntPtr handle = new WindowInteropHelper(window).Handle;
            if (handle == IntPtr.Zero) return;
            opacity = Math.Clamp(opacity, 0.3, 1.0);

            int style = GetWindowLong(handle, GWL_EXSTYLE);
            if (opacity >= 0.999)
            {
                SetWindowLong(handle, GWL_EXSTYLE, style & ~WS_EX_LAYERED);
            }
            else
            {
                SetWindowLong(handle, GWL_EXSTYLE, style | WS_EX_LAYERED);
                SetLayeredWindowAttributes(handle, 0, (byte)Math.Round(opacity * 255), LWA_ALPHA);
            }
        }

        public static void SetDarkTitleBar(Window window, bool dark)
        {
            IntPtr handle = new WindowInteropHelper(window).Handle;
            if (handle == IntPtr.Zero) return;
            int value = dark ? 1 : 0;
            DwmSetWindowAttribute(handle, DWMWA_USE_IMMERSIVE_DARK_MODE, ref value, sizeof(int));
        }
    }
}
