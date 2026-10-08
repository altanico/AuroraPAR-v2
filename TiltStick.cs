using System.Globalization;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;

namespace AuroraPAR
{
    /// <summary>
    /// Small 4-way joystick of the analog console for the antenna tilt, as on some old American PAR consoles.
    /// Pushed (dragged with the mouse, or a click on a direction) up / down / left / right it reports one step, then
    /// repeats while held; released it springs back to the centre. A click on the centre reports
    /// <see cref="Centred"/> (antenna neutral). Directions as shown on the scope (right = right on the screen).
    /// </summary>
    internal sealed class TiltStick : FrameworkElement
    {
        private const double PlateRadius = 34;
        private const double Reach = 13;
        private const double BallRadius = 8;
        /// <summary>Movement (px) that chooses a direction; a press closer to the centre without moving is a centre click.</summary>
        private const double DirectionThreshold = 7;
        private const double RepeatDelay = 0.5;
        private const double RepeatInterval = 0.25;

        private static readonly Brush TextBrush = Frozen(new SolidColorBrush(Color.FromRgb(0xD8, 0xD8, 0xD0)));
        private static readonly Brush PlateBrush = Frozen(new RadialGradientBrush(Color.FromRgb(0x14, 0x15, 0x13), Color.FromRgb(0x26, 0x27, 0x24)));
        private static readonly Pen PlatePen = Frozen(new Pen(new SolidColorBrush(Color.FromRgb(0x50, 0x52, 0x4E)), 1.5));
        private static readonly Pen BootPen = Frozen(new Pen(new SolidColorBrush(Color.FromRgb(0x0A, 0x0A, 0x09)), 1));
        private static readonly Brush BootBrush = Frozen(new SolidColorBrush(Color.FromRgb(0x1C, 0x1C, 0x1A)));
        private static readonly Pen ShaftPen = Frozen(new Pen(new SolidColorBrush(Color.FromRgb(0x9A, 0x9C, 0x98)), 4) { StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round });
        private static readonly Brush BallBrush = Frozen(new RadialGradientBrush(Color.FromRgb(0x6A, 0x6C, 0x68), Color.FromRgb(0x18, 0x19, 0x17))
        {
            GradientOrigin = new Point(0.35, 0.3)
        });
        private static readonly Pen EdgePen = Frozen(new Pen(new SolidColorBrush(Color.FromRgb(0x10, 0x10, 0x10)), 1));

        private readonly DispatcherTimer repeat = new();
        /// <summary>Direction held now (elevation, azimuth), (0, 0) none.</summary>
        private (int Elevation, int Azimuth) direction;
        private Vector deflection;
        private bool pressed;
        /// <summary>A direction was taken during this press (then the release is not a centre click).</summary>
        private bool movedThisPress;

        /// <summary>Name written above the stick.</summary>
        public string Title { get; set; } = "TILT";

        /// <summary>One step: elevation +1 up / −1 down, azimuth +1 right / −1 left (as on the screen).</summary>
        public event Action<int, int>? Moved;
        /// <summary>Click on the centre.</summary>
        public event Action? Centred;

        public TiltStick()
        {
            Height = 96;
            Cursor = Cursors.Hand;
            repeat.Tick += (s, e) =>
            {
                repeat.Interval = TimeSpan.FromSeconds(RepeatInterval);
                if (direction != (0, 0)) Moved?.Invoke(direction.Elevation, direction.Azimuth);
            };
        }

        private static T Frozen<T>(T freezable) where T : Freezable
        {
            freezable.Freeze();
            return freezable;
        }

        private Point Center => new(ActualWidth / 2, 54);

        protected override void OnMouseLeftButtonDown(MouseButtonEventArgs e)
        {
            Vector offset = e.GetPosition(this) - Center;
            if (offset.Length > PlateRadius) return;
            CaptureMouse();
            pressed = true;
            movedThisPress = false;
            Follow(offset);
            e.Handled = true;
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            if (pressed && IsMouseCaptured) Follow(e.GetPosition(this) - Center);
        }

        protected override void OnMouseLeftButtonUp(MouseButtonEventArgs e)
        {
            if (!pressed) return;
            bool centreClick = !movedThisPress;
            Release();
            if (IsMouseCaptured) ReleaseMouseCapture();
            if (centreClick) Centred?.Invoke();
            e.Handled = true;
        }

        protected override void OnLostMouseCapture(MouseEventArgs e)
        {
            if (pressed) Release();
        }

        /// <summary>The stick follows the mouse (up to its reach); a new direction gives a step at once and starts the repeat.</summary>
        private void Follow(Vector offset)
        {
            (int, int) now = (0, 0);
            if (offset.Length >= DirectionThreshold)
            {
                // The main direction only (4 ways).
                now = Math.Abs(offset.X) > Math.Abs(offset.Y) ? (0, Math.Sign(offset.X)) : (-Math.Sign(offset.Y), 0);
            }
            deflection = now == (0, 0) ? new Vector(0, 0) : now.Item2 != 0 ? new Vector(now.Item2 * Reach, 0) : new Vector(0, -now.Item1 * Reach);
            InvalidateVisual();
            if (now == direction) return;
            direction = now;
            repeat.Stop();
            if (direction != (0, 0))
            {
                movedThisPress = true;
                Moved?.Invoke(direction.Elevation, direction.Azimuth);
                repeat.Interval = TimeSpan.FromSeconds(RepeatDelay);
                repeat.Start();
            }
        }

        /// <summary>Back to the centre (spring).</summary>
        private void Release()
        {
            pressed = false;
            repeat.Stop();
            direction = (0, 0);
            deflection = new Vector(0, 0);
            InvalidateVisual();
        }

        protected override void OnRender(DrawingContext dc)
        {
            dc.DrawRectangle(Brushes.Transparent, null, new Rect(RenderSize));
            double pixelsPerDip = VisualTreeHelper.GetDpi(this).PixelsPerDip;
            Point c = Center;
            FormattedText title = Text(Title, 10, FontWeights.Bold, pixelsPerDip);
            dc.DrawText(title, new Point(c.X - title.Width / 2, 0));
            // Recessed round plate with the four directions engraved.
            dc.DrawEllipse(PlateBrush, PlatePen, c, PlateRadius, PlateRadius);
            foreach ((string text, double dx, double dy) in new[] { ("UP", 0.0, -1.0), ("DN", 0.0, 1.0), ("L", -1.0, 0.0), ("R", 1.0, 0.0) })
            {
                FormattedText label = Text(text, 8, FontWeights.Bold, pixelsPerDip);
                Point p = new(c.X + dx * (PlateRadius - 8), c.Y + dy * (PlateRadius - 8));
                dc.DrawText(label, new Point(p.X - label.Width / 2, p.Y - label.Height / 2));
            }
            // Rubber boot, shaft and ball.
            dc.DrawEllipse(BootBrush, BootPen, c, 12, 12);
            Point ball = c + deflection;
            dc.DrawLine(ShaftPen, c, ball);
            dc.DrawEllipse(BallBrush, EdgePen, ball, BallRadius, BallRadius);
        }

        private static FormattedText Text(string text, double size, FontWeight weight, double pixelsPerDip)
        {
            return new FormattedText(text, CultureInfo.InvariantCulture, FlowDirection.LeftToRight,
                new Typeface(new FontFamily("Segoe UI"), FontStyles.Normal, weight, FontStretches.Normal), size, TextBrush, pixelsPerDip);
        }
    }
}
