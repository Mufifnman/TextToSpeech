using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using DrawingRectangle = System.Drawing.Rectangle;
using WpfPoint = System.Windows.Point;

namespace TextToSpeech
{
    /// <summary>
    /// Full-screen, see-through overlay that lets the user drag a rectangle around
    /// the text they want read (like the Snipping Tool). Covers every monitor.
    /// </summary>
    public partial class RegionSelectWindow : Window
    {
        // Selections smaller than this (in DIPs) are treated as an accidental click.
        private const double MinSelectionSize = 8;

        private WpfPoint? dragStart;

        /// <summary>The selected region in screen pixels, or null if the user cancelled.</summary>
        public DrawingRectangle? SelectedRegion { get; private set; }

        public RegionSelectWindow()
        {
            InitializeComponent();

            Left = SystemParameters.VirtualScreenLeft;
            Top = SystemParameters.VirtualScreenTop;
            Width = SystemParameters.VirtualScreenWidth;
            Height = SystemParameters.VirtualScreenHeight;

            FullRect.Rect = new Rect(0, 0, Width, Height);
            HoleRect.Rect = Rect.Empty;

            Loaded += (_, _) =>
            {
                PlaceHint();
                Activate();
                Focus();
            };
        }

        /// <summary>Shows the overlay and returns the region the user picked, in screen pixels.</summary>
        public static DrawingRectangle? Pick(Window? owner = null)
        {
            var picker = new RegionSelectWindow();
            if (owner != null && owner.IsVisible)
            {
                picker.Owner = owner;
            }
            picker.ShowDialog();
            return picker.SelectedRegion;
        }

        private void PlaceHint()
        {
            // Center the hint on the primary monitor (which starts at screen 0,0).
            Hint.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
            var primaryOrigin = PointFromScreen(new WpfPoint(0, 0));
            Canvas.SetLeft(Hint, primaryOrigin.X + (SystemParameters.PrimaryScreenWidth - Hint.DesiredSize.Width) / 2);
            Canvas.SetTop(Hint, primaryOrigin.Y + 40);
        }

        private void OnMouseDown(object sender, MouseButtonEventArgs e)
        {
            dragStart = e.GetPosition(Root);
            Hint.Visibility = Visibility.Collapsed;
            CaptureMouse();
            UpdateSelection(dragStart.Value);
        }

        private void OnMouseMove(object sender, MouseEventArgs e)
        {
            if (dragStart != null)
            {
                UpdateSelection(e.GetPosition(Root));
            }
        }

        private void OnMouseUp(object sender, MouseButtonEventArgs e)
        {
            if (dragStart == null)
            {
                return;
            }

            ReleaseMouseCapture();
            var rect = new Rect(dragStart.Value, e.GetPosition(Root));
            dragStart = null;

            if (rect.Width < MinSelectionSize || rect.Height < MinSelectionSize)
            {
                // Too small: let the user try again.
                HoleRect.Rect = Rect.Empty;
                SelectionBorder.Visibility = Visibility.Collapsed;
                SizeLabel.Visibility = Visibility.Collapsed;
                Hint.Visibility = Visibility.Visible;
                return;
            }

            // PointToScreen converts DIPs to the device pixels CopyFromScreen expects,
            // which keeps this correct on high-DPI displays.
            var topLeft = Root.PointToScreen(rect.TopLeft);
            var bottomRight = Root.PointToScreen(rect.BottomRight);
            SelectedRegion = new DrawingRectangle(
                (int)Math.Round(topLeft.X),
                (int)Math.Round(topLeft.Y),
                (int)Math.Round(bottomRight.X - topLeft.X),
                (int)Math.Round(bottomRight.Y - topLeft.Y));

            Close();
        }

        private void UpdateSelection(WpfPoint current)
        {
            var rect = new Rect(dragStart!.Value, current);
            HoleRect.Rect = rect;

            SelectionBorder.Visibility = Visibility.Visible;
            Canvas.SetLeft(SelectionBorder, rect.X);
            Canvas.SetTop(SelectionBorder, rect.Y);
            SelectionBorder.Width = rect.Width;
            SelectionBorder.Height = rect.Height;

            SizeLabel.Visibility = Visibility.Visible;
            SizeText.Text = $"{(int)rect.Width} × {(int)rect.Height}";
            Canvas.SetLeft(SizeLabel, rect.X);
            Canvas.SetTop(SizeLabel, rect.Y >= 26 ? rect.Y - 26 : rect.Bottom + 4);
        }

        private void OnCancel(object sender, MouseButtonEventArgs e) => Cancel();

        private void OnKeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Escape)
            {
                Cancel();
            }
        }

        private void Cancel()
        {
            SelectedRegion = null;
            if (IsMouseCaptured)
            {
                ReleaseMouseCapture();
            }
            Close();
        }
    }
}
