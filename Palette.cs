using System.Windows.Media;

namespace AuroraPAR
{
    /// <summary>Display mode: the modern synthetic display or the old analog scope.</summary>
    internal enum DisplayMode
    {
        Modern,
        Analog
    }

    /// <summary>
    /// Colours of the elements of the views. <see cref="Modern"/> is the usual display; <see cref="Analog"/> is the
    /// monochrome phosphor of an old radar scope (only brightness changes).
    /// </summary>
    internal sealed class Palette
    {
        public required Brush Runway { get; init; }
        public required Brush Ground { get; init; }
        public required Brush ScanLimit { get; init; }
        public required Brush GlidePath { get; init; }
        public required Brush ApproachLimit { get; init; }
        public required Brush DecisionHeight { get; init; }
        public required Brush Touchdown { get; init; }
        public required Brush RangeMark { get; init; }
        public required Brush RangeText { get; init; }
        public required Brush ScaleText { get; init; }
        public required Brush TrackIn { get; init; }
        public required Brush TrackOut { get; init; }
        public required Brush Sweep { get; init; }

        public static readonly Palette Modern = new()
        {
            Runway = Brushes.Green,
            Ground = Brushes.Green,
            ScanLimit = Brushes.CadetBlue,
            GlidePath = Brushes.Yellow,
            ApproachLimit = Brushes.Red,
            DecisionHeight = Brushes.Red,
            Touchdown = Brushes.Yellow,
            RangeMark = Brushes.Green,
            RangeText = Brushes.Yellow,
            ScaleText = Brushes.Gray,
            TrackIn = Brushes.Green,
            TrackOut = Brushes.Red,
            Sweep = Frozen(Color.FromRgb(0x70, 0xF0, 0xE0))
        };

        /// <summary>Colour of the phosphor of the analog scope (yellow-green, as on the old PAR screens).</summary>
        public static readonly Color Phosphor = Color.FromRgb(0xA8, 0xFF, 0x60);

        public static readonly Palette Analog = new()
        {
            Runway = Phosphor0(0.95),
            Ground = Phosphor0(0.6),
            ScanLimit = Phosphor0(0.55),
            GlidePath = Phosphor0(0.9),
            ApproachLimit = Phosphor0(0.45),
            DecisionHeight = Phosphor0(0.6),
            Touchdown = Phosphor0(0.9),
            RangeMark = Phosphor0(0.5),
            RangeText = Phosphor0(0.55),
            ScaleText = Phosphor0(0.5),
            TrackIn = Phosphor0(1),
            TrackOut = Phosphor0(1),
            Sweep = Frozen(Color.FromRgb(0xD8, 0xFF, 0xB0))
        };

        private static Brush Phosphor0(double brightness)
        {
            return Frozen(Color.FromArgb((byte)Math.Round(255 * brightness), Phosphor.R, Phosphor.G, Phosphor.B));
        }

        private static Brush Frozen(Color color)
        {
            SolidColorBrush brush = new(color);
            brush.Freeze();
            return brush;
        }
    }
}
