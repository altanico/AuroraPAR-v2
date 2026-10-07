using System.Globalization;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;

namespace AuroraPAR
{
    /// <summary>
    /// Rotary knob of the analog console. It only shows a position and reports turns: the owner decides what a turn
    /// does and sets <see cref="Index"/> back, so the knob always shows the real state (also when it is changed
    /// with the keyboard or the mouse wheel elsewhere).
    ///
    /// Mouse, with separate zones (the pointer shows what a click does):
    /// - right half of the knob: each click one step clockwise (curved arrow pointer to the right);
    /// - left half: each click one step counter-clockwise (curved arrow to the left);
    /// - centre of the knob: drag up/down to turn, double click for <see cref="Reset"/> (up/down arrow pointer);
    /// - mouse wheel anywhere over the control.
    /// A click acts when the button is pressed, and every click counts (two quick clicks are two steps, not a
    /// reset). Outside the knob and its scale a click does nothing.
    /// </summary>
    internal class Knob : FrameworkElement
    {
        /// <summary>Angle covered by the detents, centred on the top.</summary>
        private const double ArcDegrees = 270;
        /// <summary>Pixels of mouse drag for one step.</summary>
        private const double DragStep = 14;
        private const double KnobRadius = 17;
        private const double TickInner = 20;
        private const double TickOuter = 24;
        private const double LabelRadius = 32;

        private static readonly Brush TextBrush = Frozen(new SolidColorBrush(Color.FromRgb(0xD8, 0xD8, 0xD0)));
        private static readonly Pen TickPen = Frozen(new Pen(TextBrush, 1));
        private static readonly Pen EdgePen = Frozen(new Pen(new SolidColorBrush(Color.FromRgb(0x10, 0x10, 0x10)), 1));
        private static readonly Pen PointerPen = Frozen(new Pen(Brushes.White, 2.5) { StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round });
        private static readonly Brush SkirtBrush = Frozen(new RadialGradientBrush(Color.FromRgb(0x78, 0x7A, 0x76), Color.FromRgb(0x26, 0x27, 0x25))
        {
            GradientOrigin = new Point(0.35, 0.3)
        });
        private static readonly Brush CapBrush = Frozen(new RadialGradientBrush(Color.FromRgb(0x55, 0x57, 0x53), Color.FromRgb(0x1A, 0x1B, 0x19))
        {
            GradientOrigin = new Point(0.35, 0.3)
        });

        private int index;
        /// <summary>Angle of the pointer of an endless knob (<see cref="Positions"/> = 0).</summary>
        private double endlessAngle;
        /// <summary>Radius of the centre zone (drag and double click), in pixels.</summary>
        private const double CentreRadius = 7;
        /// <summary>Clickable radius: the knob and its scale.</summary>
        private const double ActiveRadius = TickOuter + 3;
        /// <summary>Vertical strip around the middle where a click does nothing (between the two halves).</summary>
        private const double NeutralHalfWidth = 2.5;

        private static readonly Cursor ClockwiseCursor = KnobCursors.Arc(clockwise: true);
        private static readonly Cursor CounterClockwiseCursor = KnobCursors.Arc(clockwise: false);

        /// <summary>Where a drag (from the centre zone) started.</summary>
        private Point? dragPoint;

        /// <summary>Name written above the knob.</summary>
        public string Title { get; set; } = "";
        /// <summary>Number of detents; 0 for an endless knob (no positions, it just turns).</summary>
        public int Positions { get; set; }
        /// <summary>Text written next to a detent (null: none).</summary>
        public Func<int, string?> LabelFor { get; set; } = _ => null;

        /// <summary>Detent shown by the pointer.</summary>
        public int Index
        {
            get => index;
            set
            {
                if (index != value)
                {
                    index = value;
                    InvalidateVisual();
                }
            }
        }

        /// <summary>Turned by the given number of steps (positive clockwise).</summary>
        public event Action<int>? Turned;
        /// <summary>Double click.</summary>
        public event Action? Reset;

        public Knob()
        {
            Height = 86;
        }

        private enum Zone
        {
            Outside,
            Neutral,
            Centre,
            Clockwise,
            CounterClockwise
        }

        private Zone ZoneAt(Point p)
        {
            Vector v = p - Center;
            if (v.Length > ActiveRadius) return Zone.Outside;
            if (v.Length <= CentreRadius) return Zone.Centre;
            if (Math.Abs(v.X) <= NeutralHalfWidth) return Zone.Neutral;
            return v.X > 0 ? Zone.Clockwise : Zone.CounterClockwise;
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            Point position = e.GetPosition(this);
            if (dragPoint is Point start && IsMouseCaptured)
            {
                int steps = (int)((start.Y - position.Y) / DragStep);
                if (steps != 0)
                {
                    dragPoint = new Point(start.X, start.Y - steps * DragStep);
                    Step(steps);
                }
                return;
            }
            Cursor = ZoneAt(position) switch
            {
                Zone.Clockwise => ClockwiseCursor,
                Zone.CounterClockwise => CounterClockwiseCursor,
                Zone.Centre => Cursors.SizeNS,
                _ => null
            };
        }

        protected override void OnMouseLeave(MouseEventArgs e)
        {
            if (!IsMouseCaptured) Cursor = null;
        }

        private Point Center => new(ActualWidth / 2, 50);

        private static Point Polar(Point center, double radius, double degrees)
        {
            double a = degrees * Math.PI / 180;
            return new Point(center.X + radius * Math.Sin(a), center.Y - radius * Math.Cos(a));
        }

        private double DetentAngle(int i)
        {
            return Positions <= 1 ? 0 : -ArcDegrees / 2 + ArcDegrees * i / (Positions - 1);
        }

        protected override void OnRender(DrawingContext dc)
        {
            // Transparent background, so the whole area reacts to the mouse.
            dc.DrawRectangle(Brushes.Transparent, null, new Rect(RenderSize));
            double pixelsPerDip = VisualTreeHelper.GetDpi(this).PixelsPerDip;
            Point c = Center;
            FormattedText title = Text(Title, 10, FontWeights.Bold, pixelsPerDip);
            dc.DrawText(title, new Point(c.X - title.Width / 2, 0));
            if (Positions > 0)
            {
                for (int i = 0; i < Positions; i++)
                {
                    double angle = DetentAngle(i);
                    dc.DrawLine(TickPen, Polar(c, TickInner, angle), Polar(c, TickOuter, angle));
                    string? label = LabelFor(i);
                    if (label != null)
                    {
                        FormattedText text = Text(label, 9, FontWeights.Normal, pixelsPerDip);
                        Point p = Polar(c, LabelRadius, angle);
                        dc.DrawText(text, new Point(p.X - text.Width / 2, p.Y - text.Height / 2));
                    }
                }
            }
            else
            {
                for (int i = 0; i < 12; i++)
                {
                    double angle = i * 30;
                    dc.DrawLine(TickPen, Polar(c, TickInner, angle), Polar(c, TickInner + 2, angle));
                }
            }
            dc.DrawEllipse(SkirtBrush, EdgePen, c, KnobRadius, KnobRadius);
            dc.DrawEllipse(CapBrush, EdgePen, c, KnobRadius * 0.7, KnobRadius * 0.7);
            double pointer = Positions > 0 ? DetentAngle(Math.Clamp(Index, 0, Positions - 1)) : endlessAngle;
            dc.DrawLine(PointerPen, Polar(c, 4, pointer), Polar(c, KnobRadius - 2, pointer));
        }

        private static FormattedText Text(string text, double size, FontWeight weight, double pixelsPerDip)
        {
            return new FormattedText(text, CultureInfo.InvariantCulture, FlowDirection.LeftToRight,
                new Typeface(new FontFamily("Segoe UI"), FontStyles.Normal, weight, FontStretches.Normal), size, TextBrush, pixelsPerDip);
        }

        private void Step(int steps)
        {
            if (steps == 0) return;
            if (Positions == 0)
            {
                endlessAngle = (endlessAngle + steps * 20) % 360;
                InvalidateVisual();
            }
            Turned?.Invoke(steps);
        }

        protected override void OnMouseWheel(MouseWheelEventArgs e)
        {
            // Handled here, so the wheel over a knob does not also change the range of the display.
            Step(e.Delta > 0 ? 1 : -1);
            e.Handled = true;
        }

        protected override void OnMouseLeftButtonDown(MouseButtonEventArgs e)
        {
            e.Handled = true;
            Point position = e.GetPosition(this);
            switch (ZoneAt(position))
            {
                case Zone.Clockwise:
                    Step(1);
                    break;
                case Zone.CounterClockwise:
                    Step(-1);
                    break;
                case Zone.Centre:
                    if (e.ClickCount == 2)
                    {
                        dragPoint = null;
                        Reset?.Invoke();
                        return;
                    }
                    dragPoint = position;
                    CaptureMouse();
                    break;
            }
        }

        protected override void OnMouseLeftButtonUp(MouseButtonEventArgs e)
        {
            e.Handled = true;
            dragPoint = null;
            if (IsMouseCaptured) ReleaseMouseCapture();
        }

        private static T Frozen<T>(T freezable) where T : Freezable
        {
            freezable.Freeze();
            return freezable;
        }
    }

    /// <summary>
    /// Mouse pointers of the knobs: a curved arrow turning clockwise or counter-clockwise (as on the knobs of the
    /// flight simulators), drawn at start and turned into a Windows cursor (32x32, 32-bit with transparency).
    /// </summary>
    internal static class KnobCursors
    {
        private const int Pixels = 32;

        public static Cursor Arc(bool clockwise)
        {
            try
            {
                return new Cursor(new System.IO.MemoryStream(CursorFile(Render(clockwise), Pixels / 2, Pixels / 2 + 2)));
            }
            catch (Exception)
            {
                return Cursors.Hand;
            }
        }

        /// <summary>Arrow drawn on a transparent 32x32 image: white with a black outline, readable on any colour.</summary>
        private static byte[] Render(bool clockwise)
        {
            Point centre = new(Pixels / 2.0, Pixels / 2.0 + 3);
            const double radius = 10;
            const double span = 75;
            Point Polar(double degrees) => new(centre.X + radius * Math.Sin(degrees * Math.PI / 180), centre.Y - radius * Math.Cos(degrees * Math.PI / 180));
            double startAngle = clockwise ? -span : span;
            double endAngle = clockwise ? span : -span;
            StreamGeometry arc = new();
            using (StreamGeometryContext ctx = arc.Open())
            {
                ctx.BeginFigure(Polar(startAngle), false, false);
                ctx.ArcTo(Polar(endAngle), new Size(radius, radius), 0, false,
                    clockwise ? SweepDirection.Clockwise : SweepDirection.Counterclockwise, true, false);
            }
            arc.Freeze();
            // Arrow head at the end of the arc, pointing along the turn.
            double a = endAngle * Math.PI / 180;
            Vector along = clockwise ? new Vector(Math.Cos(a), Math.Sin(a)) : new Vector(-Math.Cos(a), -Math.Sin(a));
            Vector outward = new(Math.Sin(a), -Math.Cos(a));
            Point end = Polar(endAngle);
            Point tip = end + along * 5;
            StreamGeometry head = new();
            using (StreamGeometryContext ctx = head.Open())
            {
                ctx.BeginFigure(tip, true, true);
                ctx.LineTo(end - along * 1.5 + outward * 4.5, true, false);
                ctx.LineTo(end - along * 1.5 - outward * 4.5, true, false);
            }
            head.Freeze();
            DrawingVisual visual = new();
            using (DrawingContext dc = visual.RenderOpen())
            {
                Pen outline = new(Brushes.Black, 4.5) { StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round };
                Pen line = new(Brushes.White, 2) { StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round };
                dc.DrawGeometry(null, outline, arc);
                dc.DrawGeometry(Brushes.Black, new Pen(Brushes.Black, 2.5), head);
                dc.DrawGeometry(null, line, arc);
                dc.DrawGeometry(Brushes.White, null, head);
                // Point of the click.
                dc.DrawEllipse(Brushes.White, new Pen(Brushes.Black, 1), new Point(Pixels / 2.0, Pixels / 2.0 + 2), 1.8, 1.8);
            }
            System.Windows.Media.Imaging.RenderTargetBitmap bitmap = new(Pixels, Pixels, 96, 96, PixelFormats.Pbgra32);
            bitmap.Render(visual);
            System.Windows.Media.Imaging.FormatConvertedBitmap straight = new(bitmap, PixelFormats.Bgra32, null, 0);
            byte[] pixels = new byte[Pixels * Pixels * 4];
            straight.CopyPixels(pixels, Pixels * 4, 0);
            return pixels;
        }

        /// <summary>Windows .cur file with one 32x32 32-bit image (top-down BGRA pixels given).</summary>
        private static byte[] CursorFile(byte[] pixels, int hotX, int hotY)
        {
            int maskSize = Pixels * 4; // 1 bit per pixel, rows of 32 bits
            int imageSize = 40 + pixels.Length + maskSize;
            using System.IO.MemoryStream stream = new();
            using System.IO.BinaryWriter w = new(stream);
            // Header: reserved, type 2 (cursor), one image.
            w.Write((short)0); w.Write((short)2); w.Write((short)1);
            // Directory entry.
            w.Write((byte)Pixels); w.Write((byte)Pixels); w.Write((byte)0); w.Write((byte)0);
            w.Write((short)hotX); w.Write((short)hotY);
            w.Write(imageSize); w.Write(6 + 16);
            // BITMAPINFOHEADER (height doubled: colour image + mask).
            w.Write(40); w.Write(Pixels); w.Write(Pixels * 2); w.Write((short)1); w.Write((short)32);
            w.Write(0); w.Write(pixels.Length + maskSize); w.Write(0); w.Write(0); w.Write(0); w.Write(0);
            // Pixels bottom-up.
            for (int y = Pixels - 1; y >= 0; y--) w.Write(pixels, y * Pixels * 4, Pixels * 4);
            // Mask: all zero, the transparency is in the alpha channel.
            w.Write(new byte[maskSize]);
            w.Flush();
            return stream.ToArray();
        }
    }
}
