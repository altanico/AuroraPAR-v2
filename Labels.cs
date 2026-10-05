using System.Globalization;

namespace AuroraPAR
{
    /// <summary>
    /// Values that can be shown in a track label.
    /// </summary>
    internal enum LabelField
    {
        None,
        Callsign,
        DistanceFromTouchdown,
        /// <summary>Altitude (A) with QNH, height above the threshold (H) with QFE.</summary>
        Altitude,
        GroundSpeed,
        VerticalSpeed,
        /// <summary>Above (U) or below (D) the ideal glide path.</summary>
        GlidePathDeviation,
        /// <summary>Left (L) or right (R) of the extended centreline, as seen by the pilot.</summary>
        CenterlineDeviation
    }

    /// <summary>
    /// Layout of a track label: a grid of rows and columns, each cell showing one field (or nothing).
    /// </summary>
    internal class LabelLayout
    {
        public const int MaxRows = 6;
        public const int MaxColumns = 3;

        public int Rows { get; set; } = 1;
        public int Columns { get; set; } = 1;
        /// <summary>Cells row by row (Rows × Columns).</summary>
        public List<LabelField> Cells { get; set; } = [];

        public LabelField Get(int row, int column) => Cells[row * Columns + column];

        public void Set(int row, int column, LabelField field) => Cells[row * Columns + column] = field;

        /// <summary>
        /// True when no cell shows anything: only the track symbol is drawn.
        /// </summary>
        public bool IsEmpty => Cells.All(c => c == LabelField.None);

        /// <summary>
        /// Changes the number of rows and columns, keeping the fields that still fit.
        /// </summary>
        public void Resize(int rows, int columns)
        {
            rows = Math.Clamp(rows, 1, MaxRows);
            columns = Math.Clamp(columns, 1, MaxColumns);
            List<LabelField> cells = [];
            for (int r = 0; r < rows; r++)
            {
                for (int c = 0; c < columns; c++)
                {
                    cells.Add(r < Rows && c < Columns && r * Columns + c < Cells.Count ? Get(r, c) : LabelField.None);
                }
            }
            Rows = rows;
            Columns = columns;
            Cells = cells;
        }

        /// <summary>
        /// Repairs a layout read from a file (wrong sizes, missing cells).
        /// </summary>
        public void Normalize()
        {
            Cells ??= [];
            int rows = Math.Clamp(Rows, 1, MaxRows);
            int columns = Math.Clamp(Columns, 1, MaxColumns);
            List<LabelField> cells = [];
            for (int i = 0; i < rows * columns; i++)
            {
                cells.Add(i < Cells.Count && Enum.IsDefined(Cells[i]) ? Cells[i] : LabelField.None);
            }
            Rows = rows;
            Columns = columns;
            Cells = cells;
        }

        public static LabelLayout Create(int rows, int columns, params LabelField[] cells)
        {
            LabelLayout layout = new() { Rows = rows, Columns = columns, Cells = [.. cells] };
            layout.Normalize();
            return layout;
        }

        /// <summary>
        /// Default elevation label: deviation from the glide path and callsign, distance, altitude, speed, vertical speed.
        /// </summary>
        public static LabelLayout DefaultElevation() => Create(5, 2,
            LabelField.GlidePathDeviation, LabelField.Callsign,
            LabelField.DistanceFromTouchdown, LabelField.None,
            LabelField.Altitude, LabelField.None,
            LabelField.GroundSpeed, LabelField.None,
            LabelField.VerticalSpeed, LabelField.None);

        /// <summary>
        /// Default azimuth label: deviation from the centreline and callsign, distance, speed.
        /// </summary>
        public static LabelLayout DefaultAzimuth() => Create(3, 2,
            LabelField.CenterlineDeviation, LabelField.Callsign,
            LabelField.DistanceFromTouchdown, LabelField.None,
            LabelField.GroundSpeed, LabelField.None);
    }

    /// <summary>
    /// Text of each label field.
    /// </summary>
    internal static class LabelFormatter
    {
        private const double MetresPerFoot = 0.3048;

        public static string DisplayName(LabelField field) => field switch
        {
            LabelField.None => "(empty)",
            LabelField.Callsign => "Callsign",
            LabelField.DistanceFromTouchdown => "Distance from touchdown",
            LabelField.Altitude => "Altitude / height",
            LabelField.GroundSpeed => "Ground speed",
            LabelField.VerticalSpeed => "Vertical speed",
            LabelField.GlidePathDeviation => "Deviation from glide path",
            LabelField.CenterlineDeviation => "Deviation from centreline",
            _ => field.ToString()
        };

        private static string Number(double value, string format) => value.ToString(format, CultureInfo.InvariantCulture);

        private static string Length(double feet, bool metres) =>
            metres ? $"{Number(feet * MetresPerFoot, "0")} m" : $"{Number(feet, "0")} ft";

        /// <summary>
        /// Text of a field for an aircraft. Heights, deviations and vertical speed in feet (ft, ft/min) or metres (m, m/s).
        /// </summary>
        public static string Format(LabelField field, Aircraft aircraft, Runway runway, ViewOptions options)
        {
            switch (field)
            {
                case LabelField.Callsign:
                    return aircraft.Callsign;
                case LabelField.DistanceFromTouchdown:
                    return $"{Number(aircraft.DistanceFromTouchdown(runway), "0.0")} NM";
                case LabelField.Altitude:
                    return options.Qfe
                        ? $"H {Length(aircraft.Altitude - runway.Elevation, options.ScaleInMetres)}"
                        : $"A {Length(aircraft.Altitude, options.ScaleInMetres)}";
                case LabelField.GroundSpeed:
                    return $"{Number(aircraft.Speed, "0")} Kts";
                case LabelField.VerticalSpeed:
                    if (aircraft.VerticalSpeedFpm is not double fpm) return "";
                    return options.ScaleInMetres
                        ? $"{Number(fpm * MetresPerFoot / 60, "+0.0;-0.0;0.0")} m/s"
                        : $"{Number(Math.Round(fpm / 10) * 10, "+0;-0;0")} ft/min";
                case LabelField.GlidePathDeviation:
                    {
                        double deviation = aircraft.Altitude - runway.Elevation - runway.GlidePathHeight(aircraft.DistanceFromTouchdown(runway));
                        return $"{(deviation >= 0 ? "U" : "D")} {Length(Math.Abs(deviation), options.ScaleInMetres)}";
                    }
                case LabelField.CenterlineDeviation:
                    {
                        double feet = aircraft.LateralOffset(runway) * Runway.FeetPerNM;
                        return $"{(feet >= 0 ? "R" : "L")} {Length(Math.Abs(feet), options.ScaleInMetres)}";
                    }
                default:
                    return "";
            }
        }
    }

    /// <summary>
    /// Vertical speed of each aircraft, from the change of altitude over the last seconds (least squares,
    /// so the steps of the altitude received from Aurora are smoothed). Unknown for the first seconds of a track.
    /// </summary>
    internal class VerticalSpeedEstimator
    {
        private static readonly TimeSpan Window = TimeSpan.FromSeconds(4);
        private const double MinSpanSeconds = 2;
        private readonly Dictionary<string, Queue<(DateTime Time, double Altitude)>> samples = [];

        public void Update(IReadOnlyList<Aircraft> aircrafts, DateTime now)
        {
            HashSet<string> present = [];
            foreach (Aircraft aircraft in aircrafts)
            {
                present.Add(aircraft.Callsign);
                if (!samples.TryGetValue(aircraft.Callsign, out Queue<(DateTime Time, double Altitude)>? queue))
                {
                    queue = new();
                    samples[aircraft.Callsign] = queue;
                }
                queue.Enqueue((now, aircraft.Altitude));
                while (queue.Count > 0 && now - queue.Peek().Time > Window)
                {
                    queue.Dequeue();
                }
                aircraft.VerticalSpeedFpm = Estimate(queue);
            }
            foreach (string callsign in samples.Keys.Where(c => !present.Contains(c)).ToList())
            {
                samples.Remove(callsign);
            }
        }

        private static double? Estimate(Queue<(DateTime Time, double Altitude)> queue)
        {
            if (queue.Count < 2) return null;
            DateTime start = queue.Peek().Time;
            double n = 0, sumT = 0, sumA = 0, sumTT = 0, sumTA = 0, last = 0;
            foreach ((DateTime time, double altitude) in queue)
            {
                double t = (time - start).TotalSeconds;
                n++;
                sumT += t;
                sumA += altitude;
                sumTT += t * t;
                sumTA += t * altitude;
                last = t;
            }
            if (last < MinSpanSeconds) return null;
            double denominator = n * sumTT - sumT * sumT;
            if (denominator <= 0) return null;
            double feetPerSecond = (n * sumTA - sumT * sumA) / denominator;
            return feetPerSecond * 60;
        }
    }
}
