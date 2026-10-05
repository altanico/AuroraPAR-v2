using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace AuroraPAR
{
    /// <summary>
    /// Elevation (profile) view. Logical x: pixels from the far end of the runway (runway end on the left,
    /// approach on the right); logical y: pixels from the top, ground (threshold elevation) at the bottom.
    /// </summary>
    internal class ProfileView(Canvas canvas, Runway runway) : RadarView(canvas, runway)
    {
        private double H => Canvas.ActualHeight;

        /// <summary>
        /// Angle of the upper scan limit, in degrees.
        /// </summary>
        private double ScanAngle => Runway.ElevationScanAngle;

        protected override void CalculateScale()
        {
            xscale = (Canvas.ActualWidth - 50) / (Runway.Distance + Runway.LengthNM);
            yscale = (Canvas.ActualHeight - 50) / ((Runway.Distance + Runway.LengthNM) * Math.Tan(ScanAngle * Math.PI / 180) * Runway.FeetPerNM);
        }

        /// <summary>
        /// Logical x of a point at the given distance from the touchdown point.
        /// </summary>
        private double X(double distanceFromTouchdownNM)
        {
            return (Runway.LengthNM - Runway.TouchdownNM + distanceFromTouchdownNM) * xscale;
        }

        /// <summary>
        /// Logical y of a height in ft above the threshold elevation.
        /// </summary>
        private double Y(double heightFt)
        {
            return H - heightFt * yscale;
        }

        /// <summary>
        /// Logical y of the upper scan limit at the given distance (NM) from the far end of the runway.
        /// </summary>
        private double ScanLimitY(double distanceFromRunwayEndNM)
        {
            return H - distanceFromRunwayEndNM * Math.Tan(ScanAngle * Math.PI / 180) * Runway.FeetPerNM * yscale;
        }

        /// <summary>
        /// Draws a line starting at the touchdown point with angle GlideSlope + angleOffset:
        /// dashed from touchdown to threshold, solid from threshold to the end of the display.
        /// </summary>
        private void AddGlidePathLine(double angleOffset, Brush stroke, double thickness)
        {
            double threshold = Runway.TouchdownNM;
            double end = Runway.TouchdownNM + Runway.Distance;
            AddLine(X(0), Y(0), X(threshold), Y(Runway.GlidePathHeight(threshold, angleOffset)), stroke, thickness, dashed: true);
            AddLine(X(threshold), Y(Runway.GlidePathHeight(threshold, angleOffset)), X(end), Y(Runway.GlidePathHeight(end, angleOffset)), stroke, thickness);
        }

        protected override void DrawStatic()
        {
            double length = Runway.LengthNM;
            double range = Runway.Distance;
            // Ground beyond the threshold.
            AddLine(length * xscale, H, (range + length) * xscale, H, Brushes.Green, 2);
            // Runway.
            AddLine(0, H, length * xscale, H, Brushes.Green, 3);
            // Threshold.
            AddLine(length * xscale, H, length * xscale, ScanLimitY(length), Brushes.Green, 3);
            // Upper scan limit.
            AddLine(0, H, (length + range) * xscale, ScanLimitY(length + range), Brushes.CadetBlue, 3);
            // Glide path and its approach limits, all starting at the touchdown point.
            AddGlidePathLine(0, Brushes.Yellow, 2);
            AddGlidePathLine(-Runway.GlidePathTolerance, Brushes.Red, 1);
            AddGlidePathLine(Runway.GlidePathTolerance, Brushes.Red, 1);
            // Minimum (MDH) line and missed approach point, where the glide path reaches it.
            double mapt = Runway.MissedApproachPointNM;
            AddLine(0, Y(Runway.MDH), X(mapt), Y(Runway.MDH), Brushes.Red, 2);
            AddLine(X(mapt), Y(0), X(mapt), Y(Runway.MDH), Brushes.Red, 2);
            // Touchdown point: origin of the range marks and of the glide path.
            AddLine(X(0), Y(0), X(0), Y(0) - 12, Brushes.Yellow, 2);
            // Range marks, measured from the touchdown point.
            int num = Runway.Distance == 15 ? 15 : 10;
            for (int i = 1; i <= num; i++)
            {
                double markNM = length - Runway.TouchdownNM + i * range / num;
                AddLine(markNM * xscale, H, markNM * xscale, ScanLimitY(markNM), Brushes.Green, 1);
                AddText($"{i * range / num}NM", markNM * xscale, H, -10, Brushes.Yellow, aboveAnchor: true);
            }
        }

        protected override bool UpdateTrack(Track track, Aircraft aircraft)
        {
            double x = (aircraft.AlongTrackDistance(Runway) + Runway.LengthNM) * xscale;
            double y = Y(aircraft.Altitude - Runway.Elevation);
            Brush color = aircraft.IsWithinGlidePathTolerance(Runway) ? Brushes.Green : Brushes.Red;
            Point p = PlaceDot(track, x, y, color);
            track.Label.Text = $"{aircraft.Callsign}\n{aircraft.Altitude}\n{aircraft.DistanceFromTouchdown(Runway):0.0}NM";
            // Label above the track: its bottom 15 px above the track's centre.
            Canvas.SetLeft(track.Label, p.X - 15);
            Canvas.SetTop(track.Label, p.Y - 15 - TextHeight(track.Label));
            return true;
        }
    }
}
