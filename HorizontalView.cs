using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace AuroraPAR
{
    /// <summary>
    /// Azimuth view. Logical x: pixels from the far end of the runway (runway end on the left, approach on the right);
    /// logical y: pixels from the top, extended centreline in the middle.
    /// With the runway on the right both axes are mirrored (180° rotation), so the side of the centreline
    /// shown above/below stays consistent with the direction of flight.
    /// </summary>
    internal class HorizontalView(Canvas canvas, Runway runway) : RadarView(canvas, runway)
    {
        /// <summary>
        /// Half angle of the scan limits, in degrees.
        /// </summary>
        private const double ScanHalfAngle = 10;

        protected override bool FlipVertically => true;

        private double CenterY => Canvas.ActualHeight / 2;

        protected override void CalculateScale()
        {
            xscale = (Canvas.ActualWidth - 50) / (Runway.Distance + Runway.LengthNM);
            yscale = (Canvas.ActualHeight - 50) / (Runway.Distance * Math.Tan(2 * ScanHalfAngle * Math.PI / 180));
        }

        protected override void DrawStatic()
        {
            double length = Runway.LengthNM;
            double range = Runway.Distance;
            double cy = CenterY;
            // Runway and threshold.
            AddLine(0, cy, length * xscale, cy, Brushes.Green, 3);
            AddLine(length * xscale, cy - Runway.WidthNM * yscale, length * xscale, cy + Runway.WidthNM * yscale, Brushes.Green, 3);
            // Scan limits.
            double scanHalf = range * Math.Tan(ScanHalfAngle * Math.PI / 180) * yscale;
            AddLine(0, cy, (length + range) * xscale, cy - scanHalf, Brushes.CadetBlue, 3);
            AddLine(0, cy, (length + range) * xscale, cy + scanHalf, Brushes.CadetBlue, 3);
            // Extended centreline and its approach limits, all starting at the touchdown point:
            // dashed between touchdown and threshold, solid beyond the threshold.
            double xTouchdown = (length - Runway.TouchdownNM) * xscale;
            double xThreshold = length * xscale;
            double xEnd = (length + range) * xscale;
            double halfAtThreshold = Runway.CenterlineHalfWidth(Runway.TouchdownNM) * yscale;
            double halfAtEnd = Runway.CenterlineHalfWidth(Runway.TouchdownNM + range) * yscale;
            AddLine(xTouchdown, cy, xThreshold, cy, Brushes.Yellow, 2, dashed: true);
            AddLine(xThreshold, cy, xEnd, cy, Brushes.Yellow, 2);
            foreach (int side in new[] { -1, 1 })
            {
                AddLine(xTouchdown, cy, xThreshold, cy + side * halfAtThreshold, Brushes.Red, 1, dashed: true);
                AddLine(xThreshold, cy + side * halfAtThreshold, xEnd, cy + side * halfAtEnd, Brushes.Red, 1);
            }
            // Touchdown point.
            AddLine(xTouchdown, cy - 8, xTouchdown, cy + 8, Brushes.Yellow, 2);
            // Range marks, measured from the touchdown point, between the scan limits.
            int num = Runway.Distance == 15 ? 15 : 10;
            for (int i = 1; i <= num; i++)
            {
                double markNM = i * range / num - Runway.TouchdownNM + length;
                double half = markNM / (length + range) * scanHalf;
                AddLine(markNM * xscale, cy + half, markNM * xscale, cy - half, Brushes.Green, 1);
            }
        }

        protected override bool UpdateTrack(Track track, Aircraft aircraft)
        {
            double x = (aircraft.AlongTrackDistance(Runway) + Runway.LengthNM) * xscale;
            double y = CenterY - aircraft.LateralOffset(Runway) * yscale;
            if (ToScreenY(y) < 0 || ToScreenY(y) > Canvas.ActualHeight) return false;
            Brush color = aircraft.IsWithinCenterlineTolerance(Runway) ? Brushes.Green : Brushes.Red;
            Point p = PlaceDot(track, x, y, color);
            track.Label.Text = $"{aircraft.Callsign}\n{aircraft.Altitude}";
            // Label above the track.
            Canvas.SetLeft(track.Label, p.X - 15);
            Canvas.SetTop(track.Label, p.Y - 40);
            return true;
        }
    }
}
