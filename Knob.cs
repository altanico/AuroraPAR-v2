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
    /// Turning: mouse wheel over the knob, drag up/down, or click on the right half (clockwise) / left half
    /// (counter-clockwise). Double click: <see cref="Reset"/>.
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
        private Point? pressPoint;
        private bool dragged;

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
            Cursor = Cursors.Hand;
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
            if (e.ClickCount == 2)
            {
                pressPoint = null;
                Reset?.Invoke();
                return;
            }
            pressPoint = e.GetPosition(this);
            dragged = false;
            CaptureMouse();
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            if (pressPoint is not Point start || !IsMouseCaptured) return;
            Point position = e.GetPosition(this);
            int steps = (int)((start.Y - position.Y) / DragStep);
            if (steps != 0)
            {
                dragged = true;
                pressPoint = new Point(start.X, start.Y - steps * DragStep);
                Step(steps);
            }
        }

        protected override void OnMouseLeftButtonUp(MouseButtonEventArgs e)
        {
            e.Handled = true;
            if (IsMouseCaptured) ReleaseMouseCapture();
            if (pressPoint != null && !dragged)
            {
                Step(e.GetPosition(this).X >= ActualWidth / 2 ? 1 : -1);
            }
            pressPoint = null;
        }

        private static T Frozen<T>(T freezable) where T : Freezable
        {
            freezable.Freeze();
            return freezable;
        }
    }
}
