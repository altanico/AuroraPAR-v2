using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;

namespace AuroraPAR
{
    /// <summary>
    /// A readout window of the analog console, as 14-segment display (default) or as mechanical drum counter
    /// (<see cref="DrumDisplay"/>), as chosen in the profile (Readouts).
    /// </summary>
    internal sealed class Readout : Decorator
    {
        private readonly SegmentDisplay segments;
        private readonly DrumDisplay drums;
        private string text = "";
        private bool useDrums;

        public Readout(int cells)
        {
            segments = new SegmentDisplay(cells);
            drums = new DrumDisplay(cells);
            Child = segments;
        }

        /// <summary>Text shown, right aligned.</summary>
        public string Text
        {
            get => text;
            set
            {
                value ??= "";
                if (text == value) return;
                text = value;
                if (useDrums) drums.Text = value; else segments.Text = value;
            }
        }

        /// <summary>True: mechanical drum counter; false: 14-segment display.</summary>
        public bool Drums
        {
            get => useDrums;
            set
            {
                if (useDrums == value) return;
                useDrums = value;
                if (useDrums)
                {
                    Child = drums;
                    drums.Text = text;
                }
                else
                {
                    Child = segments;
                    segments.Text = text;
                }
            }
        }

    }

    /// <summary>
    /// Mechanical drum counter, as on the old panels: a row of wheels behind a window with a faint glass reflection, each
    /// wheel with the digits 0-9 or the letters A-Z. When the value changes the wheels roll to the new character (the
    /// next one is seen passing). Each wheel is a little off, by a small fixed random amount, as on worn counters.
    /// A '.' is the printed point at the bottom right of the previous wheel.
    /// </summary>
    internal sealed class DrumDisplay : FrameworkElement
    {
        private const string Letters = "ABCDEFGHIJKLMNOPQRSTUVWXYZ";
        private const string Digits = "0123456789";
        /// <summary>Same size as the segment display of the same number of cells (the panel layout does not change).</summary>
        private const double Pad = 5;
        private const double CharWidth = 12;
        private const double CharGap = 5;
        private const double CharHeight = 20;
        private const double Frame = 3;
        /// <summary>Largest misalignment of a wheel, as a fraction of its height (about 6%).</summary>
        private const double MaxMisalignment = 0.06;
        private const double RollSeconds = 0.32;
        private const double RollSecondsPerStep = 0.05;
        private const double RollSecondsMax = 0.9;

        private static readonly Typeface Face = new(new FontFamily(new Uri("pack://application:,,,/"), "./Fonts/#Carlito"),
            FontStyles.Normal, FontWeights.Bold, FontStretches.Normal);
        private static readonly Brush WindowBrush = Frozen(new SolidColorBrush(Color.FromRgb(0x05, 0x05, 0x04)));
        private static readonly Pen WindowPen = Frozen(new Pen(new SolidColorBrush(Color.FromRgb(0x12, 0x12, 0x10)), 1.5));
        private static readonly Brush WheelBrush = Frozen(new SolidColorBrush(Color.FromRgb(0x0C, 0x0C, 0x0B)));
        private static readonly Brush DigitBrush = Frozen(new SolidColorBrush(Color.FromRgb(0xEC, 0xEC, 0xE2)));
        private static readonly Pen SeparatorPen = Frozen(new Pen(new SolidColorBrush(Color.FromRgb(0x2A, 0x2A, 0x27)), 1));
        /// <summary>Shading of the cylinder: dark at the top and bottom, a little light in the middle.</summary>
        private static readonly Brush ShadeBrush = Frozen(new LinearGradientBrush(new GradientStopCollection
        {
            new GradientStop(Color.FromArgb(235, 0, 0, 0), 0),
            new GradientStop(Color.FromArgb(64, 0, 0, 0), 0.22),
            new GradientStop(Color.FromArgb(16, 255, 255, 255), 0.5),
            new GradientStop(Color.FromArgb(64, 0, 0, 0), 0.78),
            new GradientStop(Color.FromArgb(235, 0, 0, 0), 1)
        })
        { StartPoint = new Point(0, 0), EndPoint = new Point(0, 1) });
        private static readonly Brush GlassBrush = Frozen(new LinearGradientBrush(new GradientStopCollection
        {
            new GradientStop(Color.FromArgb(34, 255, 255, 255), 0),
            new GradientStop(Color.FromArgb(0, 255, 255, 255), 0.4),
            new GradientStop(Color.FromArgb(8, 255, 255, 255), 1)
        })
        { StartPoint = new Point(0, 0), EndPoint = new Point(0.6, 1) });

        private sealed class Wheel
        {
            /// <summary>Set of characters of this wheel (digits or letters); null: blank or a fixed symbol.</summary>
            public string? Set;
            public char Symbol = ' ';
            /// <summary>Position in the set (a fraction while rolling).</summary>
            public double Position;
            public double From;
            public double To;
            public double Start;
            public double Duration;
            public bool Rolling;
            public bool Point;
            /// <summary>Misalignment, fraction of the wheel height.</summary>
            public double Offset;
        }

        private static readonly System.Diagnostics.Stopwatch Clock = System.Diagnostics.Stopwatch.StartNew();
        private readonly int cells;
        private readonly Wheel[] wheels;
        private readonly DispatcherTimer timer = new(DispatcherPriority.Render) { Interval = TimeSpan.FromMilliseconds(16) };
        private readonly Dictionary<char, FormattedText> glyphs = [];
        private double glyphDpi;
        private string text = "";

        public DrumDisplay(int cells)
        {
            this.cells = cells;
            Width = 2 * Pad + cells * CharWidth + (cells - 1) * CharGap + 2;
            Height = CharHeight + 2 * Pad;
            Random random = new();
            wheels = new Wheel[cells];
            for (int i = 0; i < cells; i++)
            {
                wheels[i] = new Wheel { Offset = (random.NextDouble() * 2 - 1) * MaxMisalignment };
            }
            timer.Tick += (s, e) => Tick();
        }

        /// <summary>Text shown, right aligned; characters beyond the window are cut on the right.</summary>
        public string Text
        {
            get => text;
            set
            {
                value ??= "";
                if (text == value) return;
                text = value;
                Apply();
            }
        }

        private static double Now => Clock.Elapsed.TotalSeconds;

        private void Apply()
        {
            // Rolls only when the window is on the screen: at the start and when it is built the values just appear.
            bool animate = IsVisible && IsLoaded;
            List<(char Character, bool Point)> list = SegmentDisplay.Split(text, cells);
            for (int i = 0; i < cells; i++)
            {
                Wheel wheel = wheels[i];
                (char c, bool point) = list[i];
                wheel.Point = point;
                char upper = char.ToUpperInvariant(c);
                string? set = upper is >= 'A' and <= 'Z' ? Letters : upper is >= '0' and <= '9' ? Digits : null;
                if (set == null)
                {
                    wheel.Set = null;
                    wheel.Symbol = c;
                    wheel.Rolling = false;
                    continue;
                }
                int target = set.IndexOf(upper);
                if (wheel.Set == set && animate)
                {
                    int count = set.Length;
                    double current = ((wheel.Position % count) + count) % count;
                    double delta = target - current;
                    if (delta > count / 2.0) delta -= count;
                    if (delta < -count / 2.0) delta += count;
                    if (Math.Abs(delta) < 0.001 && !wheel.Rolling) continue;
                    wheel.From = wheel.Position;
                    wheel.To = wheel.Position + delta;
                    wheel.Start = Now;
                    wheel.Duration = Math.Min(RollSecondsMax, RollSeconds + RollSecondsPerStep * Math.Abs(delta));
                    wheel.Rolling = true;
                }
                else
                {
                    wheel.Set = set;
                    wheel.Position = target;
                    wheel.Rolling = false;
                }
            }
            if (wheels.Any(w => w.Rolling)) timer.Start();
            InvalidateVisual();
        }

        private void Tick()
        {
            bool any = false;
            foreach (Wheel wheel in wheels)
            {
                if (!wheel.Rolling) continue;
                double progress = Math.Clamp((Now - wheel.Start) / wheel.Duration, 0, 1);
                // Quick start, slowing down at the end (the click of a counter).
                double eased = 1 - Math.Pow(1 - progress, 3);
                wheel.Position = wheel.From + (wheel.To - wheel.From) * eased;
                if (progress >= 1)
                {
                    wheel.Rolling = false;
                    int count = wheel.Set?.Length ?? 1;
                    wheel.Position = ((Math.Round(wheel.To) % count) + count) % count;
                }
                else
                {
                    any = true;
                }
            }
            if (!any) timer.Stop();
            InvalidateVisual();
        }

        protected override void OnRender(DrawingContext dc)
        {
            double width = ActualWidth;
            double height = ActualHeight;
            dc.DrawRoundedRectangle(WindowBrush, WindowPen, new Rect(0, 0, width, height), 3, 3);
            double wheelHeight = height - 2 * Frame;
            double wheelWidth = (width - 2 * Frame) / cells;
            double dpi = VisualTreeHelper.GetDpi(this).PixelsPerDip;
            if (dpi != glyphDpi)
            {
                glyphs.Clear();
                glyphDpi = dpi;
            }
            double fontSize = wheelHeight * 0.85;
            for (int i = 0; i < cells; i++)
            {
                Wheel wheel = wheels[i];
                Rect area = new(Frame + i * wheelWidth, Frame, wheelWidth, wheelHeight);
                dc.DrawRectangle(WheelBrush, null, area);
                dc.PushClip(new RectangleGeometry(area));
                double centre = area.Top + wheelHeight / 2 + wheel.Offset * wheelHeight;
                if (wheel.Set is string set)
                {
                    int count = set.Length;
                    double floor = Math.Floor(wheel.Position);
                    double fraction = wheel.Position - floor;
                    for (int k = -1; k <= 2; k++)
                    {
                        int index = (((int)floor + k) % count + count) % count;
                        DrawGlyph(dc, set[index], area.Left + wheelWidth / 2, centre + (k - fraction) * wheelHeight, fontSize, dpi);
                    }
                }
                else if (wheel.Symbol != ' ')
                {
                    DrawGlyph(dc, wheel.Symbol, area.Left + wheelWidth / 2, centre, fontSize, dpi);
                }
                dc.DrawRectangle(ShadeBrush, null, area);
                dc.Pop();
                if (i > 0) dc.DrawLine(SeparatorPen, new Point(area.Left, area.Top), new Point(area.Left, area.Bottom));
                if (wheel.Point)
                {
                    // Printed decimal point on the frame, at the bottom right of the wheel.
                    dc.DrawEllipse(DigitBrush, null, new Point(area.Right, area.Bottom - 3.5), 1.3, 1.3);
                }
            }
            dc.DrawRoundedRectangle(GlassBrush, null, new Rect(0, 0, width, height), 3, 3);
        }

        private void DrawGlyph(DrawingContext dc, char character, double centreX, double centreY, double fontSize, double dpi)
        {
            if (!glyphs.TryGetValue(character, out FormattedText? glyph))
            {
                glyph = new FormattedText(character.ToString(), CultureInfo.InvariantCulture, FlowDirection.LeftToRight, Face, fontSize, DigitBrush, dpi);
                glyphs[character] = glyph;
            }
            // Capital letters and digits are about 0.63 em high: the centre of the character on the centre of the wheel.
            dc.DrawText(glyph, new Point(centreX - glyph.Width / 2, centreY - glyph.Baseline + 0.32 * fontSize));
        }

        private static T Frozen<T>(T freezable) where T : Freezable
        {
            freezable.Freeze();
            return freezable;
        }
    }
}
