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
        Action ResizeStarted,
        Action<double, double> ResizeCompleted,
        Action<int, int> JumpTo,
        Action OpenFullWindow,
        Action Dismiss,
        Action TakeBack,
        Action TogglePlayAll,
        Action<int, int> PlayLine,
        Action ToggleRaw);

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
            display.LineClicked += actions.PlayLine;
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

        /// <summary>The Play all button: shown only when the text has marked questions, lit while play-all is on.</summary>
        public void SetPlayAll(bool visible, bool on)
        {
            playAllButton.Visibility = visible ? Visibility.Visible : Visibility.Collapsed;
            playAllGlyph.Foreground = on
                ? (System.Windows.Media.Brush?)TryFindResource("ReadWordBrush") ?? System.Windows.Media.Brushes.Orange
                : (System.Windows.Media.Brush?)TryFindResource("ThemeSubtle") ?? System.Windows.Media.Brushes.Gray;
            playAllButton.ToolTip = on ? "Play all is on: no stopping at questions. Click to stop at each question." : "Stops after each question. Click to read straight through.";
        }

        /// <summary>The raw text button: shown only when the text has formatting; lit while the original text is shown.</summary>
        public void SetRaw(bool visible, bool on)
        {
            rawButton.Visibility = visible ? Visibility.Visible : Visibility.Collapsed;
            display.ShowRaw = on;
            rawGlyph.Foreground = on
                ? (System.Windows.Media.Brush?)TryFindResource("ReadWordBrush") ?? System.Windows.Media.Brushes.Orange
                : (System.Windows.Media.Brush?)TryFindResource("ThemeSubtle") ?? System.Windows.Media.Brushes.Gray;
            rawButton.ToolTip = on ? "Showing the original text with its symbols. Click for the formatted view." : "Show the original text with its symbols.";
        }

        public void SetPlaying(bool playing) => playGlyph.Text = playing ? "" : "";   // pause / play

        /// <summary>Text above the reading: the source of API text, or that a client is editing the rest.</summary>
        public void SetInfo(string? text, bool canTakeBack)
        {
            infoText.Text = text ?? "";
            infoRow.Visibility = string.IsNullOrEmpty(text) ? Visibility.Collapsed : Visibility.Visible;
            takeBackButton.Visibility = canTakeBack ? Visibility.Visible : Visibility.Collapsed;
        }

        public void SetMaxSize(double width, double height)
        {
            Width = Math.Clamp(width, 280, 900);
            MaxHeight = Math.Clamp(height, 90, 700);
            if (previewing) MinHeight = MaxHeight;
            Anchor();
        }

        private bool previewing;

        /// <summary>Preview while Settings is open: sample text and the full maximum height, so size and text size can be judged.</summary>
        public void SetPreview(bool on)
        {
            previewing = on;
            sampleText.Visibility = on ? Visibility.Visible : Visibility.Collapsed;
            display.Visibility = on ? Visibility.Hidden : Visibility.Visible;
            MinHeight = on ? MaxHeight : 90;
            Anchor();
        }

        // ---- drag to resize (cursor position from Windows, because the window moves under the mouse while it grows) ----
        [DllImport("user32.dll")] private static extern bool GetCursorPos(out POINT point);
        [StructLayout(LayoutKind.Sequential)] private struct POINT { public int X, Y; }

        private bool resizing;
        private Point startCursor;
        private double startWidth, startHeight;

        private Point CursorInDips()
        {
            GetCursorPos(out var p);
            var dpi = System.Windows.Media.VisualTreeHelper.GetDpi(this);
            return new Point(p.X / dpi.DpiScaleX, p.Y / dpi.DpiScaleY);
        }

        private void Grip_MouseDown(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            resizing = true;
            startCursor = CursorInDips();
            startWidth = ActualWidth;
            startHeight = ActualHeight;
            resizeGrip.CaptureMouse();
            actions.ResizeStarted();
            e.Handled = true;
        }

        private void Grip_MouseMove(object sender, System.Windows.Input.MouseEventArgs e)
        {
            if (!resizing) return;
            var now = CursorInDips();
            double width = Math.Clamp(startWidth + (startCursor.X - now.X), 280, 900);
            double height = Math.Clamp(startHeight + (startCursor.Y - now.Y), 90, 700);
            Width = width;
            MaxHeight = height;
            MinHeight = height;   // follow the mouse, even when the text is short
            Anchor();
        }

        private void Grip_MouseUp(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            if (!resizing) return;
            resizing = false;
            resizeGrip.ReleaseMouseCapture();
            double width = Width, height = MaxHeight;
            if (!previewing) MinHeight = 90;
            actions.ResizeCompleted(width, height);
            e.Handled = true;
        }

        public void SetTextSize(double size) { display.TextSize = Math.Clamp(size, 10, 32); sampleText.FontSize = display.TextSize; }

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
        private void PlayAll_Click(object sender, RoutedEventArgs e) => actions.TogglePlayAll();
        private void Raw_Click(object sender, RoutedEventArgs e) => actions.ToggleRaw();
        private void Open_Click(object sender, RoutedEventArgs e) => actions.OpenFullWindow();
        private void Close_Click(object sender, RoutedEventArgs e) => actions.Dismiss();
        private void TakeBack_Click(object sender, RoutedEventArgs e) => actions.TakeBack();
    }
}
