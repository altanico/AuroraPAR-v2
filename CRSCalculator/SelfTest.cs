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

            // Check of a runways.par file: pairing of the opposite ends, heading and length, rewriting of the two fields only.
            Check("opposite of 16L", RunwayFile.Opposite("16L") == "34R" ? 1 : 0, 1, 0);
            Check("opposite of 09", RunwayFile.Opposite("09") == "27" ? 1 : 0, 1, 0);
            Check("opposite of 36", RunwayFile.Opposite("36") == "18" ? 1 : 0, 1, 0);
            Check("opposite of 18C", RunwayFile.Opposite("18C") == "36C" ? 1 : 0, 1, 0);
            List<string> file =
            [
                "# comment\r",
                "LIRF;16L;162.7;14;41.84592298;12.26152158;1000;60;3.0;56.04;200;20\r",
                "LIRF;16L 2.8;162.7;14;41.84592298;12.26152158;1000;60;2.8;56.04;200;20;310;3E\r",
                "LIRF;34R;342.7;6;41.81243791;12.27552181;1000;60;3.0;57.41;200;20\r",
                "LIRF;16R;162.7;14;41.80;12.25;1000;60;3.0;56.04;200;20\r"
            ];
            Check("lines of the file read", RunwayFile.Parse(file).Count, 4, 0);
            Check("runways of the file (approaches as one)", RunwayFile.Runways(RunwayFile.Parse(file)).Count, 3, 0);
            Check("16L has its opposite end", RunwayFile.OppositeOf(RunwayFile.Parse(file), RunwayFile.Parse(file)[0]) != null ? 1 : 0, 1, 0);
            Check("16R has no opposite end", RunwayFile.OppositeOf(RunwayFile.Parse(file), RunwayFile.Parse(file)[3]) == null ? 1 : 0, 1, 0);
            List<RunwayCheck> checks = RunwayFile.Analyze(file, out List<string> unpaired);
            Check("runways paired in the file", checks.Count, 2, 0);
            Check("runway without its opposite end listed", unpaired.Count, 1, 0);
            RunwayCheck first = checks.First(c => c.Name == "LIRF 16L");
            Check("file check: heading 16L", first.NewHeading, 162.69, 0.0051);
            Check("file check: length 16L", first.NewLength, 3897, 0.5);
            Check("file check: lines of 16L (two approaches)", first.Lines.Count, 2, 0);
            RunwayFile.Apply(file, [(first, true, true)]);
            Check("file rewritten: heading", file[1].Split(';')[2] == "162.69" ? 1 : 0, 1, 0);
            Check("file rewritten: length of the second approach", file[2].Split(';')[6] == "3897" ? 1 : 0, 1, 0);
            Check("file rewritten: other fields and line ending kept", file[2] == "LIRF;16L 2.8;162.69;14;41.84592298;12.26152158;3897;60;2.8;56.04;200;20;310;3E\r" ? 1 : 0, 1, 0);
            Check("file rewritten: other runway untouched", file[3] == "LIRF;34R;342.7;6;41.81243791;12.27552181;1000;60;3.0;57.41;200;20\r" ? 1 : 0, 1, 0);

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
