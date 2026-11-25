using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using TextToSpeech.Core.Reading;

namespace TextToSpeech
{
    /// <summary>What the popup's buttons do. The main window supplies these.</summary>
    public sealed record PopupActions(
        Action Back,
        Action PlayPause,
        Action Skip,
        Action Clear,
        Action<int, int> JumpTo,
        Action OpenFullWindow,
        Action Dismiss,
        Action TakeBack);

    /// <summary>
    /// A small always-on-top window in the bottom right corner (above the tray) that shows the reading text with the
    /// current word highlighted. Clicking a word jumps there. It never takes focus.
    /// </summary>
    public partial class ReadingPopupWindow : Window
    {
        private const double EdgeGap = 12;

        private const int GWL_EXSTYLE = -20;
        private const int WS_EX_NOACTIVATE = 0x08000000;
        private const int WS_EX_TOOLWINDOW = 0x00000080;

        [DllImport("user32.dll")] private static extern int GetWindowLong(IntPtr hWnd, int index);
        [DllImport("user32.dll")] private static extern int SetWindowLong(IntPtr hWnd, int index, int value);

        private readonly PopupActions actions;

        public ReadingPopupWindow(ReadingDocument document, PopupActions actions)
        {
            InitializeComponent();
            this.actions = actions;
            display.Document = document;
            display.WordClicked += actions.JumpTo;
            SizeChanged += (_, _) => Anchor();
        }

        protected override void OnSourceInitialized(EventArgs e)
        {
            base.OnSourceInitialized(e);
            // No activation (never steals focus) and no Alt-Tab entry.
            IntPtr handle = new WindowInteropHelper(this).Handle;
            SetWindowLong(handle, GWL_EXSTYLE, GetWindowLong(handle, GWL_EXSTYLE) | WS_EX_NOACTIVATE | WS_EX_TOOLWINDOW);
            Anchor();
        }

        /// <summary>Bottom right of the work area (the area not covered by the taskbar), whichever edge the taskbar is on.</summary>
        private void Anchor()
        {
            var work = SystemParameters.WorkArea;
            Left = work.Right - ActualWidth - EdgeGap;
            Top = work.Bottom - ActualHeight - EdgeGap;
        }

        public void SetPlaying(bool playing) => playGlyph.Text = playing ? "" : "";   // pause / play

        /// <summary>Text above the reading: the source of API text, or that a client is editing the rest.</summary>
        public void SetInfo(string? text, bool canTakeBack)
        {
            infoText.Text = text ?? "";
            infoRow.Visibility = string.IsNullOrEmpty(text) ? Visibility.Collapsed : Visibility.Visible;
            takeBackButton.Visibility = canTakeBack ? Visibility.Visible : Visibility.Collapsed;
        }

        public void SetTextSize(double size) => display.TextSize = Math.Clamp(size, 10, 32);

        public void RefreshTheme() => display.RefreshTheme();

        public void SetPanelOpacity(double opacity) => panel.Opacity = opacity;

        public void ShowPopup()
        {
            if (!IsVisible)
            {
                Show();
            }
            Anchor();
        }

        public void HidePopup()
        {
            if (IsVisible)
            {
                Hide();
            }
        }

        private void Back_Click(object sender, RoutedEventArgs e) => actions.Back();
        private void PlayPause_Click(object sender, RoutedEventArgs e) => actions.PlayPause();
        private void Clear_Click(object sender, RoutedEventArgs e) => actions.Clear();
        private void Skip_Click(object sender, RoutedEventArgs e) => actions.Skip();
        private void Open_Click(object sender, RoutedEventArgs e) => actions.OpenFullWindow();
        private void Close_Click(object sender, RoutedEventArgs e) => actions.Dismiss();
        private void TakeBack_Click(object sender, RoutedEventArgs e) => actions.TakeBack();
    }
}
