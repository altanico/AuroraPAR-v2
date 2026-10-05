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
        ElongatedO
    }

    /// <summary>
    /// A shape and its size in pixels.
    /// </summary>
    internal class SymbolSetting
    {
        public SymbolShape Shape { get; set; }
        public double Size { get; set; }

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
            _ => shape.ToString()
        };

        /// <summary>
        /// True for the shapes drawn filled with the symbol colour.
        /// </summary>
        public static bool IsFilled(SymbolShape shape) =>
            shape is SymbolShape.FilledCircle or SymbolShape.Square or SymbolShape.TriangleUp or SymbolShape.TriangleDown or SymbolShape.ElongatedO;

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
