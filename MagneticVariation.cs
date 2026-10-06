using System.Globalization;

namespace AuroraPAR
{
    /// <summary>
    /// Magnetic variation (declination), in degrees, East positive, and the magnetic final course.
    /// Written like on the charts: "3E", "2.5W"; a signed number ("3", "-2.5") is also accepted.
    /// </summary>
    internal static class MagneticVariation
    {
        public static bool TryParse(string? text, out double variation)
        {
            variation = 0;
            if (text == null) return false;
            string t = text.Trim().ToUpperInvariant().Replace(',', '.').Replace("°", "").Replace(" ", "");
            if (t.Length == 0) return false;
            double sign = 1;
            if (t.EndsWith('E') || t.StartsWith('E'))
            {
                t = t.Trim('E');
            }
            else if (t.EndsWith('W') || t.StartsWith('W') || t.EndsWith('O') || t.StartsWith('O'))
            {
                // O: "Ovest" (Italian), as for the coordinates.
                t = t.Trim('W', 'O');
                sign = -1;
            }
            if (!double.TryParse(t, NumberStyles.Float, CultureInfo.InvariantCulture, out double value)) return false;
            if (sign < 0 && value < 0) return false;
            value *= sign;
            if (value < -90 || value > 90) return false;
            variation = value;
            return true;
        }

        public static string Format(double variation)
        {
            string number = Math.Abs(variation).ToString("0.#", CultureInfo.InvariantCulture);
            return variation == 0 ? "0" : number + (variation > 0 ? "E" : "W");
        }

        /// <summary>
        /// Magnetic final course from the true heading of the runway: true − variation (East positive),
        /// rounded to the degree, 1 to 360.
        /// </summary>
        public static int FinalCourse(double trueHeading, double variation)
        {
            int course = (int)Math.Round(trueHeading - variation, MidpointRounding.AwayFromZero) % 360;
            if (course <= 0) course += 360;
            return course;
        }
    }
}
