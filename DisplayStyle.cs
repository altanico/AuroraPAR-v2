using System.Globalization;
using System.Windows.Media;

namespace AuroraPAR
{
    /// <summary>Types of range mark, by their interval.</summary>
    internal enum MarkInterval
    {
        Five,
        Two,
        One,
        Half,
        Quarter
    }

    internal static class MarkIntervals
    {
        public static readonly MarkInterval[] All = [MarkInterval.Five, MarkInterval.Two, MarkInterval.One, MarkInterval.Half, MarkInterval.Quarter];

        public static double Nm(MarkInterval interval) => interval switch
        {
            MarkInterval.Five => 5,
            MarkInterval.Two => 2,
            MarkInterval.One => 1,
            MarkInterval.Half => 0.5,
            _ => 0.25
        };

        public static string Name(MarkInterval interval) => interval switch
        {
            MarkInterval.Five => "5 NM",
            MarkInterval.Two => "2 NM",
            MarkInterval.One => "1 NM",
            MarkInterval.Half => "½ NM",
            _ => "¼ NM"
        };

        /// <summary>Style element of a type of range mark.</summary>
        public static StyleElement Element(MarkInterval interval) => interval switch
        {
            MarkInterval.Five => StyleElement.MarkFive,
            MarkInterval.Two => StyleElement.MarkTwo,
            MarkInterval.One => StyleElement.MarkOne,
            MarkInterval.Half => StyleElement.MarkHalf,
            _ => StyleElement.MarkQuarter
        };
    }

    /// <summary>
    /// Range marks of one display range: which types are drawn, and on which ones the distance is written.
    /// </summary>
    internal class RangeMarkRow
    {
        public double Range { get; set; }
        public List<MarkInterval> Lines { get; set; } = [];
        /// <summary>The distance is written on the marks at multiples of this interval; null: no text.</summary>
        public MarkInterval? Text { get; set; }
    }

    /// <summary>
    /// Range marks for every display range (see <see cref="Ranges.Values"/>).
    /// </summary>
    internal class RangeMarkSettings
    {
        public List<RangeMarkRow> Rows { get; set; } = [];

        /// <summary>Default marks: every 2 NM at 20 NM, every NM at 15 and 10, NM and half miles at 5, quarter miles at 2.5 and 1.</summary>
        public static RangeMarkSettings Default()
        {
            RangeMarkSettings settings = new();
            foreach (double range in Ranges.Values)
            {
                settings.Rows.Add(DefaultRow(range));
            }
            return settings;
        }

        public static RangeMarkRow DefaultRow(double range)
        {
            return range switch
            {
                >= 20 => new() { Range = range, Lines = [MarkInterval.Two], Text = MarkInterval.Two },
                >= 10 => new() { Range = range, Lines = [MarkInterval.One], Text = MarkInterval.One },
                >= 5 => new() { Range = range, Lines = [MarkInterval.One, MarkInterval.Half], Text = MarkInterval.One },
                _ => new() { Range = range, Lines = [MarkInterval.Quarter], Text = MarkInterval.Quarter }
            };
        }

        /// <summary>Row of the given range (the default one if missing).</summary>
        public RangeMarkRow For(double range)
        {
            return Rows.FirstOrDefault(r => Math.Abs(r.Range - range) < 0.01) ?? DefaultRow(range);
        }

        /// <summary>One row per display range, without duplicates or unknown values.</summary>
        public void Normalize()
        {
            Rows ??= [];
            List<RangeMarkRow> rows = [];
            foreach (double range in Ranges.Values)
            {
                RangeMarkRow? row = Rows.FirstOrDefault(r => r != null && Math.Abs(r.Range - range) < 0.01);
                if (row == null)
                {
                    rows.Add(DefaultRow(range));
                    continue;
                }
                row.Range = range;
                row.Lines = (row.Lines ?? []).Where(i => Enum.IsDefined(i)).Distinct().OrderBy(i => i).ToList();
                if (row.Text is MarkInterval text && !Enum.IsDefined(text)) row.Text = null;
                rows.Add(row);
            }
            Rows = rows;
        }

        /// <summary>
        /// Marks to draw at a range: distance from touchdown, style element and whether the distance is written.
        /// Where marks of different types coincide (e.g. 1 NM is also a half and a quarter mile) one line is
        /// drawn, with the style of the largest type.
        /// </summary>
        public IEnumerable<(double Distance, StyleElement Element, bool Text)> Marks(double range)
        {
            RangeMarkRow row = For(range);
            List<MarkInterval> lines = row.Lines.OrderByDescending(MarkIntervals.Nm).ToList();
            if (lines.Count == 0) yield break;
            double step = MarkIntervals.Nm(lines[^1]);
            int count = (int)Math.Floor(range / step + 1e-6);
            for (int k = 1; k <= count; k++)
            {
                double distance = Math.Round(k * step, 3);
                MarkInterval type = lines.First(i => IsMultiple(distance, MarkIntervals.Nm(i)));
                bool text = row.Text is MarkInterval t && IsMultiple(distance, MarkIntervals.Nm(t));
                yield return (distance, MarkIntervals.Element(type), text);
            }
        }

        private static bool IsMultiple(double value, double interval)
        {
            double q = value / interval;
            return Math.Abs(q - Math.Round(q)) < 1e-6;
        }

        public static string Label(double distance)
        {
            return distance.ToString("0.##", CultureInfo.InvariantCulture) + "NM";
        }
    }

    internal enum LineDash
    {
        Solid,
        Dashed,
        DashDot,
        Dotted
    }

    /// <summary>Elements of the display that have their own colour (and, for lines, style and width).</summary>
    internal enum StyleElement
    {
        MarkFive,
        MarkTwo,
        MarkOne,
        MarkHalf,
        MarkQuarter,
        RangeText,
        GlidePath,
        Centerline,
        ApproachLimits,
        ScanLimits,
        DecisionHeight,
        Runway,
        Ground,
        Touchdown,
        AltitudeScale,
        Background,
        PlotInside,
        PlotOutside,
        TrackInside,
        TrackOutside,
        LabelText
    }

    /// <summary>Colour (as #RRGGBB), dash style and width of an element.</summary>
    internal class LineStyle
    {
        public string Color { get; set; } = "#00FF00";
        public LineDash Dash { get; set; } = LineDash.Solid;
        public double Width { get; set; } = 1;

        public LineStyle Copy() => new() { Color = Color, Dash = Dash, Width = Width };
    }

    /// <summary>
    /// Colours, dash styles and widths of the modern display (the analog scope has a fixed theme, only its
    /// phosphor colour can be chosen).
    /// </summary>
    internal class DisplayStyleSettings
    {
        public Dictionary<StyleElement, LineStyle> Elements { get; set; } = [];

        /// <summary>True for elements drawn as lines (with dash style and width); the others have only a colour.</summary>
        public static bool IsLine(StyleElement element) => element switch
        {
            StyleElement.RangeText or StyleElement.Background or StyleElement.PlotInside or StyleElement.PlotOutside
                or StyleElement.TrackInside or StyleElement.TrackOutside or StyleElement.LabelText => false,
            _ => true
        };

        public static string Name(StyleElement element) => element switch
        {
            StyleElement.MarkFive => "Range marks 5 NM",
            StyleElement.MarkTwo => "Range marks 2 NM",
            StyleElement.MarkOne => "Range marks 1 NM",
            StyleElement.MarkHalf => "Range marks ½ NM",
            StyleElement.MarkQuarter => "Range marks ¼ NM",
            StyleElement.RangeText => "Range mark text",
            StyleElement.GlidePath => "Glide path",
            StyleElement.Centerline => "Centreline",
            StyleElement.ApproachLimits => "Approach limits",
            StyleElement.ScanLimits => "Scan limits and antenna",
            StyleElement.DecisionHeight => "Decision height",
            StyleElement.Runway => "Runway and threshold",
            StyleElement.Ground => "Ground",
            StyleElement.Touchdown => "Touchdown point",
            StyleElement.AltitudeScale => "Altitude scale",
            StyleElement.Background => "Background",
            StyleElement.PlotInside => "Plots inside limits (history)",
            StyleElement.PlotOutside => "Plots outside limits (history)",
            StyleElement.TrackInside => "Tracks inside limits",
            StyleElement.TrackOutside => "Tracks outside limits",
            StyleElement.LabelText => "Label text",
            _ => element.ToString()
        };

        public static LineStyle Default(StyleElement element) => element switch
        {
            StyleElement.MarkHalf => new() { Color = "#008000", Dash = LineDash.Dashed, Width = 1 },
            StyleElement.MarkFive or StyleElement.MarkTwo or StyleElement.MarkOne or StyleElement.MarkQuarter => new() { Color = "#008000", Width = 1 },
            StyleElement.RangeText => new() { Color = "#FFFF00" },
            StyleElement.GlidePath => new() { Color = "#FFFF00", Width = 2 },
            StyleElement.Centerline => new() { Color = "#FFFF00", Width = 2 },
            StyleElement.ApproachLimits => new() { Color = "#FF0000", Width = 1 },
            StyleElement.ScanLimits => new() { Color = "#5F9EA0", Width = 3 },
            StyleElement.DecisionHeight => new() { Color = "#FF0000", Width = 2 },
            StyleElement.Runway => new() { Color = "#008000", Width = 3 },
            StyleElement.Ground => new() { Color = "#008000", Width = 2 },
            StyleElement.Touchdown => new() { Color = "#FFFF00", Width = 2 },
            StyleElement.AltitudeScale => new() { Color = "#808080", Width = 1 },
            StyleElement.Background => new() { Color = "#000000" },
            StyleElement.PlotInside => new() { Color = "#008000" },
            StyleElement.PlotOutside => new() { Color = "#FF0000" },
            StyleElement.TrackInside => new() { Color = "#008000" },
            StyleElement.TrackOutside => new() { Color = "#FF0000" },
            StyleElement.LabelText => new() { Color = "#FFFFFF" },
            _ => new()
        };

        public static DisplayStyleSettings CreateDefault()
        {
            DisplayStyleSettings settings = new();
            settings.Normalize();
            return settings;
        }

        public LineStyle Get(StyleElement element)
        {
            return Elements.TryGetValue(element, out LineStyle? style) ? style : Default(element);
        }

        /// <summary>Every element present, with a valid colour, dash style and width.</summary>
        public void Normalize()
        {
            Elements ??= [];
            foreach (StyleElement element in Enum.GetValues<StyleElement>())
            {
                if (!Elements.TryGetValue(element, out LineStyle? style) || style == null)
                {
                    Elements[element] = Default(element);
                    continue;
                }
                if (!ColorText.TryParse(style.Color, out _)) style.Color = Default(element).Color;
                if (!Enum.IsDefined(style.Dash)) style.Dash = LineDash.Solid;
                style.Width = double.IsNaN(style.Width) ? 1 : Math.Clamp(style.Width, 1, 6);
            }
            foreach (StyleElement unknown in Elements.Keys.Where(k => !Enum.IsDefined(k)).ToList())
            {
                Elements.Remove(unknown);
            }
        }
    }
}
