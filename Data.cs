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
        /// Half angle of the azimuth scan limits, in degrees.
        /// </summary>
        public const double AzimuthScanHalfAngle = 10;
        /// <summary>
        /// Angle of the upper elevation scan limit, in degrees.
        /// </summary>
        public double ElevationScanAngle => GlideSlope + 5;
        /// <summary>
        /// Vertical tolerance around the glide path, in degrees (each side).
        /// </summary>
        public double GlidePathTolerance { get; set; } = 0.5;
        /// <summary>
        /// Lateral tolerance around the extended centreline, in degrees (each side).
        /// </summary>
        public double CenterlineTolerance { get; set; } = 1.5;
        /// <summary>
        /// Height in ft above the threshold elevation of a line starting at the touchdown point (on the runway)
        /// with angle GlideSlope + <paramref name="angleOffset"/>, at <paramref name="distanceFromTouchdownNM"/>.
        /// With offset 0 this is the ideal glide path; with ±GlidePathTolerance the tolerance limits.
        /// </summary>
        public double GlidePathHeight(double distanceFromTouchdownNM, double angleOffset = 0)
        {
            return distanceFromTouchdownNM * Math.Tan((GlideSlope + angleOffset) * Math.PI / 180) * FeetPerNM;
        }
        /// <summary>
        /// Half width in NM of the centreline tolerance at <paramref name="distanceFromTouchdownNM"/>,
        /// for lines starting at the touchdown point.
        /// </summary>
        public double CenterlineHalfWidth(double distanceFromTouchdownNM)
        {
            return distanceFromTouchdownNM * Math.Tan(CenterlineTolerance * Math.PI / 180);
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
        /// <summary>
        /// True when the aircraft is inside the vertical tolerance, measured as angles from the touchdown point.
        /// </summary>
        public bool IsWithinGlidePathTolerance(Runway runway)
        {
            double s = DistanceFromTouchdown(runway);
            double height = Altitude - runway.Elevation;
            return height > runway.GlidePathHeight(s, -runway.GlidePathTolerance)
                && height < runway.GlidePathHeight(s, runway.GlidePathTolerance);
        }
        /// <summary>
        /// True when the aircraft is inside the lateral tolerance, measured as angles from the touchdown point.
        /// </summary>
        public bool IsWithinCenterlineTolerance(Runway runway)
        {
            return Math.Abs(LateralOffset(runway)) <= runway.CenterlineHalfWidth(DistanceFromTouchdown(runway));
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
        /// <summary>
        /// True when the aircraft is inside the scan limits drawn on screen, also over the runway after the threshold:
        /// between the far end of the runway and the end of the displayed range, inside the azimuth cone,
        /// above the threshold elevation and below the upper elevation scan limit.
        /// The cones start at the far end of the runway, as drawn.
        /// </summary>
        public bool IsDisplayed(Runway runway)
        {
            // Distance from the far end of the runway, along the centreline.
            double fromRunwayEnd = runway.LengthNM + AlongTrackDistance(runway);
            double coneLength = runway.LengthNM + runway.Distance;
            if (fromRunwayEnd < 0 || fromRunwayEnd > coneLength) return false;
            // Azimuth scan limits.
            double azimuthHalfWidth = fromRunwayEnd / coneLength * runway.Distance * Math.Tan(Runway.AzimuthScanHalfAngle * Math.PI / 180);
            if (Math.Abs(LateralOffset(runway)) > azimuthHalfWidth) return false;
            // Elevation scan limits: above the ground, below the upper limit.
            double height = Altitude - runway.Elevation;
            if (height <= 0) return false;
            if (height > fromRunwayEnd * Math.Tan(runway.ElevationScanAngle * Math.PI / 180) * Runway.FeetPerNM) return false;
            return true;
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
        public const string FormatComment = "# ICAO;DESIGNATOR;HEADING(deg true);THRESHOLD ELEVATION(ft);THRESHOLD LATITUDE;THRESHOLD LONGITUDE;LENGTH(m);WIDTH(m);GLIDE SLOPE(deg);TCH(ft);DH(ft);DEFAULT RANGE(NM)[;TOUCHDOWN FROM THRESHOLD(m)]";

        public static async Task<Runway[]> GetRunways(string path)
        {
            List<Runway> runways = [];
            string[] data = await System.IO.File.ReadAllLinesAsync(path);
            foreach (string line in data)
            {
                if (TryParseLine(line, out Runway? runway))
                {
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
                return true;
            }
            return false;
        }

        /// <summary>
        /// Writes the runway file. The previous file is kept as ".bak"; lines of the previous file that are not
        /// runways (comments) are kept at the top. Saved values are the file values (DefaultMDH, DefaultDistance),
        /// not the ones changed on the fly during the session.
        /// </summary>
        public static void SaveRunways(string path, IEnumerable<Runway> runways)
        {
            List<string> lines = [];
            if (System.IO.File.Exists(path))
            {
                foreach (string line in System.IO.File.ReadAllLines(path))
                {
                    if (!string.IsNullOrWhiteSpace(line) && !TryParseLine(line, out _))
                    {
                        lines.Add(line);
                    }
                }
                System.IO.File.Copy(path, path + ".bak", overwrite: true);
            }
            if (!lines.Any(l => l.TrimStart().StartsWith('#')))
            {
                lines.Insert(0, FormatComment);
            }
            foreach (Runway r in runways)
            {
                string line = string.Join(";",
                    r.ICAO, r.Designator, Format(r.Heading), Format(r.Elevation),
                    CoordinateParser.Format(r.Latitude), CoordinateParser.Format(r.Longitude),
                    Format(r.LengthM), Format(r.WidthM), Format(r.GlideSlope), Format(r.TCH),
                    Format(r.DefaultMDH), Format(r.DefaultDistance));
                if (r.TouchdownOverrideM is double touchdown)
                {
                    line += ";" + Format(touchdown);
                }
                lines.Add(line);
            }
            string temp = path + ".tmp";
            System.IO.File.WriteAllLines(temp, lines);
            System.IO.File.Move(temp, path, overwrite: true);
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
