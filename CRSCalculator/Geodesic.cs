using System.Globalization;
using System.Text.RegularExpressions;

namespace AuroraPAR
{
    /// <summary>
    /// Direction and distance between two points: on the WGS84 ellipsoid (as the charts give the true heading of a
    /// runway) and on the sphere that AuroraPAR uses for its geometry (bearing and distances of Data.cs).
    /// </summary>
    internal static class Geodesic
    {
        private const double A = 6378137.0;
        private const double Flattening = 1 / 298.257223563;

        /// <summary>
        /// Distance (m) and azimuths (degrees, from true North) at the first and at the second point, on the WGS84
        /// ellipsoid (Vincenty inverse formula). NaN when the iteration does not converge (nearly opposite points).
        /// </summary>
        public static (double Distance, double Azimuth1, double Azimuth2) Inverse(double lat1, double lon1, double lat2, double lon2)
        {
            double f = Flattening;
            double b = (1 - f) * A;
            double u1 = Math.Atan((1 - f) * Math.Tan(Rad(lat1)));
            double u2 = Math.Atan((1 - f) * Math.Tan(Rad(lat2)));
            double l = Rad(lon2 - lon1);
            double lambda = l;
            double sinU1 = Math.Sin(u1), cosU1 = Math.Cos(u1), sinU2 = Math.Sin(u2), cosU2 = Math.Cos(u2);
            double sinSigma = 0, cosSigma = 0, sigma = 0, cos2Alpha = 0, cos2SigmaM = 0, sinLambda = 0, cosLambda = 0;
            bool converged = false;
            for (int i = 0; i < 200; i++)
            {
                sinLambda = Math.Sin(lambda);
                cosLambda = Math.Cos(lambda);
                sinSigma = Math.Sqrt(Math.Pow(cosU2 * sinLambda, 2) + Math.Pow(cosU1 * sinU2 - sinU1 * cosU2 * cosLambda, 2));
                if (sinSigma == 0) return (0, 0, 0);
                cosSigma = sinU1 * sinU2 + cosU1 * cosU2 * cosLambda;
                sigma = Math.Atan2(sinSigma, cosSigma);
                double sinAlpha = cosU1 * cosU2 * sinLambda / sinSigma;
                cos2Alpha = 1 - sinAlpha * sinAlpha;
                cos2SigmaM = cos2Alpha != 0 ? cosSigma - 2 * sinU1 * sinU2 / cos2Alpha : 0;
                double c = f / 16 * cos2Alpha * (4 + f * (4 - 3 * cos2Alpha));
                double previous = lambda;
                lambda = l + (1 - c) * f * sinAlpha * (sigma + c * sinSigma * (cos2SigmaM + c * cosSigma * (-1 + 2 * cos2SigmaM * cos2SigmaM)));
                if (Math.Abs(lambda - previous) < 1e-12)
                {
                    converged = true;
                    break;
                }
            }
            if (!converged) return (double.NaN, double.NaN, double.NaN);
            double uSquared = cos2Alpha * (A * A - b * b) / (b * b);
            double bigA = 1 + uSquared / 16384 * (4096 + uSquared * (-768 + uSquared * (320 - 175 * uSquared)));
            double bigB = uSquared / 1024 * (256 + uSquared * (-128 + uSquared * (74 - 47 * uSquared)));
            double deltaSigma = bigB * sinSigma * (cos2SigmaM + bigB / 4 * (cosSigma * (-1 + 2 * cos2SigmaM * cos2SigmaM)
                - bigB / 6 * cos2SigmaM * (-3 + 4 * sinSigma * sinSigma) * (-3 + 4 * cos2SigmaM * cos2SigmaM)));
            double distance = b * bigA * (sigma - deltaSigma);
            double azimuth1 = Math.Atan2(cosU2 * Math.Sin(lambda), cosU1 * sinU2 - sinU1 * cosU2 * Math.Cos(lambda));
            double azimuth2 = Math.Atan2(cosU1 * Math.Sin(lambda), -sinU1 * cosU2 + cosU1 * sinU2 * Math.Cos(lambda));
            return (distance, Normalize(Deg(azimuth1)), Normalize(Deg(azimuth2)));
        }

        /// <summary>
        /// True heading of a runway on the ellipsoid: the direction at the middle between the thresholds (mean of the
        /// azimuth at the first and the one at the second point).
        /// </summary>
        public static double MeanAzimuth(double azimuth1, double azimuth2)
        {
            double difference = ((azimuth2 - azimuth1 + 540) % 360) - 180;
            return Normalize(azimuth1 + difference / 2);
        }

        /// <summary>
        /// Initial bearing on the sphere, with the same formula as <c>Aircraft.BearingFromRunway</c> of AuroraPAR
        /// (geodetic latitudes and longitudes used as if the Earth were a sphere).
        /// </summary>
        public static double SphericalBearing(double lat1, double lon1, double lat2, double lon2)
        {
            double phi1 = Rad(lat1), phi2 = Rad(lat2), dLon = Rad(lon2 - lon1);
            double y = Math.Sin(dLon) * Math.Cos(phi2);
            double x = Math.Cos(phi1) * Math.Sin(phi2) - Math.Sin(phi1) * Math.Cos(phi2) * Math.Cos(dLon);
            return Normalize(Deg(Math.Atan2(y, x)));
        }

        public static double Normalize(double degrees)
        {
            double d = degrees % 360;
            return d < 0 ? d + 360 : d;
        }

        private static double Rad(double degrees) => degrees * Math.PI / 180;
        private static double Deg(double radians) => radians * 180 / Math.PI;
    }

    /// <summary>
    /// How precisely a coordinate was written (number of decimals), and the error that this can cause in the heading.
    /// </summary>
    internal static class CoordinatePrecision
    {
        private static readonly Regex Numbers = new(@"\d+(?:\.\d+)?");

        /// <summary>
        /// Largest rounding error of a coordinate written as text, in degrees: half of the last digit written
        /// (degrees, minutes or seconds, with their decimals).
        /// </summary>
        public static double MaxErrorDegrees(string text, bool latitude)
        {
            string s = text.Trim().Replace(',', '.');
            MatchCollection tokens = Numbers.Matches(s);
            if (tokens.Count == 0) return 0;
            string last = tokens[^1].Value;
            int dot = last.IndexOf('.');
            int decimals = dot < 0 ? 0 : last.Length - dot - 1;
            double unit;
            if (tokens.Count >= 3)
            {
                unit = 1.0 / 3600;
            }
            else if (tokens.Count == 2)
            {
                unit = 1.0 / 60;
            }
            else
            {
                // A single number: decimal degrees, or compact DDMM / DDMMSS (DDDMM / DDDMMSS for a longitude).
                int integerDigits = dot < 0 ? last.Length : dot;
                int degreeDigits = latitude ? 2 : 3;
                unit = integerDigits <= degreeDigits ? 1.0 : integerDigits <= degreeDigits + 2 ? 1.0 / 60 : 1.0 / 3600;
            }
            return unit * Math.Pow(10, -decimals) / 2;
        }

        /// <summary>
        /// Worst error of the heading (degrees) from the rounding of the two thresholds, which are
        /// <paramref name="distanceMetres"/> apart.
        /// </summary>
        public static double HeadingErrorDegrees(double latitude, double latError, double lonError, double distanceMetres)
        {
            const double metresPerDegree = 111320;
            double eLat = latError * metresPerDegree;
            double eLon = lonError * metresPerDegree * Math.Cos(latitude * Math.PI / 180);
            double pointError = Math.Sqrt(eLat * eLat + eLon * eLon);
            return Math.Atan(2 * pointError / Math.Max(1, distanceMetres)) * 180 / Math.PI;
        }

        public static string Format(double value) => value.ToString("0.##", CultureInfo.InvariantCulture);
    }
}
