namespace AuroraPAR
{
    /// <summary>
    /// World Magnetic Model WMM2025 (NOAA / British Geological Survey, public domain), valid from 2025.0 to 2030.0:
    /// magnetic declination (variation) and inclination anywhere on the Earth, for any date in that period.
    /// The coefficients are those of the official WMM2025.COF file; the calculation follows the model report
    /// (spherical harmonics of degree and order 12) and is checked against the official test values
    /// (see <see cref="SelfTest"/>). Accuracy of the model: about 0.3 degrees in the declination at mid latitudes.
    /// </summary>
    internal static class WorldMagneticModel
    {
        public const double Epoch = 2025.0;
        public const double ValidUntil = 2030.0;
        private const int Degree = 12;

        private static readonly double[,] G = new double[Degree + 1, Degree + 1];
        private static readonly double[,] H = new double[Degree + 1, Degree + 1];
        private static readonly double[,] GDot = new double[Degree + 1, Degree + 1];
        private static readonly double[,] HDot = new double[Degree + 1, Degree + 1];

        /// <summary>n, m, g, h, g dot, h dot (nT and nT/year) of the file WMM2025.COF.</summary>
        private const string Coefficients = """
            1 0 -29351.8 0.0 12.0 0.0
            1 1 -1410.8 4545.4 9.7 -21.5
            2 0 -2556.6 0.0 -11.6 0.0
            2 1 2951.1 -3133.6 -5.2 -27.7
            2 2 1649.3 -815.1 -8.0 -12.1
            3 0 1361.0 0.0 -1.3 0.0
            3 1 -2404.1 -56.6 -4.2 4.0
            3 2 1243.8 237.5 0.4 -0.3
            3 3 453.6 -549.5 -15.6 -4.1
            4 0 895.0 0.0 -1.6 0.0
            4 1 799.5 278.6 -2.4 -1.1
            4 2 55.7 -133.9 -6.0 4.1
            4 3 -281.1 212.0 5.6 1.6
            4 4 12.1 -375.6 -7.0 -4.4
            5 0 -233.2 0.0 0.6 0.0
            5 1 368.9 45.4 1.4 -0.5
            5 2 187.2 220.2 0.0 2.2
            5 3 -138.7 -122.9 0.6 0.4
            5 4 -142.0 43.0 2.2 1.7
            5 5 20.9 106.1 0.9 1.9
            6 0 64.4 0.0 -0.2 0.0
            6 1 63.8 -18.4 -0.4 0.3
            6 2 76.9 16.8 0.9 -1.6
            6 3 -115.7 48.8 1.2 -0.4
            6 4 -40.9 -59.8 -0.9 0.9
            6 5 14.9 10.9 0.3 0.7
            6 6 -60.7 72.7 0.9 0.9
            7 0 79.5 0.0 -0.0 0.0
            7 1 -77.0 -48.9 -0.1 0.6
            7 2 -8.8 -14.4 -0.1 0.5
            7 3 59.3 -1.0 0.5 -0.8
            7 4 15.8 23.4 -0.1 0.0
            7 5 2.5 -7.4 -0.8 -1.0
            7 6 -11.1 -25.1 -0.8 0.6
            7 7 14.2 -2.3 0.8 -0.2
            8 0 23.2 0.0 -0.1 0.0
            8 1 10.8 7.1 0.2 -0.2
            8 2 -17.5 -12.6 0.0 0.5
            8 3 2.0 11.4 0.5 -0.4
            8 4 -21.7 -9.7 -0.1 0.4
            8 5 16.9 12.7 0.3 -0.5
            8 6 15.0 0.7 0.2 -0.6
            8 7 -16.8 -5.2 -0.0 0.3
            8 8 0.9 3.9 0.2 0.2
            9 0 4.6 0.0 -0.0 0.0
            9 1 7.8 -24.8 -0.1 -0.3
            9 2 3.0 12.2 0.1 0.3
            9 3 -0.2 8.3 0.3 -0.3
            9 4 -2.5 -3.3 -0.3 0.3
            9 5 -13.1 -5.2 0.0 0.2
            9 6 2.4 7.2 0.3 -0.1
            9 7 8.6 -0.6 -0.1 -0.2
            9 8 -8.7 0.8 0.1 0.4
            9 9 -12.9 10.0 -0.1 0.1
            10 0 -1.3 0.0 0.1 0.0
            10 1 -6.4 3.3 0.0 0.0
            10 2 0.2 0.0 0.1 -0.0
            10 3 2.0 2.4 0.1 -0.2
            10 4 -1.0 5.3 -0.0 0.1
            10 5 -0.6 -9.1 -0.3 -0.1
            10 6 -0.9 0.4 0.0 0.1
            10 7 1.5 -4.2 -0.1 0.0
            10 8 0.9 -3.8 -0.1 -0.1
            10 9 -2.7 0.9 -0.0 0.2
            10 10 -3.9 -9.1 -0.0 -0.0
            11 0 2.9 0.0 0.0 0.0
            11 1 -1.5 0.0 -0.0 -0.0
            11 2 -2.5 2.9 0.0 0.1
            11 3 2.4 -0.6 0.0 -0.0
            11 4 -0.6 0.2 0.0 0.1
            11 5 -0.1 0.5 -0.1 -0.0
            11 6 -0.6 -0.3 0.0 -0.0
            11 7 -0.1 -1.2 -0.0 0.1
            11 8 1.1 -1.7 -0.1 -0.0
            11 9 -1.0 -2.9 -0.1 0.0
            11 10 -0.2 -1.8 -0.1 0.0
            11 11 2.6 -2.3 -0.1 0.0
            12 0 -2.0 0.0 0.0 0.0
            12 1 -0.2 -1.3 0.0 -0.0
            12 2 0.3 0.7 -0.0 0.0
            12 3 1.2 1.0 -0.0 -0.1
            12 4 -1.3 -1.4 -0.0 0.1
            12 5 0.6 -0.0 -0.0 -0.0
            12 6 0.6 0.6 0.1 -0.0
            12 7 0.5 -0.1 -0.0 -0.0
            12 8 -0.1 0.8 0.0 0.0
            12 9 -0.4 0.1 0.0 -0.0
            12 10 -0.2 -1.0 -0.1 -0.0
            12 11 -1.3 0.1 -0.0 0.0
            12 12 -0.7 0.2 -0.1 -0.1
            """;

        static WorldMagneticModel()
        {
            foreach (string line in Coefficients.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            {
                string[] p = line.Split(' ', StringSplitOptions.RemoveEmptyEntries);
                if (p.Length < 6) continue;
                int n = int.Parse(p[0], System.Globalization.CultureInfo.InvariantCulture);
                int m = int.Parse(p[1], System.Globalization.CultureInfo.InvariantCulture);
                G[n, m] = double.Parse(p[2], System.Globalization.CultureInfo.InvariantCulture);
                H[n, m] = double.Parse(p[3], System.Globalization.CultureInfo.InvariantCulture);
                GDot[n, m] = double.Parse(p[4], System.Globalization.CultureInfo.InvariantCulture);
                HDot[n, m] = double.Parse(p[5], System.Globalization.CultureInfo.InvariantCulture);
            }
        }

        /// <summary>Decimal year of a date (2026.5 is about the 2nd of July 2026).</summary>
        public static double DecimalYear(DateTime date)
        {
            double days = DateTime.IsLeapYear(date.Year) ? 366 : 365;
            return date.Year + (date.DayOfYear - 1 + date.TimeOfDay.TotalDays) / days;
        }

        /// <summary>
        /// Declination (East positive) and inclination, in degrees, at a geodetic position (WGS84), height above the
        /// ellipsoid in km, and decimal year.
        /// </summary>
        public static (double Declination, double Inclination) Field(double latitude, double longitude, double heightKm, double year)
        {
            const double a = 6378.137;
            const double f = 1 / 298.257223563;
            const double referenceRadius = 6371.2;
            double e2 = f * (2 - f);
            double phi = latitude * Math.PI / 180;
            double lambda = longitude * Math.PI / 180;
            // Geodetic to geocentric spherical coordinates.
            double rc = a / Math.Sqrt(1 - e2 * Math.Sin(phi) * Math.Sin(phi));
            double p = (rc + heightKm) * Math.Cos(phi);
            double z = (rc * (1 - e2) + heightKm) * Math.Sin(phi);
            double r = Math.Sqrt(p * p + z * z);
            double phiGeocentric = Math.Asin(z / r);
            double s = Math.Sin(phiGeocentric);
            double c = Math.Cos(phiGeocentric);

            // Schmidt semi-normalised associated Legendre functions and their derivative with the latitude.
            double[,] pn = new double[Degree + 2, Degree + 2];
            double[,] dp = new double[Degree + 2, Degree + 2];
            pn[0, 0] = 1;
            for (int n = 1; n <= Degree; n++)
            {
                for (int m = 0; m <= n; m++)
                {
                    if (m == n)
                    {
                        double k = n == 1 ? 1.0 : Math.Sqrt((2.0 * n - 1) / (2.0 * n));
                        pn[n, n] = k * c * pn[n - 1, n - 1];
                        dp[n, n] = k * (c * dp[n - 1, n - 1] - s * pn[n - 1, n - 1]);
                    }
                    else if (m == n - 1)
                    {
                        double k = Math.Sqrt(2.0 * n - 1);
                        pn[n, m] = k * s * pn[n - 1, m];
                        dp[n, m] = k * (s * dp[n - 1, m] + c * pn[n - 1, m]);
                    }
                    else
                    {
                        double den = Math.Sqrt((double)n * n - (double)m * m);
                        double k2 = Math.Sqrt((n - 1.0) * (n - 1.0) - (double)m * m);
                        pn[n, m] = ((2.0 * n - 1) * s * pn[n - 1, m] - k2 * pn[n - 2, m]) / den;
                        dp[n, m] = ((2.0 * n - 1) * (s * dp[n - 1, m] + c * pn[n - 1, m]) - k2 * dp[n - 2, m]) / den;
                    }
                }
            }

            double dt = year - Epoch;
            double br = 0, bp = 0, bl = 0;
            for (int n = 1; n <= Degree; n++)
            {
                double rn = Math.Pow(referenceRadius / r, n + 2);
                for (int m = 0; m <= n; m++)
                {
                    double g = G[n, m] + dt * GDot[n, m];
                    double h = H[n, m] + dt * HDot[n, m];
                    double cm = Math.Cos(m * lambda);
                    double sm = Math.Sin(m * lambda);
                    br += rn * (n + 1) * (g * cm + h * sm) * pn[n, m];
                    bp -= rn * (g * cm + h * sm) * dp[n, m];
                    bl += rn * m * (g * sm - h * cm) * pn[n, m];
                }
            }
            bl /= c;
            // North, East, Down components in the geocentric frame, then rotated to the geodetic one.
            double xg = bp;
            double yg = bl;
            double zg = -br;
            double psi = phi - phiGeocentric;
            double x = xg * Math.Cos(psi) + zg * Math.Sin(psi);
            double zd = -xg * Math.Sin(psi) + zg * Math.Cos(psi);
            double declination = Math.Atan2(yg, x) * 180 / Math.PI;
            double inclination = Math.Atan2(zd, Math.Sqrt(x * x + yg * yg)) * 180 / Math.PI;
            return (declination, inclination);
        }
    }
}
