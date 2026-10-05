using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace AuroraPAR
{
    /// <summary>
    /// Azimuth view. Logical x: pixels from the far end of the runway (runway end on the left, approach on the right);
    /// logical y: pixels from the top, extended centreline in the middle, right of the approach (pilot's view) upwards.
    /// With the runway on the right both axes are mirrored (180° rotation), so the side of the centreline
    /// shown above/below stays consistent with the direction of flight.
    /// </summary>
    internal class HorizontalView(Canvas canvas, Runway runway, Radar radar, ViewOptions options) : RadarView(canvas, runway, radar, options)
    {
        protected override bool FlipVertically => true;

        private double CenterY => Canvas.ActualHeight / 2;

        /// <summary>
        /// Distance of the antenna from the far end of the runway, in NM.
        /// </summary>
        private double AntennaNM => Radar.AntennaFromRunwayEnd(Runway);

        protected override void CalculateScale()
        {
            xscale = (Canvas.ActualWidth - 50) / (Runway.Distance + Runway.LengthNM);
            // Lateral scale: the wider neutral scan limit fits at the end of the range (not changed by the tilt).
            double half = Radar.ScanOffset(Runway.Distance + Runway.LengthNM - AntennaNM, Math.Max(Math.Max(Radar.ScanLeft, Radar.ScanRight), 1));
            yscale = (Canvas.ActualHeight - 50) / (2 * half);
        }

        /// <summary>
        /// Logical y of a lateral offset in NM (positive right of the approach).
        /// </summary>
        private double LateralY(double offsetNM)
        {
            return CenterY - offsetNM * yscale;
        }

        /// <summary>
        /// Logical y of an azimuth scan limit (signed angle from the antenna) at the given distance (NM) from the far end of the runway.
        /// </summary>
        private double ScanY(double fromRunwayEndNM, double angle)
        {
            return LateralY(Radar.ScanOffset(Math.Max(0, fromRunwayEndNM - AntennaNM), angle));
        }

        protected override void DrawStatic()
        {
            double length = Runway.LengthNM;
            double range = Runway.Distance;
            double end = length + range;
            double cy = CenterY;
            double left = Radar.AzimuthLeftEdge;
            double right = Radar.AzimuthRightEdge;
            // Runway and threshold.
            AddLine(0, cy, length * xscale, cy, Brushes.Green, 3);
            AddLine(length * xscale, cy - Runway.WidthNM * yscale, length * xscale, cy + Runway.WidthNM * yscale, Brushes.Green, 3);
            // Scan limits, from the antenna.
            AddLine(AntennaNM * xscale, cy, end * xscale, ScanY(end, left), Brushes.CadetBlue, 3);
            AddLine(AntennaNM * xscale, cy, end * xscale, ScanY(end, right), Brushes.CadetBlue, 3);
            AddSquare(AntennaNM * xscale, cy, 8, Brushes.CadetBlue);
            // Extended centreline and its approach limits, all starting at the touchdown point:
            // dashed between touchdown and threshold, solid beyond the threshold.
            double xTouchdown = (length - Runway.TouchdownNM) * xscale;
            double xThreshold = length * xscale;
            double xEnd = end * xscale;
            AddLine(xTouchdown, cy, xThreshold, cy, Brushes.Yellow, 2, dashed: true);
            AddLine(xThreshold, cy, xEnd, cy, Brushes.Yellow, 2);
            foreach (bool isRight in new[] { false, true })
            {
                double sign = isRight ? 1 : -1;
                double atThreshold = LateralY(sign * Radar.ApproachHalfWidth(Runway.TouchdownNM, isRight));
                double atEnd = LateralY(sign * Radar.ApproachHalfWidth(Runway.TouchdownNM + range, isRight));
                AddLine(xTouchdown, cy, xThreshold, atThreshold, Brushes.Red, 1, dashed: true);
                AddLine(xThreshold, atThreshold, xEnd, atEnd, Brushes.Red, 1);
            }
            // Touchdown point.
            AddLine(xTouchdown, cy - 8, xTouchdown, cy + 8, Brushes.Yellow, 2);
            // Distance where the glide path reaches the decision height: vertical line between the scan limits.
            double interceptNM = length - Runway.TouchdownNM + Runway.MissedApproachPointNM;
            if (interceptNM <= end)
            {
                AddLine(interceptNM * xscale, ScanY(interceptNM, left), interceptNM * xscale, ScanY(interceptNM, right), Brushes.Red, 2);
            }
            // Range marks, measured from the touchdown point, between the scan limits.
            int num = Runway.Distance == 15 ? 15 : 10;
            for (int i = 1; i <= num; i++)
            {
                double markNM = i * range / num - Runway.TouchdownNM + length;
                AddLine(markNM * xscale, ScanY(markNM, left), markNM * xscale, ScanY(markNM, right), Brushes.Green, 1);
            }
        }

        protected override bool UpdateTrack(Track track, Aircraft aircraft)
        {
            double x = (aircraft.AlongTrackDistance(Runway) + Runway.LengthNM) * xscale;
            double y = LateralY(aircraft.LateralOffset(Runway));
            if (ToScreenY(y) < 0 || ToScreenY(y) > Canvas.ActualHeight) return false;
            Brush color = Radar.IsWithinCenterlineLimits(aircraft, Runway) ? Brushes.Green : Brushes.Red;
            Point p = PlaceDot(track, x, y, color);
            track.Label.Text = $"{aircraft.Callsign}\n{FormatAltitude(aircraft)}";
            // Label above the track.
            Canvas.SetLeft(track.Label, p.X - 15);
            Canvas.SetTop(track.Label, p.Y - 40);
            return true;
        }
    }
}
