using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace AuroraPAR
{
    /// <summary>
    /// Elevation (profile) view. Logical x: pixels from the far end of the runway (runway end on the left,
    /// approach on the right); logical y: pixels from the top, ground (threshold elevation) at the bottom.
    /// </summary>
    internal class ProfileView(Canvas canvas, Runway runway, Radar radar, ViewOptions options) : RadarView(canvas, runway, radar, options)
    {
        private double H => Canvas.ActualHeight;

        /// <summary>
        /// Distance of the antenna from the far end of the runway, in NM.
        /// </summary>
        private double AntennaNM => Radar.AntennaFromRunwayEnd(Runway);

        protected override void CalculateScale()
        {
            xscale = (Canvas.ActualWidth - 50) / (Runway.Distance + Runway.LengthNM);
            // Fixed vertical scale, depending only on the range: the default upper scan limit (8°) reaches the top at
            // the end of the range. Other scan limits or a tilt move the lines (beyond the view if needed), as on a real PAR.
            double top = Radar.ScanHeight(Runway.Distance + Runway.LengthNM - AntennaNM, Radar.ReferenceScanUp);
            yscale = (Canvas.ActualHeight - 50) / top;
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
        /// Logical y of an elevation scan limit (angle from the antenna) at the given distance (NM) from the far end of the runway.
        /// </summary>
        private double ScanY(double fromRunwayEndNM, double angle)
        {
            return Y(Radar.ScanHeight(Math.Max(0, fromRunwayEndNM - AntennaNM), angle));
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
            double end = length + range;
            double upper = Radar.ElevationUpper;
            double lower = Radar.ElevationLower;
            // Ground beyond the threshold.
            AddLine(length * xscale, H, end * xscale, H, Options.Palette.Ground, 2);
            // Runway.
            AddLine(0, H, length * xscale, H, Options.Palette.Runway, 3);
            // Threshold: a line up to the scan limit, or a symbol on the runway.
            if (Options.ThresholdSymbol.Shape == SymbolShape.Line)
            {
                AddLine(length * xscale, H, length * xscale, Math.Min(H, ScanY(length, upper)), Options.Palette.Runway, 3);
            }
            else
            {
                AddSymbol(Options.ThresholdSymbol, length * xscale, H - Options.ThresholdSymbol.Size / 2, Options.Palette.Runway);
            }
            // Scan limits, from the antenna (the lower one only when above the ground).
            AddLine(AntennaNM * xscale, H, end * xscale, ScanY(end, upper), Options.Palette.ScanLimit, 3);
            if (lower > 0)
            {
                AddLine(AntennaNM * xscale, H, end * xscale, ScanY(end, lower), Options.Palette.ScanLimit, 3);
            }
            AddSymbol(Options.AntennaSymbol, AntennaNM * xscale, H - Options.AntennaSymbol.Size / 2, Options.Palette.ScanLimit);
            // Glide path and its approach limits, all starting at the touchdown point.
            AddGlidePathLine(0, Options.Palette.GlidePath, 2);
            AddGlidePathLine(-Radar.ApproachBelow, Options.Palette.ApproachLimit, 1);
            AddGlidePathLine(Radar.ApproachAbove, Options.Palette.ApproachLimit, 1);
            // Decision height: horizontal line from the touchdown point to 3 NM (or the end of the display),
            // and a dashed vertical line from its intercept with the glide path down to the runway axis.
            double displayEnd = Runway.TouchdownNM + range;
            AddLine(X(0), Y(Runway.MDH), X(Math.Min(Runway.DecisionHeightLineLength, displayEnd)), Y(Runway.MDH), Options.Palette.DecisionHeight, 2);
            double intercept = Runway.MissedApproachPointNM;
            if (intercept <= displayEnd)
            {
                AddLine(X(intercept), Y(0), X(intercept), Y(Runway.MDH), Options.Palette.DecisionHeight, 2, dashed: true);
            }
            // Touchdown point: origin of the range marks and of the glide path.
            if (Options.TouchdownSymbol.Shape == SymbolShape.Line)
            {
                AddLine(X(0), Y(0), X(0), Y(0) - Options.TouchdownSymbol.Size, Options.Palette.Touchdown, 2);
            }
            else
            {
                AddSymbol(Options.TouchdownSymbol, X(0), H - Options.TouchdownSymbol.Size / 2, Options.Palette.Touchdown);
            }
            // Range marks, measured from the touchdown point, between the scan limits.
            int num = Runway.Distance == 15 ? 15 : 10;
            for (int i = 1; i <= num; i++)
            {
                double markNM = length - Runway.TouchdownNM + i * range / num;
                double top = ScanY(markNM, upper);
                double bottom = lower > 0 ? ScanY(markNM, lower) : H;
                if (top < bottom)
                {
                    AddLine(markNM * xscale, bottom, markNM * xscale, top, Options.Palette.RangeMark, 1);
                }
                AddText($"{(i * range / num).ToString(System.Globalization.CultureInfo.InvariantCulture)}NM", markNM * xscale, H, -10, Options.Palette.RangeText, aboveAnchor: true);
            }
            if (Options.ShowAltitudeScale)
            {
                DrawAltitudeScale();
            }
        }

        /// <summary>
        /// Altitude scale on the runway side: altitudes with QNH, heights above the threshold with QFE,
        /// in feet or metres, at a round step chosen for the current zoom.
        /// </summary>
        private void DrawAltitudeScale()
        {
            const double FeetPerMetre = 1 / 0.3048;
            double unitToFeet = Options.ScaleInMetres ? FeetPerMetre : 1;
            string unit = Options.ScaleInMetres ? "m" : "ft";
            // Value (in display units) at the bottom (threshold elevation) and at the top of the view.
            double baseValue = Options.Qfe ? 0 : Runway.Elevation / unitToFeet;
            double topValue = baseValue + H / yscale / unitToFeet;
            double[] steps = [10, 20, 25, 50, 100, 200, 250, 500, 1000, 2000, 2500, 5000, 10000];
            double step = steps.FirstOrDefault(s => s >= (topValue - baseValue) / 6, steps[^1]);
            for (double value = Math.Ceiling(baseValue / step) * step; value <= topValue; value += step)
            {
                double y = Y((value - baseValue) * unitToFeet);
                if (y < 10 || y > H - 22) continue;
                AddLine(0, y, 8, y, Options.Palette.ScaleText, 1);
                AddSideText($"{value:0} {unit}", y, Options.Palette.ScaleText);
            }
        }

        protected override (double Along, double Value) ToWorld(Aircraft aircraft)
        {
            return (aircraft.AlongTrackDistance(Runway), aircraft.Altitude - Runway.Elevation);
        }

        protected override Point WorldToLogical(double along, double value)
        {
            return new Point((along + Runway.LengthNM) * xscale, Y(value));
        }

        protected override Brush TrackColor(Aircraft aircraft)
        {
            return Radar.IsWithinGlidePathLimits(aircraft, Runway) ? Options.Palette.TrackIn : Options.Palette.TrackOut;
        }

        protected override LabelLayout Layout => Options.ElevationLabel;

        protected override bool IsElevation => true;

        protected override Point SweepOrigin() => new(AntennaNM * xscale, H);

        protected override Point SweepEnd(double position)
        {
            double end = Runway.LengthNM + Runway.Distance;
            double angle = Radar.ElevationLower + position * (Radar.ElevationUpper - Radar.ElevationLower);
            return new Point(end * xscale, ScanY(end, angle));
        }
    }
}
