using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;

namespace AuroraPAR
{
    /// <summary>
    /// Round frame of the analog scope (as on the PAR consoles of the '70s and '80s): console panel around a round
    /// screen, metal ring with screws and two tabs, darker edges of the glass and a faint reflection.
    /// Purely graphic, drawn over the views and ignored by the mouse.
    /// </summary>
    internal static class ScopeBezel
    {
        public static readonly Color PanelColor = Color.FromRgb(0x2E, 0x30, 0x2C);

        /// <summary>Glass of the screen (behind the phosphor).</summary>
        public static Brush CreateGlassBrush()
        {
            RadialGradientBrush brush = new()
            {
                GradientStops =
                {
                    new GradientStop(Color.FromRgb(0x16, 0x24, 0x12), 0),
                    new GradientStop(Color.FromRgb(0x0C, 0x16, 0x0A), 0.7),
                    new GradientStop(Color.FromRgb(0x05, 0x09, 0x04), 1)
                }
            };
            brush.Freeze();
            return brush;
        }

        /// <summary>
        /// Draws the frame on <paramref name="layer"/> (cleared first): screen of radius <paramref name="radius"/>
        /// centred on <paramref name="center"/>, metal ring <paramref name="ring"/> pixels wide.
        /// </summary>
        public static void Draw(Canvas layer, Size area, Point center, double radius, double ring)
        {
            layer.Children.Clear();
            double outer = radius + ring;
            // Console panel, with a hole for the screen.
            layer.Children.Add(new Path
            {
                Data = new CombinedGeometry(GeometryCombineMode.Exclude,
                    new RectangleGeometry(new Rect(area)), new EllipseGeometry(center, outer, outer)),
                Fill = new LinearGradientBrush(Color.FromRgb(0x3A, 0x3C, 0x37), Color.FromRgb(0x24, 0x26, 0x22), 90)
            });
            // Shadow of the ring on the panel.
            Add(layer, new Ellipse { Stroke = new SolidColorBrush(Color.FromArgb(0x90, 0, 0, 0)), StrokeThickness = 4 }, center, outer + 3);
            // Metal ring.
            Add(layer, new Ellipse
            {
                Stroke = new LinearGradientBrush
                {
                    StartPoint = new Point(0, 0),
                    EndPoint = new Point(1, 1),
                    GradientStops =
                    {
                        new GradientStop(Color.FromRgb(0xC4, 0xC6, 0xC0), 0),
                        new GradientStop(Color.FromRgb(0x80, 0x83, 0x7D), 0.45),
                        new GradientStop(Color.FromRgb(0x3E, 0x40, 0x3B), 1)
                    }
                },
                StrokeThickness = ring
            }, center, outer);
            Add(layer, new Ellipse { Stroke = new SolidColorBrush(Color.FromRgb(0x1A, 0x1B, 0x18)), StrokeThickness = 1 }, center, outer);
            // Inner edge of the ring.
            Add(layer, new Ellipse { Stroke = new SolidColorBrush(Color.FromRgb(0x0A, 0x0A, 0x09)), StrokeThickness = 2 }, center, radius + 1);
            // Screws on the ring.
            double screw = Math.Max(2.5, ring * 0.2);
            for (int i = 0; i < 8; i++)
            {
                double angle = (22.5 + 45 * i) * Math.PI / 180;
                Point p = new(center.X + (radius + ring / 2) * Math.Sin(angle), center.Y - (radius + ring / 2) * Math.Cos(angle));
                Add(layer, new Ellipse
                {
                    Fill = new RadialGradientBrush(Color.FromRgb(0xD8, 0xD8, 0xD0), Color.FromRgb(0x55, 0x56, 0x52)) { GradientOrigin = new Point(0.3, 0.3) },
                    Stroke = new SolidColorBrush(Color.FromRgb(0x22, 0x22, 0x20)),
                    StrokeThickness = 0.8
                }, p, screw);
                double slot = (37 * i) * Math.PI / 180;
                layer.Children.Add(new Line
                {
                    X1 = p.X - screw * 0.7 * Math.Cos(slot),
                    Y1 = p.Y - screw * 0.7 * Math.Sin(slot),
                    X2 = p.X + screw * 0.7 * Math.Cos(slot),
                    Y2 = p.Y + screw * 0.7 * Math.Sin(slot),
                    Stroke = new SolidColorBrush(Color.FromRgb(0x30, 0x30, 0x2E)),
                    StrokeThickness = 1
                });
            }
            // Two tabs holding the glass, at the top and at the bottom.
            foreach (double sign in new[] { -1.0, 1.0 })
            {
                double width = Math.Max(6, ring * 0.6);
                double height = Math.Max(5, ring * 0.55);
                Rectangle tab = new()
                {
                    Width = width,
                    Height = height,
                    Fill = new SolidColorBrush(Color.FromRgb(0x6A, 0x6C, 0x66)),
                    Stroke = new SolidColorBrush(Color.FromRgb(0x1A, 0x1B, 0x18)),
                    StrokeThickness = 1
                };
                Canvas.SetLeft(tab, center.X - width / 2);
                Canvas.SetTop(tab, sign < 0 ? center.Y - radius - height / 2 : center.Y + radius - height / 2);
                layer.Children.Add(tab);
            }
            // Darker edges of the glass.
            Add(layer, new Ellipse
            {
                Fill = new RadialGradientBrush
                {
                    GradientStops =
                    {
                        new GradientStop(Color.FromArgb(0, 0, 0, 0), 0),
                        new GradientStop(Color.FromArgb(0, 0, 0, 0), 0.62),
                        new GradientStop(Color.FromArgb(0xA0, 0, 0, 0), 1)
                    }
                }
            }, center, radius);
            // Faint reflection on the upper part of the glass.
            layer.Children.Add(new Path
            {
                Data = new EllipseGeometry(new Point(center.X - radius * 0.2, center.Y - radius * 0.5), radius * 0.75, radius * 0.42),
                Fill = new LinearGradientBrush(Color.FromArgb(0x1C, 0xFF, 0xFF, 0xFF), Color.FromArgb(0, 0xFF, 0xFF, 0xFF), 90),
                Clip = new EllipseGeometry(center, radius, radius)
            });
        }

        private static void Add(Canvas layer, Shape ellipse, Point center, double radius)
        {
            ellipse.Width = 2 * radius;
            ellipse.Height = 2 * radius;
            Canvas.SetLeft(ellipse, center.X - radius);
            Canvas.SetTop(ellipse, center.Y - radius);
            layer.Children.Add(ellipse);
        }
    }
}
