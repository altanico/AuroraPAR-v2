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
            // Fixed lateral scale, depending only on the range: the default scan limits (±10°) fill the view at the end
            // of the range. Other scan limits or a tilt move the lines (beyond the view if needed), as on a real PAR.
            double half = Radar.ScanOffset(Runway.Distance + Runway.LengthNM - AntennaNM, Radar.ReferenceScanHalfWidth);
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
            // Runway and threshold (a line across the runway, or a symbol).
            AddLine(0, cy, length * xscale, cy, Brushes.Green, 3);
            if (Options.ThresholdSymbol.Shape == SymbolShape.Line)
            {
                AddLine(length * xscale, cy - Runway.WidthNM * yscale, length * xscale, cy + Runway.WidthNM * yscale, Brushes.Green, 3);
            }
            else
            {
                AddSymbol(Options.ThresholdSymbol, length * xscale, cy, Brushes.Green);
            }
            // Scan limits, from the antenna.
            AddLine(AntennaNM * xscale, cy, end * xscale, ScanY(end, left), Brushes.CadetBlue, 3);
            AddLine(AntennaNM * xscale, cy, end * xscale, ScanY(end, right), Brushes.CadetBlue, 3);
            AddSymbol(Options.AntennaSymbol, AntennaNM * xscale, cy, Brushes.CadetBlue);
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
            if (Options.TouchdownSymbol.Shape == SymbolShape.Line)
            {
                double half = Options.TouchdownSymbol.Size * 2 / 3;
                AddLine(xTouchdown, cy - half, xTouchdown, cy + half, Brushes.Yellow, 2);
            }
            else
            {
                AddSymbol(Options.TouchdownSymbol, xTouchdown, cy, Brushes.Yellow);
            }
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

        protected override (double Along, double Value) ToWorld(Aircraft aircraft)
        {
            return (aircraft.AlongTrackDistance(Runway), aircraft.LateralOffset(Runway));
        }

        protected override Point WorldToLogical(double along, double value)
        {
            return new Point((along + Runway.LengthNM) * xscale, LateralY(value));
        }

        protected override bool IsDrawable(Point logical)
        {
            return logical.Y >= 0 && logical.Y <= Canvas.ActualHeight;
        }

        protected override Brush TrackColor(Aircraft aircraft)
        {
            return Radar.IsWithinCenterlineLimits(aircraft, Runway) ? Brushes.Green : Brushes.Red;
        }

        protected override LabelLayout Layout => Options.AzimuthLabel;
    }
}
