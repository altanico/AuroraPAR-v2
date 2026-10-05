using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;

namespace AuroraPAR
{
    /// <summary>
    /// Display options of the views, taken from the active profile.
    /// </summary>
    internal class ViewOptions
    {
        /// <summary>True: heights above the threshold (QFE); false: altitudes (QNH).</summary>
        public bool Qfe { get; set; }
        public bool ShowAltitudeScale { get; set; } = true;
        public bool ScaleInMetres { get; set; }
    }

    /// <summary>
    /// Common part of the profile (elevation) and azimuth views.
    ///
    /// Static elements (runway, scan and approach limits, range marks...) are built only when something they
    /// depend on changes (runway, range, window size, orientation): see <see cref="Invalidate"/>.
    /// Aircraft tracks are created once per callsign and then only moved and updated, so a refresh
    /// no longer clears and recreates the whole screen.
    ///
    /// The drawing code of the views works in "logical" coordinates, in pixels, as if the runway were on the left
    /// (x grows from the runway towards the approach). <see cref="ToScreenX"/> and <see cref="ToScreenY"/>
    /// mirror them when the runway is shown on the right.
    /// </summary>
    internal abstract class RadarView
    {
        private const int StaticZIndex = 0;
        private const int TrackZIndex = 10;

        protected readonly Canvas Canvas;
        protected Runway Runway;
        /// <summary>
        /// Approach limits, scan limits and antenna tilt (shared by both views).
        /// </summary>
        protected readonly Radar Radar;
        protected readonly ViewOptions Options;
        protected double xscale = 1;
        protected double yscale = 1;
        protected bool RunwayOnRight { get; private set; }

        private bool staticValid = false;
        private readonly List<UIElement> staticElements = [];
        private readonly Dictionary<string, Track> tracks = [];

        /// <summary>
        /// Screen elements of one aircraft, reused at every refresh.
        /// </summary>
        protected sealed class Track
        {
            public readonly Ellipse Dot = new() { Width = 10, Height = 10 };
            public readonly TextBlock Label = new() { FontSize = 12, Foreground = Brushes.White };

            public void SetVisible(bool visible)
            {
                Visibility v = visible ? Visibility.Visible : Visibility.Collapsed;
                Dot.Visibility = v;
                Label.Visibility = v;
            }
        }

        protected RadarView(Canvas canvas, Runway runway, Radar radar, ViewOptions options)
        {
            Canvas = canvas;
            Runway = runway;
            Radar = radar;
            Options = options;
            // Lines going beyond the view (e.g. a tilted scan limit) must not be drawn over the other view.
            Canvas.ClipToBounds = true;
        }

        /// <summary>
        /// True when the vertical axis must also be mirrored with the runway on the right
        /// (azimuth view: mirroring both axes is a 180° rotation, which keeps left/right of the approach correct).
        /// </summary>
        protected virtual bool FlipVertically => false;

        protected abstract void CalculateScale();
        protected abstract void DrawStatic();
        /// <summary>
        /// Positions and updates the track of a displayed aircraft. Returns false to hide it.
        /// </summary>
        protected abstract bool UpdateTrack(Track track, Aircraft aircraft);

        public void SetRunway(Runway runway)
        {
            Runway = runway;
            Invalidate();
        }

        public void SetRunwayOnRight(bool value)
        {
            if (RunwayOnRight != value)
            {
                RunwayOnRight = value;
                Invalidate();
            }
        }

        /// <summary>
        /// Rebuilds the static elements at the next <see cref="Render"/> (call after a change of range, size, settings...).
        /// </summary>
        public void Invalidate()
        {
            staticValid = false;
        }

        /// <summary>
        /// Updates the view. Must be called on the window's (UI) thread.
        /// </summary>
        public void Render(IReadOnlyList<Aircraft> aircrafts)
        {
            if (Canvas.ActualWidth <= 0 || Canvas.ActualHeight <= 0) return;
            if (!staticValid)
            {
                foreach (UIElement element in staticElements)
                {
                    Canvas.Children.Remove(element);
                }
                staticElements.Clear();
                CalculateScale();
                DrawStatic();
                staticValid = true;
            }
            HashSet<string> present = [];
            foreach (Aircraft aircraft in aircrafts)
            {
                if (!present.Add(aircraft.Callsign)) continue;
                if (!tracks.TryGetValue(aircraft.Callsign, out Track? track))
                {
                    track = CreateTrack();
                    tracks[aircraft.Callsign] = track;
                }
                bool visible = Radar.IsInsideScan(aircraft, Runway) && UpdateTrack(track, aircraft);
                track.SetVisible(visible);
            }
            // Remove the tracks of aircraft no longer received from Aurora.
            foreach (string callsign in tracks.Keys.Where(c => !present.Contains(c)).ToList())
            {
                Canvas.Children.Remove(tracks[callsign].Dot);
                Canvas.Children.Remove(tracks[callsign].Label);
                tracks.Remove(callsign);
            }
        }

        private Track CreateTrack()
        {
            Track track = new();
            Panel.SetZIndex(track.Dot, TrackZIndex);
            Panel.SetZIndex(track.Label, TrackZIndex);
            Canvas.Children.Add(track.Dot);
            Canvas.Children.Add(track.Label);
            return track;
        }

        protected double ToScreenX(double x)
        {
            return RunwayOnRight ? Canvas.ActualWidth - x : x;
        }

        protected double ToScreenY(double y)
        {
            return RunwayOnRight && FlipVertically ? Canvas.ActualHeight - y : y;
        }

        /// <summary>
        /// Adds a static line. Coordinates are logical (see class description).
        /// </summary>
        protected void AddLine(double x1, double y1, double x2, double y2, Brush stroke, double thickness, bool dashed = false)
        {
            Line line = new()
            {
                X1 = ToScreenX(x1),
                Y1 = ToScreenY(y1),
                X2 = ToScreenX(x2),
                Y2 = ToScreenY(y2),
                Stroke = stroke,
                StrokeThickness = thickness
            };
            if (dashed)
            {
                line.StrokeDashArray = new DoubleCollection { 4, 3 };
            }
            AddStatic(line);
        }

        /// <summary>
        /// Adds a static text anchored at a logical point; <paramref name="dx"/> is a screen offset of its left side.
        /// With <paramref name="aboveAnchor"/> the text's bottom is at the anchor, otherwise its top.
        /// </summary>
        protected void AddText(string text, double x, double y, double dx, Brush foreground, bool aboveAnchor)
        {
            TextBlock textBlock = new()
            {
                Text = text,
                FontSize = 12,
                Foreground = foreground
            };
            double top = ToScreenY(y);
            if (aboveAnchor)
            {
                top -= TextHeight(textBlock);
            }
            Canvas.SetLeft(textBlock, ToScreenX(x) + dx);
            Canvas.SetTop(textBlock, top);
            AddStatic(textBlock);
        }

        /// <summary>
        /// Adds a static text near the side of the view where the runway is, vertically centred on logical y.
        /// </summary>
        protected void AddSideText(string text, double y, Brush foreground)
        {
            TextBlock textBlock = new()
            {
                Text = text,
                FontSize = 11,
                Foreground = foreground
            };
            textBlock.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
            double left = RunwayOnRight ? Canvas.ActualWidth - 12 - textBlock.DesiredSize.Width : 12;
            Canvas.SetLeft(textBlock, left);
            Canvas.SetTop(textBlock, ToScreenY(y) - textBlock.DesiredSize.Height / 2);
            AddStatic(textBlock);
        }

        /// <summary>
        /// Altitude of the aircraft for labels: altitude with QNH, height above the threshold with QFE (ft).
        /// </summary>
        protected string FormatAltitude(Aircraft aircraft)
        {
            double value = Options.Qfe ? aircraft.Altitude - Runway.Elevation : aircraft.Altitude;
            return value.ToString("0", System.Globalization.CultureInfo.InvariantCulture);
        }

        /// <summary>
        /// Adds a small static filled square centred on a logical point (e.g. the radar antenna).
        /// </summary>
        protected void AddSquare(double x, double y, double size, Brush fill)
        {
            Rectangle square = new()
            {
                Width = size,
                Height = size,
                Fill = fill
            };
            Canvas.SetLeft(square, ToScreenX(x) - size / 2);
            Canvas.SetTop(square, ToScreenY(y) - size / 2);
            AddStatic(square);
        }

        private void AddStatic(UIElement element)
        {
            Panel.SetZIndex(element, StaticZIndex);
            Canvas.Children.Add(element);
            staticElements.Add(element);
        }

        /// <summary>
        /// Places the track symbol centred on a logical point, with the given colour. Returns its screen position.
        /// </summary>
        protected Point PlaceDot(Track track, double x, double y, Brush color)
        {
            Point p = new(ToScreenX(x), ToScreenY(y));
            track.Dot.Fill = color;
            track.Dot.Stroke = color;
            Canvas.SetLeft(track.Dot, p.X - track.Dot.Width / 2);
            Canvas.SetTop(track.Dot, p.Y - track.Dot.Height / 2);
            return p;
        }

        protected static double TextHeight(TextBlock textBlock)
        {
            textBlock.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
            return textBlock.DesiredSize.Height;
        }
    }
}
