using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Shapes;

namespace AuroraPAR
{
    internal class Runway
    {
        public string ICAO { get; set; } = "ZZZZ";
        public string Designator { get; set; } = "00";
        /// <summary>
        /// Heading in deg.
        /// </summary>
        public double Heading { get; set; } = 0;
        /// <summary>
        /// Elevation in ft.
        /// </summary>
        public double Elevation { get; set; } = 0;
        /// <summary>
        /// Plus is north.
        /// </summary>
        public double Latitude { get; set; }
        /// <summary>
        /// Plus is east.
        /// </summary>
        public double Longitude { get; set; }
        private double _length = 0;
        /// <summary>
        /// Length in meters.
        /// </summary>
        public double LengthM { get { return _length; } set { _length = value; } }
        /// <summary>
        /// Length in nautical miles.
        /// </summary>
        public double LengthNM { get { return LengthM / 1852; } set { _length = value * 1852; } }
        /// <summary>
        /// Glide slope angle in degrees.
        /// </summary>
        public double GlideSlope { get; set; } = 3.0;
        /// <summary>
        /// Threshold crossing height.
        /// </summary>
        public double TCH { get; set; } = 50;
        /// <summary>
        /// Minimum descend height.
        /// </summary>
        public double MDH { get; set; } = 200;
        /// <summary>
        /// Decision height from the runway file. <see cref="MDH"/> can be changed on the fly during the session
        /// (never saved) and is reset to this value when the runway is selected again.
        /// </summary>
        public double DefaultMDH { get; set; } = 200;
        /// <summary>
        /// Length of the decision height line from the touchdown point, in NM.
        /// </summary>
        public const double DecisionHeightLineLength = 3;
        /// <summary>
        /// Distance from the runway to be displayed in NM.
        /// </summary>
        public double Distance { get; set; } = 10.0;
        /// <summary>
        /// Default display range of this runway in NM, as read from the runway file (not changed by zooming).
        /// </summary>
        public double DefaultDistance { get; set; } = 10.0;
        private double _width = 0;
        /// <summary>
        /// Width in meters.
        /// </summary>
        public double WidthM { get { return _width; } set { _width = value; } }
        /// <summary>
        /// Width in nautical miles.
        /// </summary>
        public double WidthNM { get { return WidthM / 1852; } set { _width = value * 1852; } }
        /// <summary>
        /// Distance of the touchdown point beyond the threshold, in meters, when given in the runway file.
        /// When null it is calculated as the point where the glide path reaches the runway.
        /// </summary>
        public double? TouchdownOverrideM { get; set; }
        /// <summary>
        /// Magnetic variation of this runway (degrees, East positive), when given in the runway file.
        /// When null the default of the profile is used (see <see cref="Profile.MagneticVariation"/>).
        /// </summary>
        public double? MagneticVariation { get; set; }

        /// <summary>
        /// Magnetic final course: true heading corrected with the variation of the runway, or the given default.
        /// </summary>
        public int FinalCourse(double defaultVariation)
        {
            return AuroraPAR.MagneticVariation.FinalCourse(Heading, MagneticVariation ?? defaultVariation);
        }
        /// <summary>
        /// Line number in the runway file this runway was read from (null for a new runway).
        /// </summary>
        public int? SourceLine { get; set; }
        /// <summary>
        /// Original text of the line; when saving, an unchanged runway is written back exactly as it was.
        /// </summary>
        public string? SourceText { get; set; }
        /// <summary>
        /// For a new runway: line of the file after which it is saved (e.g. right after the runway it was copied from).
        /// </summary>
        public int? InsertAfterLine { get; set; }
        /// <summary>
        /// Distance of the touchdown point beyond the threshold, in meters.
        /// Default: where the glide path, crossing the threshold at TCH, reaches the runway (TCH / tan(glide slope)).
        /// </summary>
        public double TouchdownM
        {
            get
            {
                if (TouchdownOverrideM is double m) return m;
                if (GlideSlope <= 0) return 0;
                return TCH * 0.3048 / Math.Tan(GlideSlope * Math.PI / 180);
            }
        }
        /// <summary>
        /// Distance of the touchdown point beyond the threshold, in nautical miles.
        /// </summary>
        public double TouchdownNM => TouchdownM / 1852;
        public const double FeetPerNM = 6076.11549;
        /// <summary>
        /// Height in ft above the threshold elevation of a line starting at the touchdown point (on the runway)
        /// with angle GlideSlope + <paramref name="angleOffset"/>, at <paramref name="distanceFromTouchdownNM"/>.
        /// With offset 0 this is the ideal glide path; with the approach limits (see <see cref="Radar"/>) their lines.
        /// </summary>
        public double GlidePathHeight(double distanceFromTouchdownNM, double angleOffset = 0)
        {
            return distanceFromTouchdownNM * Math.Tan((GlideSlope + angleOffset) * Math.PI / 180) * FeetPerNM;
        }
        /// <summary>
        /// Distance from the touchdown point, in NM, where the glide path reaches the MDH (missed approach point).
        /// </summary>
        public double MissedApproachPointNM
        {
            get
            {
                double t = Math.Tan(GlideSlope * Math.PI / 180);
                return t > 0 ? MDH / (t * FeetPerNM) : 0;
            }
        }

        /// <summary>
        /// True for an approach with a glide path angle typed by the controller (modern display), not one of the
        /// lines of the runway file: "unpublished approach".
        /// </summary>
        public bool IsUnpublished { get; private set; }

        /// <summary>
        /// Copy of this approach with another glide path angle, marked as unpublished. Everything else (DH, range,
        /// touchdown, magnetic variation) is the same; the touchdown point follows the angle when it is not given
        /// in the file.
        /// </summary>
        public Runway WithGlideSlope(double glideSlope)
        {
            Runway copy = (Runway)MemberwiseClone();
            copy.GlideSlope = glideSlope;
            copy.IsUnpublished = true;
            copy.SourceLine = null;
            copy.SourceText = null;
            copy.InsertAfterLine = null;
            return copy;
        }

        /// <summary>Glide path angle as written on the screen (e.g. 3.0, 2.75).</summary>
        public static string FormatGlideSlope(double degrees)
        {
            return degrees.ToString("0.0#", CultureInfo.InvariantCulture);
        }

        /// <summary>Missed approach point distance as written on the screen (e.g. 0.98), "----" when not known.</summary>
        public string MissedApproachPointText()
        {
            double nm = MissedApproachPointNM;
            if (!(nm > 0) || double.IsInfinity(nm)) return "----";
            return nm.ToString(nm < 10 ? "0.00" : "0.0", CultureInfo.InvariantCulture);
        }

        public override string ToString()
        {
            return $"{ICAO} {Designator}";
        }

    }
    internal class Aircraft
    {
        public string Callsign { get; set; } = "ABC1234";
        public double Latitude { get; set; }
        public double Longitude { get; set; }
        public double Altitude { get; set; } = 0;
        public double Track { get; set; }
        public double Speed { get; set; }
        /// <summary>
        /// Vertical speed in ft/min (negative descending), estimated from the altitude history; null when not known yet.
        /// </summary>
        public double? VerticalSpeedFpm { get; set; }
        /// <summary>
        /// Distance to runway.
        /// </summary>
        /// <param name="runway">Runway.</param>
        /// <returns>Distance in NM.</returns>
        public double Distance(Runway runway)
        {
            double lat1Rad = Latitude*double.Pi/180;
            double lon1Rad = Longitude * double.Pi / 180;
            double lat2Rad = runway.Latitude * double.Pi / 180;
            double lon2Rad = runway.Longitude*double.Pi/180;
            double dLat = lat2Rad - lat1Rad;
            double dLon = lon2Rad - lon1Rad;
            // Haversine formula
            double a = Math.Sin(dLat / 2) * Math.Sin(dLat / 2) +
                       Math.Cos(lat1Rad) * Math.Cos(lat2Rad) *
                       Math.Sin(dLon / 2) * Math.Sin(dLon / 2);
            double c = 2 * Math.Atan2(Math.Sqrt(a), Math.Sqrt(1 - a));
            return (c * 6371 * 1000 / 1852);
        }
        /// <summary>
        /// Distance from the threshold measured along the extended runway centreline (ignoring the lateral offset), in NM.
        /// </summary>
        public double AlongTrackDistance(Runway runway)
        {
            double d = Distance(runway);
            double xt = LateralOffset(runway);
            double along = Math.Sqrt(Math.Max(0, d * d - xt * xt));
            // Positive on the approach side of the threshold, negative once the aircraft has passed it
            // (over the runway): the approach direction is the opposite of the runway heading.
            double approachBearing = (runway.Heading + 180) % 360;
            bool pastThreshold = Math.Cos((BearingFromRunway(runway) - approachBearing) * Math.PI / 180) < 0;
            return pastThreshold ? -along : along;
        }
        /// <summary>
        /// Distance from the touchdown point measured along the extended runway centreline, in NM.
        /// This is the distance controllers give on final ("4 miles from touchdown").
        /// </summary>
        public double DistanceFromTouchdown(Runway runway)
        {
            return AlongTrackDistance(runway) + runway.TouchdownNM;
        }
        public double LateralOffset(Runway runway)
        {
            // Calculate initial bearing from runway to aircraft
            double bearingToAircraft = BearingFromRunway(runway);

            double R = 6371e3; // Earth radius in meters

            // Angular distance from runway to aircraft (radians)
            double delta13 = Distance(runway) * 1852 / R; // Distance(runway) returns NM, convert to meters then to radians

            // Bearings in radians
            double theta13 = bearingToAircraft * Math.PI / 180.0; // runway -> aircraft
            double theta12 = runway.Heading * Math.PI / 180.0;     // runway heading

            // Cross-track distance formula (meters)
            double xt = Math.Asin(Math.Sin(delta13) * Math.Sin(theta13 - theta12)) * R;

            // Convert meters back to NM
            double lateralOffsetNM = xt / 1852.0;

            return lateralOffsetNM;
        }
        public double BearingFromRunway(Runway runway)
        {
            double lat1Rad = runway.Latitude * Math.PI / 180;
            double lon1Rad = runway.Longitude * Math.PI / 180;
            double lat2Rad = Latitude * Math.PI / 180;
            double lon2Rad = Longitude * Math.PI / 180;

            double dLon = lon2Rad - lon1Rad;
            double y = Math.Sin(dLon) * Math.Cos(lat2Rad);
            double x = Math.Cos(lat1Rad) * Math.Sin(lat2Rad) -
                       Math.Sin(lat1Rad) * Math.Cos(lat2Rad) * Math.Cos(dLon);
            double bearingToAircraft = (Math.Atan2(y, x) * 180 / Math.PI + 360) % 360;
            return bearingToAircraft;
        }
    }
    internal struct Distance(double distance)
    {
        double distance = distance;
        public static implicit operator double(Distance d) => d.distance;
        public static implicit operator Distance(int distance) => new Distance(distance);
        public static implicit operator Distance(double distance) => new Distance(distance);
        public override string ToString()
        {
            return $"{distance} nm";
        }
    }

    internal class DataFile
    {
        //Format: ICAO;DESIGNATOR;HEADING;ELEVATION;LATITUDE;LONGITUDE;LENGTH IN METERS;WIDTH IN METERS;GLIDE SLOPE;TCH;MDH;DEFAULT DISTANCE[;TOUCHDOWN DISTANCE FROM THRESHOLD IN METERS (optional)]
        public const string FormatComment = "# ICAO;DESIGNATOR;HEADING(deg true);THRESHOLD ELEVATION(ft);THRESHOLD LATITUDE;THRESHOLD LONGITUDE;LENGTH(m);WIDTH(m);GLIDE SLOPE(deg);TCH(ft);DH(ft);DEFAULT RANGE(NM)[;TOUCHDOWN FROM THRESHOLD(m)][;MAGNETIC VARIATION e.g. 3E or 2W]";

        public static async Task<Runway[]> GetRunways(string path)
        {
            return Parse(await System.IO.File.ReadAllLinesAsync(path));
        }

        public static Runway[] ReadRunways(string path)
        {
            return Parse(System.IO.File.ReadAllLines(path));
        }

        private static Runway[] Parse(string[] lines)
        {
            List<Runway> runways = [];
            for (int i = 0; i < lines.Length; i++)
            {
                if (TryParseLine(lines[i], out Runway? runway))
                {
                    runway.SourceLine = i;
                    runway.SourceText = lines[i];
                    runways.Add(runway);
                }
            }
            return runways.ToArray();
        }

        /// <summary>
        /// Reads one line of the runway file. Lines that are incomplete or contain invalid numbers
        /// (e.g. comments starting with #, empty lines) are not runways and return false.
        /// </summary>
        public static bool TryParseLine(string line, [System.Diagnostics.CodeAnalysis.NotNullWhen(true)] out Runway? runway)
        {
            runway = null;
            string[] linedata = line.Split(';', StringSplitOptions.TrimEntries);
            // 12 fields are needed (indexes 0 to 11).
            if (linedata.Length >= 12
                && TryParse(linedata[2], out double heading)
                && TryParse(linedata[3], out double elevation)
                && TryParse(linedata[4], out double latitude)
                && TryParse(linedata[5], out double longitude)
                && TryParse(linedata[6], out double length)
                && TryParse(linedata[7], out double width)
                && TryParse(linedata[8], out double glideSlope)
                && TryParse(linedata[9], out double tch)
                && TryParse(linedata[10], out double mdh)
                && TryParse(linedata[11], out double distance))
            {
                runway = new()
                {
                    ICAO = linedata[0],
                    Designator = linedata[1],
                    Heading = heading,
                    Elevation = elevation,
                    Latitude = latitude,
                    Longitude = longitude,
                    LengthM = length,
                    WidthM = width,
                    GlideSlope = glideSlope,
                    TCH = tch,
                    MDH = mdh,
                    DefaultMDH = mdh,
                    Distance = distance,
                    DefaultDistance = distance
                };
                // Optional 13th field: touchdown point distance beyond the threshold, in meters.
                if (linedata.Length >= 13 && TryParse(linedata[12], out double touchdown))
                {
                    runway.TouchdownOverrideM = touchdown;
                }
                // Optional 14th field: magnetic variation (e.g. 3E, 2W). The 13th can then be empty.
                if (linedata.Length >= 14 && AuroraPAR.MagneticVariation.TryParse(linedata[13], out double variation))
                {
                    runway.MagneticVariation = variation;
                }
                return true;
            }
            return false;
        }

        /// <summary>
        /// Writes the runway file keeping its layout: every line that is not a runway (comments, separators)
        /// stays where it was; runways read from the file are written at their original line (exactly as they were
        /// if unchanged, see <see cref="Runway.SourceText"/>); deleted runways are removed; new runways go right after
        /// <see cref="Runway.InsertAfterLine"/>, or at the end. The previous file is kept as ".bak".
        /// Saved values are the file values (DefaultMDH, DefaultDistance), not the ones changed on the fly.
        /// </summary>
        public static void SaveRunways(string path, IReadOnlyList<Runway> runways)
        {
            string[] original = System.IO.File.Exists(path) ? System.IO.File.ReadAllLines(path) : [];
            Dictionary<int, Runway> bySource = runways.Where(r => r.SourceLine != null).ToDictionary(r => r.SourceLine!.Value);
            ILookup<int, Runway> inserted = runways.Where(r => r.SourceLine == null && r.InsertAfterLine != null).ToLookup(r => r.InsertAfterLine!.Value);
            List<string> lines = [];
            if (original.Length == 0)
            {
                lines.Add(FormatComment);
            }
            for (int i = 0; i < original.Length; i++)
            {
                if (TryParseLine(original[i], out _))
                {
                    if (bySource.TryGetValue(i, out Runway? runway))
                    {
                        lines.Add(ToLine(runway));
                    }
                }
                else
                {
                    lines.Add(original[i]);
                }
                foreach (Runway runway in inserted[i])
                {
                    lines.Add(ToLine(runway));
                }
            }
            foreach (Runway runway in runways.Where(r => r.SourceLine == null && (r.InsertAfterLine == null || r.InsertAfterLine >= original.Length)))
            {
                lines.Add(ToLine(runway));
            }
            if (System.IO.File.Exists(path))
            {
                System.IO.File.Copy(path, path + ".bak", overwrite: true);
            }
            string temp = path + ".tmp";
            System.IO.File.WriteAllLines(temp, lines);
            System.IO.File.Move(temp, path, overwrite: true);
        }

        private static string ToLine(Runway r)
        {
            if (r.SourceText != null)
            {
                return r.SourceText;
            }
            string line = string.Join(";",
                r.ICAO, r.Designator, Format(r.Heading), Format(r.Elevation),
                CoordinateParser.Format(r.Latitude), CoordinateParser.Format(r.Longitude),
                Format(r.LengthM), Format(r.WidthM), Format(r.GlideSlope), Format(r.TCH),
                Format(r.DefaultMDH), Format(r.DefaultDistance));
            if (r.TouchdownOverrideM != null || r.MagneticVariation != null)
            {
                line += ";" + (r.TouchdownOverrideM is double touchdown ? Format(touchdown) : "");
            }
            if (r.MagneticVariation is double variation)
            {
                line += ";" + AuroraPAR.MagneticVariation.Format(variation);
            }
            return line;
        }

        private static string Format(double value)
        {
            return value.ToString("0.###", CultureInfo.InvariantCulture);
        }

        /// <summary>
        /// Numbers in the file always use a dot as decimal separator, whatever the Windows language.
        /// </summary>
        private static bool TryParse(string text, out double value)
        {
            return double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out value);
        }
    }
}
