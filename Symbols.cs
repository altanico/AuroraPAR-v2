using System.Windows;
using System.Windows.Media;

namespace AuroraPAR
{
    /// <summary>
    /// Shapes available for the track symbol and for the markers (threshold, touchdown point, antenna).
    /// </summary>
    internal enum SymbolShape
    {
        None,
        Line,
        Circle,
        FilledCircle,
        CrossCircle,
        Square,
        Diamond,
        TriangleUp,
        TriangleDown,
        Plus,
        Cross,
        /// <summary>
        /// Filled vertical capsule (bar with rounded ends), as on older PAR displays.
        /// The internal name is kept for compatibility with saved profiles.
        /// </summary>
        ElongatedO,
        /// <summary>Drawn by the user (grid or vector path, see <see cref="SymbolSetting.Path"/>).</summary>
        Custom
    }

    /// <summary>
    /// A shape and its size in pixels.
    /// </summary>
    internal class SymbolSetting
    {
        public SymbolShape Shape { get; set; }
        public double Size { get; set; }
        /// <summary>
        /// Custom shape: vector path (SVG / WPF path syntax) in a box of <see cref="Symbols.CustomBox"/> units
        /// centred on (0, 0), scaled to <see cref="Size"/>.
        /// </summary>
        public string? Path { get; set; }
        /// <summary>Custom shape drawn filled with the symbol colour (otherwise only its outline).</summary>
        public bool Filled { get; set; } = true;
        /// <summary>Custom shape drawn on the grid: the cells, row by row ("1" drawn, "0" empty); null when typed as a path.</summary>
        public string? Cells { get; set; }

        public SymbolSetting() { }

        public SymbolSetting(SymbolShape shape, double size)
        {
            Shape = shape;
            Size = size;
        }
    }

    internal static class Symbols
    {
        /// <summary>
        /// Names shown in the settings.
        /// </summary>
        public static string DisplayName(SymbolShape shape) => shape switch
        {
            SymbolShape.None => "None",
            SymbolShape.Line => "Line",
            SymbolShape.Circle => "Circle",
            SymbolShape.FilledCircle => "Filled circle",
            SymbolShape.CrossCircle => "Circle with cross",
            SymbolShape.Square => "Square",
            SymbolShape.Diamond => "Diamond",
            SymbolShape.TriangleUp => "Triangle",
            SymbolShape.TriangleDown => "Inverted triangle",
            SymbolShape.Plus => "Cross +",
            SymbolShape.Cross => "Cross ×",
            SymbolShape.ElongatedO => "Capsule",
            SymbolShape.Custom => "Custom...",
            _ => shape.ToString()
        };

        /// <summary>
        /// True for the shapes drawn filled with the symbol colour.
        /// </summary>
        public static bool IsFilled(SymbolShape shape) =>
            shape is SymbolShape.FilledCircle or SymbolShape.Square or SymbolShape.TriangleUp or SymbolShape.TriangleDown or SymbolShape.ElongatedO;

        /// <summary>Side of the box of the custom shapes (units of the path; also the cells of the grid).</summary>
        public const int CustomBox = 15;

        /// <summary>Geometry of a symbol setting (also a custom one), centred on (0, 0).</summary>
        public static Geometry Create(SymbolSetting symbol)
        {
            if (symbol.Shape != SymbolShape.Custom) return Create(symbol.Shape, symbol.Size);
            return CreateCustom(symbol.Path, symbol.Size) ?? Create(SymbolShape.CrossCircle, symbol.Size);
        }

        public static bool IsFilled(SymbolSetting symbol) => symbol.Shape == SymbolShape.Custom ? symbol.Filled : IsFilled(symbol.Shape);

        /// <summary>A custom path scaled to the size, or null when the text is not a valid path.</summary>
        public static Geometry? CreateCustom(string? path, double size)
        {
            if (string.IsNullOrWhiteSpace(path)) return null;
            try
            {
                Geometry geometry = Geometry.Parse(path).Clone();
                double scale = Math.Max(size, 2) / CustomBox;
                geometry.Transform = new ScaleTransform(scale, scale);
                if (geometry.CanFreeze) geometry.Freeze();
                return geometry;
            }
            catch
            {
                return null;
            }
        }

        /// <summary>Path of the cells drawn on the grid: one rectangle for each run of cells in a row.</summary>
        public static string PathFromCells(string cells)
        {
            List<string> parts = [];
            double half = CustomBox / 2.0;
            for (int row = 0; row < CustomBox; row++)
            {
                int column = 0;
                while (column < CustomBox)
                {
                    int index = row * CustomBox + column;
                    if (index >= cells.Length || cells[index] != '1')
                    {
                        column++;
                        continue;
                    }
                    int start = column;
                    while (column < CustomBox && row * CustomBox + column < cells.Length && cells[row * CustomBox + column] == '1') column++;
                    string x = (start - half).ToString(System.Globalization.CultureInfo.InvariantCulture);
                    string y = (row - half).ToString(System.Globalization.CultureInfo.InvariantCulture);
                    parts.Add($"M{x},{y} h{column - start} v1 h{start - column} Z");
                }
            }
            return string.Join(" ", parts);
        }

        /// <summary>
        /// Geometry of the shape centred on (0, 0), fitting a square of the given size.
        /// </summary>
        public static Geometry Create(SymbolShape shape, double size)
        {
            double r = Math.Max(size, 2) / 2;
            Geometry geometry = shape switch
            {
                SymbolShape.Line => new LineGeometry(new Point(0, -r), new Point(0, r)),
                SymbolShape.Circle or SymbolShape.FilledCircle => new EllipseGeometry(new Point(0, 0), r, r),
                SymbolShape.CrossCircle => Group(
                    new EllipseGeometry(new Point(0, 0), r, r),
                    new LineGeometry(new Point(-r, 0), new Point(r, 0)),
                    new LineGeometry(new Point(0, -r), new Point(0, r))),
                SymbolShape.Square => new RectangleGeometry(new Rect(-r, -r, 2 * r, 2 * r)),
                SymbolShape.Diamond => Polygon(new Point(0, -r), new Point(r, 0), new Point(0, r), new Point(-r, 0)),
                SymbolShape.TriangleUp => Polygon(new Point(0, -r), new Point(r, r * 0.8), new Point(-r, r * 0.8)),
                SymbolShape.TriangleDown => Polygon(new Point(0, r), new Point(r, -r * 0.8), new Point(-r, -r * 0.8)),
                SymbolShape.Plus => Group(
                    new LineGeometry(new Point(-r, 0), new Point(r, 0)),
                    new LineGeometry(new Point(0, -r), new Point(0, r))),
                SymbolShape.Cross => Group(
                    new LineGeometry(new Point(-r, -r), new Point(r, r)),
                    new LineGeometry(new Point(-r, r), new Point(r, -r))),
                SymbolShape.ElongatedO => new RectangleGeometry(new Rect(-r * 0.45, -r, r * 0.9, 2 * r), r * 0.3, r * 0.3),
                _ => Geometry.Empty
            };
            if (geometry.CanFreeze) geometry.Freeze();
            return geometry;
        }

        private static Geometry Group(params Geometry[] children)
        {
            GeometryGroup group = new();
            foreach (Geometry child in children)
            {
                group.Children.Add(child);
            }
            return group;
        }

        private static Geometry Polygon(params Point[] points)
        {
            StreamGeometry geometry = new();
            using (StreamGeometryContext context = geometry.Open())
            {
                context.BeginFigure(points[0], isFilled: true, isClosed: true);
                context.PolyLineTo(points[1..], isStroked: true, isSmoothJoin: false);
            }
            return geometry;
        }
    }
}
