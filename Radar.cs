namespace AuroraPAR
{
    /// <summary>
    /// Radar equipment geometry: approach limits, scan limits and antenna tilt.
    /// Limits come from the active profile (they depend on the radar type, not on the runway);
    /// the tilt is the current antenna position, changed during the session and never saved.
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

        /// <summary>Scan limits with the antenna in neutral position, angles from the antenna.</summary>
        public double ScanUp { get; set; } = 8;
        public double ScanDown { get; set; } = -1;
        public double ScanLeft { get; set; } = 10;
        public double ScanRight { get; set; } = 10;

        /// <summary>
        /// Fixed angles used for the scale of the views (the default scan limits), so the scale depends only on the
        /// range: changing the scan limits or tilting the antenna moves the lines without resizing everything else.
        /// </summary>
        public const double ReferenceScanUp = 8;
        public const double ReferenceScanHalfWidth = 10;

        public double TiltStep { get; set; } = 2;
        public double TiltMax { get; set; } = 10;

        /// <summary>Current elevation tilt, positive up.</summary>
        public double TiltElevation { get; private set; }
        /// <summary>Current azimuth tilt, positive to the right.</summary>
        public double TiltAzimuth { get; private set; }

        public bool IsNeutral => TiltElevation == 0 && TiltAzimuth == 0;

        /// <summary>Elevation scan limits including the tilt.</summary>
        public double ElevationLower => ScanDown + TiltElevation;
        public double ElevationUpper => ScanUp + TiltElevation;
        /// <summary>Azimuth scan limits including the tilt (signed, positive right).</summary>
        public double AzimuthLeftEdge => -ScanLeft + TiltAzimuth;
        public double AzimuthRightEdge => ScanRight + TiltAzimuth;

        /// <summary>
        /// Copies the limits from a profile. The tilt is kept.
        /// </summary>
        public void ApplyProfile(Profile profile)
        {
            ApproachAbove = profile.ApproachAbove;
            ApproachBelow = profile.ApproachBelow;
            ApproachLeft = profile.ApproachLeft;
            ApproachRight = profile.ApproachRight;
            ScanUp = profile.ScanUp;
            ScanDown = profile.ScanDown;
            ScanLeft = profile.ScanLeft;
            ScanRight = profile.ScanRight;
            TiltStep = profile.TiltStep;
            TiltMax = profile.TiltMax;
            TiltElevation = Math.Clamp(TiltElevation, -TiltMax, TiltMax);
            TiltAzimuth = Math.Clamp(TiltAzimuth, -TiltMax, TiltMax);
        }

        /// <summary>
        /// Moves the antenna by the given number of steps (positive up / right). Returns true if it moved.
        /// </summary>
        public bool Tilt(int elevationSteps, int azimuthSteps)
        {
            double elevation = Math.Clamp(Math.Round(TiltElevation + elevationSteps * TiltStep, 3), -TiltMax, TiltMax);
            double azimuth = Math.Clamp(Math.Round(TiltAzimuth + azimuthSteps * TiltStep, 3), -TiltMax, TiltMax);
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
        /// True when the aircraft is inside the scan limits (as drawn): in front of the antenna, within the
        /// displayed range, between the azimuth and elevation limits and above the ground.
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
