namespace AuroraPAR
{
    /// <summary>Speed of the antenna scan effect.</summary>
    internal enum ScanEffectSpeed
    {
        Slow,
        Normal,
        Fast
    }

    /// <summary>
    /// Antenna scan effect, as on the old PAR screens: a beam sweeps the elevation view (up, then down) and the
    /// azimuth view (one side, then back) in turn, leaving a fading glow behind, like the persistence of the phosphor.
    ///
    /// Purely graphic: it is drawn over the views and does not change how or when the tracks are updated.
    /// This class only computes where the beam is; the views draw it (see <see cref="RadarView.RenderSweep"/>).
    /// </summary>
    internal static class ScanEffect
    {
        /// <summary>Number of lines drawn for the beam and its glow (the first is the beam).</summary>
        public const int Lines = 24;
        /// <summary>Time between two lines of the glow, in seconds (the glow lasts Lines × this).</summary>
        private const double GlowStep = 0.02;

        /// <summary>Seconds for one sweep across a view.</summary>
        public static double SweepSeconds(ScanEffectSpeed speed) => speed switch
        {
            ScanEffectSpeed.Slow => 1.0,
            ScanEffectSpeed.Fast => 0.25,
            _ => 0.5
        };

        public static string DisplayName(ScanEffectSpeed speed) => speed switch
        {
            ScanEffectSpeed.Slow => "Slow",
            ScanEffectSpeed.Fast => "Fast",
            _ => "Normal"
        };

        /// <summary>
        /// Position of the beam in a view at time <paramref name="t"/> (seconds): 0 = lower/left scan limit,
        /// 1 = upper/right scan limit; null when the beam is in the other view.
        /// Cycle: elevation up, azimuth one way, elevation down, azimuth back.
        /// </summary>
        public static double? Position(double t, double sweepSeconds, bool elevation)
        {
            double phase = t / sweepSeconds;
            double cycle = Math.Floor(phase);
            double fraction = phase - cycle;
            int step = (int)(((long)cycle % 4 + 4) % 4);
            bool inElevation = step % 2 == 0;
            if (inElevation != elevation) return null;
            return step < 2 ? fraction : 1 - fraction;
        }

        /// <summary>
        /// Beam and glow lines of a view at time <paramref name="t"/>: position (see <see cref="Position"/>, null = not
        /// drawn) and opacity of each line, the beam first. The glow fades also after the beam has moved to the other view.
        /// </summary>
        public static void Compute(double t, ScanEffectSpeed speed, bool elevation, (double? Position, double Opacity)[] lines)
        {
            double sweep = SweepSeconds(speed);
            for (int i = 0; i < lines.Length; i++)
            {
                double? position = Position(t - i * GlowStep, sweep, elevation);
                double fade = 1 - (double)i / lines.Length;
                double opacity = i == 0 ? 0.9 : 0.35 * fade * fade;
                lines[i] = (position, opacity);
            }
        }
    }
}
