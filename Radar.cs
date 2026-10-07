namespace AuroraPAR
{
    /// <summary>
    /// Radar equipment geometry: approach limits, scan limits, antenna beam and antenna tilt.
    /// Limits and beam come from the active profile (they depend on the radar type, not on the runway);
    /// the tilt is the current antenna position, changed during the session and never saved.
    ///
    /// The scan limits are the physical limits of the antenna (fixed). The antenna beam is narrower and is moved
    /// by the tilt inside them: only what is inside the beam is seen. The tilt is an offset from the neutral
    /// position of the beam, and stops where the beam reaches a scan limit (no tilt at all when the beam is as
    /// wide as the scan limits).
    ///
    /// Angles are in degrees. Lateral angles are signed: positive to the right of the approach course,
    /// as seen by the pilot flying the approach (same sign as <see cref="Aircraft.LateralOffset"/>).
    /// The antenna is on the runway centreline, at half the runway length.
    /// </summary>
    internal class Radar
    {
        /// <summary>Approach limits, angles from the ideal glide path (above/below) and centreline (left/right) at the touchdown point.</summary>
        public double ApproachAbove { get; set; } = 0.5;
        public double ApproachBelow { get; set; } = 0.5;
        public double ApproachLeft { get; set; } = 1.5;
        public double ApproachRight { get; set; } = 1.5;

        /// <summary>Scan limits: physical limits of the antenna, angles from the antenna (fixed, not tilted).</summary>
        public double ScanUp { get; set; } = 18;
        public double ScanDown { get; set; } = -11;
        public double ScanLeft { get; set; } = 20;
        public double ScanRight { get; set; } = 20;

        /// <summary>Width of the antenna beam (degrees) and its centre in neutral position (EL up, AZ right).</summary>
        public double BeamElevation { get; set; } = 9;
        public double BeamAzimuth { get; set; } = 20;
        public double BeamElevationNeutral { get; set; } = 3.5;
        public double BeamAzimuthNeutral { get; set; }

        /// <summary>
        /// Fixed angles used for the scale of the views (the default scan limits), so the scale depends only on the
        /// range: changing the scan limits or tilting the antenna moves the lines without resizing everything else.
        /// </summary>
        public const double ReferenceScanUp = 8;
        public const double ReferenceScanHalfWidth = 10;

        public double TiltStep { get; set; } = 2;

        /// <summary>Current elevation tilt (from the neutral position of the beam), positive up.</summary>
        public double TiltElevation { get; private set; }
        /// <summary>Current azimuth tilt (from the neutral position of the beam), positive to the right.</summary>
        public double TiltAzimuth { get; private set; }

        public bool IsNeutral => TiltElevation == 0 && TiltAzimuth == 0;

        /// <summary>
        /// Positions of the beam centre that keep the whole beam inside the scan limits; a beam wider than the scan
        /// limits stays in the middle.
        /// </summary>
        private static (double Min, double Max) CentreRange(double lower, double upper, double width)
        {
            double min = lower + width / 2;
            double max = upper - width / 2;
            if (min > max)
            {
                double middle = (lower + upper) / 2;
                return (middle, middle);
            }
            return (min, max);
        }

        private (double Min, double Max) ElevationCentreRange => CentreRange(ScanDown, ScanUp, BeamElevation);
        private (double Min, double Max) AzimuthCentreRange => CentreRange(-ScanLeft, ScanRight, BeamAzimuth);

        /// <summary>Beam centre in neutral position (the one of the profile, brought inside the possible range).</summary>
        private double ElevationNeutralCentre => Math.Clamp(BeamElevationNeutral, ElevationCentreRange.Min, ElevationCentreRange.Max);
        private double AzimuthNeutralCentre => Math.Clamp(BeamAzimuthNeutral, AzimuthCentreRange.Min, AzimuthCentreRange.Max);

        /// <summary>Tilt range from neutral (minimum ≤ 0 ≤ maximum).</summary>
        public double TiltElevationMin => ElevationCentreRange.Min - ElevationNeutralCentre;
        public double TiltElevationMax => ElevationCentreRange.Max - ElevationNeutralCentre;
        public double TiltAzimuthMin => AzimuthCentreRange.Min - AzimuthNeutralCentre;
        public double TiltAzimuthMax => AzimuthCentreRange.Max - AzimuthNeutralCentre;

        private double ElevationCentre => ElevationNeutralCentre + TiltElevation;
        private double AzimuthCentre => AzimuthNeutralCentre + TiltAzimuth;

        /// <summary>Elevation edges of the antenna beam, tilt included (inside the scan limits).</summary>
        public double ElevationLower => Math.Max(ScanDown, ElevationCentre - BeamElevation / 2);
        public double ElevationUpper => Math.Min(ScanUp, ElevationCentre + BeamElevation / 2);
        /// <summary>Azimuth edges of the antenna beam, tilt included (signed, positive right; inside the scan limits).</summary>
        public double AzimuthLeftEdge => Math.Max(-ScanLeft, AzimuthCentre - BeamAzimuth / 2);
        public double AzimuthRightEdge => Math.Min(ScanRight, AzimuthCentre + BeamAzimuth / 2);

        /// <summary>Azimuth scan limits as signed angles (positive right).</summary>
        public double ScanLeftEdge => -ScanLeft;
        public double ScanRightEdge => ScanRight;

        /// <summary>
        /// Copies the limits and the beam from a profile. The tilt is kept, inside its new range.
        /// </summary>
        public void ApplyProfile(Profile profile)
        {
            profile.MigrateScanModel();
            ApproachAbove = profile.ApproachAbove;
            ApproachBelow = profile.ApproachBelow;
            ApproachLeft = profile.ApproachLeft;
            ApproachRight = profile.ApproachRight;
            ScanUp = profile.ScanUp;
            ScanDown = profile.ScanDown;
            ScanLeft = profile.ScanLeft;
            ScanRight = profile.ScanRight;
            BeamElevation = profile.BeamElevation;
            BeamAzimuth = profile.BeamAzimuth;
            BeamElevationNeutral = profile.BeamElevationNeutral;
            BeamAzimuthNeutral = profile.BeamAzimuthNeutral;
            TiltStep = profile.TiltStep;
            TiltElevation = Math.Clamp(TiltElevation, TiltElevationMin, TiltElevationMax);
            TiltAzimuth = Math.Clamp(TiltAzimuth, TiltAzimuthMin, TiltAzimuthMax);
        }

        /// <summary>
        /// Moves the antenna by the given number of steps (positive up / right). Returns true if it moved.
        /// </summary>
        public bool Tilt(int elevationSteps, int azimuthSteps)
        {
            double elevation = Math.Clamp(Math.Round(TiltElevation + elevationSteps * TiltStep, 3), TiltElevationMin, TiltElevationMax);
            double azimuth = Math.Clamp(Math.Round(TiltAzimuth + azimuthSteps * TiltStep, 3), TiltAzimuthMin, TiltAzimuthMax);
            bool moved = elevation != TiltElevation || azimuth != TiltAzimuth;
            TiltElevation = elevation;
            TiltAzimuth = azimuth;
            return moved;
        }

        /// <summary>
        /// Back to the neutral position. Returns true if it moved.
        /// </summary>
        public bool Neutral()
        {
            bool moved = !IsNeutral;
            TiltElevation = 0;
            TiltAzimuth = 0;
            return moved;
        }

        /// <summary>
        /// Elevation (or azimuth) only back to neutral. Returns true if it moved.
        /// </summary>
        public bool NeutralElevation()
        {
            bool moved = TiltElevation != 0;
            TiltElevation = 0;
            return moved;
        }

        public bool NeutralAzimuth()
        {
            bool moved = TiltAzimuth != 0;
            TiltAzimuth = 0;
            return moved;
        }

        /// <summary>Number of tilt steps from neutral to the end of a tilt range (the last one may be shorter).</summary>
        private int Steps(double span) => TiltStep > 0 && span > 0 ? (int)Math.Ceiling(Math.Round(span / TiltStep, 3)) : 0;

        public int ElevationStepsDown => Steps(-TiltElevationMin);
        public int ElevationStepsUp => Steps(TiltElevationMax);
        public int AzimuthStepsLeft => Steps(-TiltAzimuthMin);
        public int AzimuthStepsRight => Steps(TiltAzimuthMax);

        /// <summary>
        /// Distance of the antenna from the far end of the runway, in NM.
        /// </summary>
        public static double AntennaFromRunwayEnd(Runway runway) => runway.LengthNM / 2;

        private static double Tan(double degrees) => Math.Tan(degrees * Math.PI / 180);

        /// <summary>
        /// Height in ft above the threshold of an elevation scan limit (angle from the antenna) at the given
        /// distance from the antenna in NM.
        /// </summary>
        public static double ScanHeight(double distanceFromAntennaNM, double angle)
        {
            return distanceFromAntennaNM * Tan(angle) * Runway.FeetPerNM;
        }

        /// <summary>
        /// Lateral position in NM (positive right) of an azimuth scan limit at the given distance from the antenna.
        /// </summary>
        public static double ScanOffset(double distanceFromAntennaNM, double angle)
        {
            return distanceFromAntennaNM * Tan(angle);
        }

        /// <summary>
        /// Half width in NM of the approach limit on one side of the centreline, at the given distance from touchdown.
        /// </summary>
        public double ApproachHalfWidth(double distanceFromTouchdownNM, bool right)
        {
            return distanceFromTouchdownNM * Tan(right ? ApproachRight : ApproachLeft);
        }

        /// <summary>
        /// True when the aircraft is inside the antenna beam (as drawn; the beam is inside the scan limits): in front
        /// of the antenna, within the displayed range, between the azimuth and elevation edges of the beam and above
        /// the ground. Only these aircraft are seen by the radar.
        /// </summary>
        public bool IsInsideScan(Aircraft aircraft, Runway runway)
        {
            double fromRunwayEnd = runway.LengthNM + aircraft.AlongTrackDistance(runway);
            if (fromRunwayEnd > runway.LengthNM + runway.Distance) return false;
            double fromAntenna = fromRunwayEnd - AntennaFromRunwayEnd(runway);
            if (fromAntenna <= 0) return false;
            double azimuth = Math.Atan2(aircraft.LateralOffset(runway), fromAntenna) * 180 / Math.PI;
            if (azimuth < AzimuthLeftEdge || azimuth > AzimuthRightEdge) return false;
            double height = aircraft.Altitude - runway.Elevation;
            if (height <= 0) return false;
            double elevation = Math.Atan2(height / Runway.FeetPerNM, fromAntenna) * 180 / Math.PI;
            return elevation >= ElevationLower && elevation <= ElevationUpper;
        }

        /// <summary>
        /// True when the aircraft is inside the vertical approach limits (angles from the touchdown point).
        /// </summary>
        public bool IsWithinGlidePathLimits(Aircraft aircraft, Runway runway)
        {
            double s = aircraft.DistanceFromTouchdown(runway);
            double height = aircraft.Altitude - runway.Elevation;
            return height > runway.GlidePathHeight(s, -ApproachBelow)
                && height < runway.GlidePathHeight(s, ApproachAbove);
        }

        /// <summary>
        /// True when the aircraft is inside the lateral approach limits (angles from the touchdown point).
        /// </summary>
        public bool IsWithinCenterlineLimits(Aircraft aircraft, Runway runway)
        {
            double offset = aircraft.LateralOffset(runway);
            return Math.Abs(offset) <= ApproachHalfWidth(aircraft.DistanceFromTouchdown(runway), right: offset > 0);
        }
    }
}
