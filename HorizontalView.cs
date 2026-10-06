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
            CalculateHorizontalScale();
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
            AddLine(0, cy, length * xscale, cy, Options.Palette.Runway, 3);
            if (Options.ThresholdSymbol.Shape == SymbolShape.Line)
            {
                AddLine(length * xscale, cy - Runway.WidthNM * yscale, length * xscale, cy + Runway.WidthNM * yscale, Options.Palette.Runway, 3);
            }
            else
            {
                AddSymbol(Options.ThresholdSymbol, length * xscale, cy, Options.Palette.Runway);
            }
            // Scan limits, from the antenna.
            AddLine(AntennaNM * xscale, cy, end * xscale, ScanY(end, left), Options.Palette.ScanLimit, 3);
            AddLine(AntennaNM * xscale, cy, end * xscale, ScanY(end, right), Options.Palette.ScanLimit, 3);
            AddSymbol(Options.AntennaSymbol, AntennaNM * xscale, cy, Options.Palette.ScanLimit);
            // Extended centreline and its approach limits, all starting at the touchdown point:
            // dashed between touchdown and threshold, solid beyond the threshold.
            double xTouchdown = (length - Runway.TouchdownNM) * xscale;
            double xThreshold = length * xscale;
            double xEnd = end * xscale;
            AddLine(xTouchdown, cy, xThreshold, cy, Options.Palette.GlidePath, 2, dashed: true);
            AddLine(xThreshold, cy, xEnd, cy, Options.Palette.GlidePath, 2);
            foreach (bool isRight in new[] { false, true })
            {
                double sign = isRight ? 1 : -1;
                double atThreshold = LateralY(sign * Radar.ApproachHalfWidth(Runway.TouchdownNM, isRight));
                double atEnd = LateralY(sign * Radar.ApproachHalfWidth(Runway.TouchdownNM + range, isRight));
                AddLine(xTouchdown, cy, xThreshold, atThreshold, Options.Palette.ApproachLimit, 1, dashed: true);
                AddLine(xThreshold, atThreshold, xEnd, atEnd, Options.Palette.ApproachLimit, 1);
            }
            // Touchdown point.
            if (Options.TouchdownSymbol.Shape == SymbolShape.Line)
            {
                double half = Options.TouchdownSymbol.Size * 2 / 3;
                AddLine(xTouchdown, cy - half, xTouchdown, cy + half, Options.Palette.Touchdown, 2);
            }
            else
            {
                AddSymbol(Options.TouchdownSymbol, xTouchdown, cy, Options.Palette.Touchdown);
            }
            // Distance where the glide path reaches the decision height: vertical line between the scan limits.
            double interceptNM = length - Runway.TouchdownNM + Runway.MissedApproachPointNM;
            if (interceptNM <= end)
            {
                AddLine(interceptNM * xscale, ScanY(interceptNM, left), interceptNM * xscale, ScanY(interceptNM, right), Options.Palette.DecisionHeight, 2);
            }
            // Range marks, measured from the touchdown point, between the scan limits.
            foreach ((double distance, bool major) in RangeMarks.For(range))
            {
                double markNM = distance - Runway.TouchdownNM + length;
                AddLine(markNM * xscale, ScanY(markNM, left), markNM * xscale, ScanY(markNM, right),
                    major ? Options.Palette.RangeMark : Options.Palette.RangeMarkMinor, 1, dashed: !major);
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
            return Radar.IsWithinCenterlineLimits(aircraft, Runway) ? Options.Palette.TrackIn : Options.Palette.TrackOut;
        }

        protected override LabelLayout Layout => Options.AzimuthLabel;

        protected override bool IsElevation => false;

        protected override Point SweepOrigin() => new(AntennaNM * xscale, CenterY);

        protected override Point SweepEnd(double position)
        {
            double end = Runway.LengthNM + Runway.Distance;
            double angle = Radar.AzimuthLeftEdge + position * (Radar.AzimuthRightEdge - Radar.AzimuthLeftEdge);
            return new Point(end * xscale, ScanY(end, angle));
        }
    }
}
