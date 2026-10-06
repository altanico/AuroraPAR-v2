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
    /// Brush, width and dash style of every element of the views. <see cref="Modern"/> comes from the user's
    /// settings (<see cref="DisplayStyleSettings"/>); <see cref="Analog"/> is the fixed theme of an old radar
    /// scope: one phosphor colour, only the brightness changes from element to element.
    /// </summary>
    internal sealed class Theme
    {
        /// <summary>Default phosphor of the analog scope (yellow-green, as on the European PAR screens).</summary>
        public static readonly Color DefaultPhosphor = Color.FromRgb(0xA8, 0xFF, 0x60);

        private readonly Dictionary<StyleElement, (Brush Brush, double Width, LineDash Dash)> elements = [];

        public bool IsAnalog { get; private init; }
        /// <summary>Beam of the antenna scan effect.</summary>
        public Brush Sweep { get; private init; } = Brushes.White;
        /// <summary>Colour of the glow of the analog scope.</summary>
        public Color Glow { get; private init; } = DefaultPhosphor;

        public Brush Brush(StyleElement element) => elements[element].Brush;
        public double Width(StyleElement element) => elements[element].Width;
        public LineDash Dash(StyleElement element) => elements[element].Dash;

        public static Theme Modern(DisplayStyleSettings settings)
        {
            Theme theme = new() { Sweep = Frozen(Color.FromRgb(0x70, 0xF0, 0xE0)) };
            foreach (StyleElement element in Enum.GetValues<StyleElement>())
            {
                LineStyle style = settings.Get(element);
                Color color = ColorText.Parse(style.Color, ColorText.Parse(DisplayStyleSettings.Default(element).Color, System.Windows.Media.Colors.White));
                theme.elements[element] = (Frozen(color), style.Width, style.Dash);
            }
            return theme;
        }

        public static Theme Analog(Color phosphor)
        {
            Theme theme = new()
            {
                IsAnalog = true,
                Glow = phosphor,
                Sweep = Frozen(Mix(phosphor, System.Windows.Media.Colors.White, 0.55))
            };
            foreach (StyleElement element in Enum.GetValues<StyleElement>())
            {
                LineStyle defaults = DisplayStyleSettings.Default(element);
                Brush brush = element == StyleElement.Background
                    ? Brushes.Transparent
                    : Frozen(Color.FromArgb((byte)Math.Round(255 * Brightness(element)), phosphor.R, phosphor.G, phosphor.B));
                theme.elements[element] = (brush, defaults.Width, defaults.Dash);
            }
            return theme;
        }

        /// <summary>Brightness of each element on the analog scope.</summary>
        private static double Brightness(StyleElement element) => element switch
        {
            StyleElement.Runway => 0.95,
            StyleElement.GlidePath or StyleElement.Centerline or StyleElement.Touchdown => 0.9,
            StyleElement.Ground or StyleElement.DecisionHeight => 0.6,
            StyleElement.ScanLimits or StyleElement.RangeText => 0.55,
            StyleElement.MarkFive or StyleElement.MarkTwo or StyleElement.MarkOne or StyleElement.AltitudeScale => 0.5,
            StyleElement.ApproachLimits or StyleElement.MarkQuarter => 0.45,
            StyleElement.MarkHalf => 0.3,
            _ => 1
        };

        /// <summary>WPF dash array of a dash style (in units of the line width).</summary>
        public static DoubleCollection? DashArray(LineDash dash) => dash switch
        {
            LineDash.Dashed => new DoubleCollection { 4, 3 },
            LineDash.DashDot => new DoubleCollection { 6, 3, 1, 3 },
            LineDash.Dotted => new DoubleCollection { 1, 2 },
            _ => null
        };

        private static Color Mix(Color a, Color b, double t)
        {
            return Color.FromRgb((byte)(a.R + (b.R - a.R) * t), (byte)(a.G + (b.G - a.G) * t), (byte)(a.B + (b.B - a.B) * t));
        }

        private static Brush Frozen(Color color)
        {
            SolidColorBrush brush = new(color);
            brush.Freeze();
            return brush;
        }
    }
}
