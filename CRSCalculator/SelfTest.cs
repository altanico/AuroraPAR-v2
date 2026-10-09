using System.Globalization;
using System.IO;
using System.Text;

namespace AuroraPAR
{
    /// <summary>
    /// Check of the calculations (run by the build on GitHub with <c>CRSCalculator.exe --selftest file</c>, never by the user):
    /// the magnetic model against rows of the official WMM2025 test values, and the geometry against known results.
    /// </summary>
    internal static class SelfTest
    {
        public static bool Run(string? reportPath)
        {
            StringBuilder report = new();
            int failures = 0;

            void Check(string name, double value, double expected, double tolerance)
            {
                bool ok = Math.Abs(value - expected) <= tolerance;
                if (!ok) failures++;
                report.AppendLine($"{(ok ? "ok  " : "FAIL")} {name}: {value.ToString("0.0000", CultureInfo.InvariantCulture)} (expected {expected.ToString("0.0000", CultureInfo.InvariantCulture)} +/- {tolerance.ToString(CultureInfo.InvariantCulture)})");
            }

            // year, altitude km, latitude, longitude, declination, inclination (official test values of WMM2025)
            (double Year, double Altitude, double Latitude, double Longitude, double Declination, double Inclination)[] model =
            [
                (2025.0, 28, 89, -121, -99.77, 88.47),
                (2025.0, 65, 43, 93, 0.50, 64.10),
                (2025.0, 51, -33, 109, -5.49, -67.50),
                (2025.0, 3, -50, -103, 27.96, -54.89),
                (2025.0, 18, 0, 21, 1.29, -26.06),
                (2025.5, 8, -52, -75, 14.91, -49.63),
                (2026.0, 69, 23, 63, 1.17, 35.92),
                (2026.5, 14, 0, 80, -3.10, -17.15),
                (2027.0, 67, 72, -115, 13.73, 84.84),
                (2027.5, 73, -72, 95, -102.64, -76.49),
                (2028.5, 28, 54, -120, 15.43, 73.74),
                (2029.0, 38, -76, 49, -64.28, -67.36)
            ];
            foreach ((double year, double altitude, double latitude, double longitude, double declination, double inclination) in model)
            {
                (double d, double i) = WorldMagneticModel.Field(latitude, longitude, altitude, year);
                Check($"WMM declination {year} {latitude},{longitude}", d, declination, 0.02);
                Check($"WMM inclination {year} {latitude},{longitude}", i, inclination, 0.02);
            }

            // Geometry: thresholds of LIBN (runways.par), of Sydney and of Tromso.
            (double Lat1, double Lon1, double Lat2, double Lon2, double Distance, double Heading, double Spherical)[] pairs =
            [
                (40.23227088, 18.13907997, 40.24588608, 18.12695816, 1830.2, 325.6945, 325.8028),
                (-33.9, 151.17, -33.92, 151.19, 2888.35, 140.1798, 140.3158),
                (69.68, 18.9, 69.69, 18.95, 2236.25, 60.0752, 60.0317)
            ];
            foreach ((double lat1, double lon1, double lat2, double lon2, double distance, double heading, double spherical) in pairs)
            {
                (double s, double a1, double a2) = Geodesic.Inverse(lat1, lon1, lat2, lon2);
                Check($"distance {lat1},{lon1}", s, distance, 0.2);
                Check($"true heading {lat1},{lon1}", Geodesic.MeanAzimuth(a1, a2), heading, 0.005);
                Check($"spherical bearing {lat1},{lon1}", Geodesic.SphericalBearing(lat1, lon1, lat2, lon2), spherical, 0.005);
            }

            // Reading the coordinates and their precision.
            bool pair = CoordinateParser.TryParsePair("N040 14.5 E018 07.9", out double lat, out double lon, out string latText, out string lonText);
            Check("coordinate pair read", pair ? 1 : 0, 1, 0);
            Check("latitude of the pair", lat, 40 + 14.5 / 60, 0.00001);
            Check("longitude of the pair", lon, 18 + 7.9 / 60, 0.00001);
            Check("precision (minutes, 1 decimal) in degrees", CoordinatePrecision.MaxErrorDegrees(latText, true), 1.0 / 60 / 10 / 2, 1e-9);
            Check("precision (decimal degrees, 6 decimals)", CoordinatePrecision.MaxErrorDegrees("40.232271", true), 5e-7, 1e-12);
            Check("precision (seconds, 2 decimals)", CoordinatePrecision.MaxErrorDegrees("40°13'57.74\"N", true), 1.0 / 3600 / 100 / 2, 1e-12);
            Check("precision (compact AIP, seconds)", CoordinatePrecision.MaxErrorDegrees("401357N", true), 1.0 / 3600 / 2, 1e-12);

            report.AppendLine(failures == 0 ? "ALL CHECKS PASSED" : $"{failures} CHECKS FAILED");
            try
            {
                if (!string.IsNullOrEmpty(reportPath)) File.WriteAllText(reportPath, report.ToString());
            }
            catch (Exception)
            {
            }
            return failures == 0;
        }
    }
}
