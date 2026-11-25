using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;

namespace TextToSpeech
{
    /// <summary>Small Win32 touch WPF does not expose for a normal window: the dark title bar.</summary>
    public static class WindowAppearance
    {
        private const int DWMWA_USE_IMMERSIVE_DARK_MODE = 20;

        [DllImport("dwmapi.dll")] private static extern int DwmSetWindowAttribute(IntPtr hWnd, int attribute, ref int value, int size);

        public static void SetDarkTitleBar(Window window, bool dark)
        {
            IntPtr handle = new WindowInteropHelper(window).Handle;
            if (handle == IntPtr.Zero) return;
            int value = dark ? 1 : 0;
            DwmSetWindowAttribute(handle, DWMWA_USE_IMMERSIVE_DARK_MODE, ref value, sizeof(int));
        }
    }
}
