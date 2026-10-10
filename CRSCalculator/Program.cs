using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace AuroraPAR
{
    /// <summary>
    /// CRSCalculator: runway heading and final course from the coordinates of the two thresholds, and the current
    /// magnetic variation (World Magnetic Model), to check <c>runways.par</c> against the charts.
    /// </summary>
    internal static class Program
    {
        [STAThread]
        public static int Main(string[] args)
        {
            // Check of the calculations, run by the build: CRSCalculator.exe --selftest report.txt
            int test = Array.IndexOf(args, "--selftest");
            if (test >= 0)
            {
                return SelfTest.Run(test + 1 < args.Length ? args[test + 1] : null) ? 0 : 1;
            }
            Application app = new() { ShutdownMode = ShutdownMode.OnMainWindowClose };
            app.DispatcherUnhandledException += (s, e) =>
            {
                e.Handled = true;
                MessageBox.Show("Something went wrong: " + e.Exception.Message, "CRSCalculator", MessageBoxButton.OK, MessageBoxImage.Warning);
            };
            return app.Run(new CalculatorWindow());
        }
    }

    internal sealed class CalculatorWindow : Window
    {
        private static readonly Brush Gray = Brushes.Gray;
        private static readonly Brush Amber = new SolidColorBrush(Color.FromRgb(0xB0, 0x60, 0x00));
        private static readonly Brush BadInput = new SolidColorBrush(Color.FromRgb(0xFF, 0xD7, 0xD7));

        // Latitude and longitude of each threshold in their own boxes, as the two fields of runways.par.
        private readonly TextBox latitudeA = CoordinateBox();
        private readonly TextBox longitudeA = CoordinateBox();
        private readonly TextBox latitudeB = CoordinateBox();
        private readonly TextBox longitudeB = CoordinateBox();
        private bool splitting;

        private static TextBox CoordinateBox() => new() { Width = 270, Height = 24, VerticalContentAlignment = VerticalAlignment.Center };
        private readonly TextBox lengthBox = new() { Width = 110, Height = 24, VerticalContentAlignment = VerticalAlignment.Center };
        private readonly TextBox variationBox = new() { Width = 110, Height = 24, VerticalContentAlignment = VerticalAlignment.Center };
        private readonly TextBox dateBox = new() { Width = 110, Height = 24, VerticalContentAlignment = VerticalAlignment.Center };
        private readonly StackPanel results = new() { Margin = new Thickness(0, 10, 0, 0) };

        public CalculatorWindow()
        {
            Title = "CRSCalculator - runway heading and final course";
            SizeToContent = SizeToContent.WidthAndHeight;
            ResizeMode = ResizeMode.CanMinimize;
            WindowStartupLocation = WindowStartupLocation.CenterScreen;
            try
            {
                Icon = new System.Windows.Media.Imaging.BitmapImage(new Uri("pack://application:,,,/CRSCalculator.ico"));
            }
            catch (Exception)
            {
            }
            StackPanel root = new() { Margin = new Thickness(14), Width = 560 };
            root.Children.Add(Note("Heading of a runway from the coordinates of its two thresholds, for the Runways editor of AuroraPAR (true heading) and to check the final course (CRS) against the charts. Coordinates in any format: 40.232271, N040 13.9, 401357N, 0180821E ... (the same as the LATITUDE and LONGITUDE fields of runways.par). Use the thresholds of the landing runways and at least 6 decimals (or seconds with 2 decimals). Pasting both coordinates, or a whole line of runways.par, in the latitude box fills both boxes."));

            root.Children.Add(Caption("Threshold A: the runway whose heading you want (the aircraft lands from here)"));
            root.Children.Add(CoordinateRow(latitudeA, longitudeA));
            root.Children.Add(Caption("Threshold B: the other end of the runway (the threshold of the opposite runway)"));
            root.Children.Add(CoordinateRow(latitudeB, longitudeB));

            StackPanel options = new() { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 10, 0, 0) };
            options.Children.Add(Field("Published length (optional, m or ft):", lengthBox, "For example 3000 or 9843ft: the distance between the thresholds is compared with it."));
            options.Children.Add(Field("Published variation (optional):", variationBox, "As on the chart: 3E, 2.5W, -2.5 ..."));
            options.Children.Add(Field("Date:", dateBox, "Date for the magnetic variation (year-month-day). The variation changes slowly, about 0.1° a year."));
            root.Children.Add(options);
            dateBox.Text = DateTime.UtcNow.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
            root.Children.Add(results);
            Button fileButton = new()
            {
                Content = "Check a runways.par file...",
                Height = 26,
                Padding = new Thickness(12, 0, 12, 0),
                HorizontalAlignment = HorizontalAlignment.Left,
                Margin = new Thickness(0, 14, 0, 0),
                ToolTip = "Reads a runways.par, pairs each runway with its opposite end and proposes the heading and the length calculated from the thresholds, to write in the file (with a .bak copy)."
            };
            fileButton.Click += (s, e) =>
            {
                Microsoft.Win32.OpenFileDialog dialog = new() { Title = "runways.par", Filter = "runways.par|*.par|All files|*.*" };
                if (dialog.ShowDialog(this) != true) return;
                try
                {
                    new RunwayFileWindow(dialog.FileName).ShowDialog();
                }
                catch (Exception error)
                {
                    MessageBox.Show(this, "Cannot read the file: " + error.Message, "CRSCalculator", MessageBoxButton.OK, MessageBoxImage.Warning);
                }
            };
            root.Children.Add(fileButton);
            Content = root;

            foreach (TextBox box in new[] { latitudeA, longitudeA, latitudeB, longitudeB, lengthBox, variationBox, dateBox })
            {
                box.TextChanged += (s, e) => Recalculate();
            }
            latitudeA.TextChanged += (s, e) => Split(latitudeA, longitudeA);
            latitudeB.TextChanged += (s, e) => Split(latitudeB, longitudeB);
            Recalculate();
        }

        private static TextBlock Note(string text) => new()
        {
            Text = text,
            TextWrapping = TextWrapping.Wrap,
            Foreground = Gray,
            FontSize = 11.5,
            Margin = new Thickness(0, 0, 0, 8)
        };

        private static FrameworkElement CoordinateRow(TextBox latitude, TextBox longitude)
        {
            StackPanel row = new() { Orientation = Orientation.Horizontal };
            foreach ((string caption, TextBox box) in new[] { ("Latitude", latitude), ("Longitude", longitude) })
            {
                StackPanel field = new() { Margin = new Thickness(0, 0, 14, 0) };
                field.Children.Add(new TextBlock { Text = caption, FontSize = 11.5, Foreground = Gray });
                field.Children.Add(box);
                row.Children.Add(field);
            }
            return row;
        }

        /// <summary>
        /// Both coordinates (or a whole line of runways.par: its fields 5 and 6) pasted in the latitude box: the
        /// longitude goes to its own box.
        /// </summary>
        private void Split(TextBox latitude, TextBox longitude)
        {
            if (splitting) return;
            string text = latitude.Text.Trim();
            string? lat = null;
            string? lon = null;
            string[] fields = text.Split(';');
            if (fields.Length >= 6 && CoordinateParser.TryParse(fields[4], true, out _) && CoordinateParser.TryParse(fields[5], false, out _))
            {
                lat = fields[4].Trim();
                lon = fields[5].Trim();
            }
            else if (!CoordinateParser.TryParse(text, true, out _)
                && CoordinateParser.TryParsePair(text, out _, out _, out string latText, out string lonText))
            {
                lat = latText;
                lon = lonText;
            }
            if (lat == null || lon == null) return;
            splitting = true;
            latitude.Text = lat;
            longitude.Text = lon;
            latitude.CaretIndex = lat.Length;
            splitting = false;
            Recalculate();
        }

        private static TextBlock Caption(string text) => new() { Text = text, Margin = new Thickness(0, 8, 0, 2) };

        private static FrameworkElement Field(string caption, TextBox box, string toolTip)
        {
            StackPanel panel = new() { Margin = new Thickness(0, 0, 14, 0), ToolTip = toolTip };
            panel.Children.Add(new TextBlock { Text = caption, FontSize = 11.5, Margin = new Thickness(0, 0, 0, 2) });
            panel.Children.Add(box);
            return panel;
        }

        private static string Number(double value, string format = "0.00") => value.ToString(format, CultureInfo.InvariantCulture);

        private static string Variation(double value) => $"{Number(Math.Abs(value), "0.0")}°{(value >= 0 ? "E" : "W")}";

        /// <summary>A result line: a title, the value (with a Copy button when there is something to copy) and a note.</summary>
        private void Add(string title, string value, string? copy = null, Brush? color = null, string? note = null)
        {
            DockPanel row = new() { Margin = new Thickness(0, 6, 0, 0) };
            if (copy != null)
            {
                Button button = new() { Content = "Copy", Width = 52, Height = 22, VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(8, 0, 0, 0) };
                button.Click += (s, e) =>
                {
                    try
                    {
                        Clipboard.SetText(copy);
                    }
                    catch (Exception)
                    {
                    }
                };
                DockPanel.SetDock(button, Dock.Right);
                row.Children.Add(button);
            }
            StackPanel text = new();
            text.Children.Add(new TextBlock { Text = title, FontSize = 11.5, Foreground = Gray });
            text.Children.Add(new TextBlock { Text = value, FontSize = 15, FontWeight = FontWeights.SemiBold, Foreground = color ?? Brushes.Black, TextWrapping = TextWrapping.Wrap });
            if (note != null) text.Children.Add(Note(note));
            row.Children.Add(text);
            results.Children.Add(row);
        }

        private void Recalculate()
        {
            results.Children.Clear();
            if (splitting) return;
            bool a = Coordinate(latitudeA, true, out double latA) & Coordinate(longitudeA, false, out double lonA);
            bool b = Coordinate(latitudeB, true, out double latB) & Coordinate(longitudeB, false, out double lonB);
            string latTextA = latitudeA.Text, lonTextA = longitudeA.Text, latTextB = latitudeB.Text, lonTextB = longitudeB.Text;
            if (!a || !b)
            {
                results.Children.Add(Note("Write the latitude and the longitude of the two thresholds (red: not a valid coordinate; e.g. 41.845923 for a latitude, 12.261522 or 0121541E for a longitude)."));
                return;
            }
            (double distance, double azimuth1, double azimuth2) = Geodesic.Inverse(latA, lonA, latB, lonB);
            if (double.IsNaN(distance) || distance < 1)
            {
                results.Children.Add(Note(double.IsNaN(distance) ? "The two points are too far apart for this calculation." : "The two thresholds are the same point."));
                return;
            }
            double heading = Geodesic.MeanAzimuth(azimuth1, azimuth2);
            double reverse = Geodesic.Normalize(heading + 180);
            double radar = Geodesic.SphericalBearing(latA, lonA, latB, lonB);
            double radarReverse = Geodesic.SphericalBearing(latB, lonB, latA, lonA);

            // Heading.
            Add("Heading for AuroraPAR (Runways editor: true heading)", $"{Number(radar)}°", Number(radar), null,
                "Calculated as AuroraPAR does, on a sphere: with this value an aircraft on the extended centreline has zero lateral offset on the radar.");
            Add("True heading on the WGS84 ellipsoid, as the charts give it", $"{Number(heading)}°", Number(heading), null,
                $"Differs from the radar value by {Number(Math.Abs(((heading - radar + 540) % 360) - 180))}°: AuroraPAR measures the angles on a sphere.");
            Add("Opposite runway (heading for AuroraPAR · ellipsoid)", $"{Number(radarReverse)}° · {Number(reverse)}°", Number(radarReverse));

            // Distance and length.
            Brush? lengthColor = null;
            string? lengthNote = null;
            if (TryLength(lengthBox.Text, out double published))
            {
                double difference = distance - published;
                bool large = Math.Abs(difference) > Math.Max(30, published * 0.02);
                lengthColor = large ? Amber : null;
                lengthNote = $"Published length {Number(published, "0")} m: the thresholds are {Number(Math.Abs(difference), "0")} m {(difference >= 0 ? "farther apart" : "closer")}"
                    + (large ? ". Check the coordinates (displaced thresholds? wrong end?)." : ": consistent.");
            }
            Add("Distance between the thresholds", $"{Number(distance, "0")} m  ({Number(distance * 3.280839895, "0")} ft, {Number(distance / 1852, "0.000")} NM)", null, lengthColor, lengthNote);

            // Precision of the coordinates.
            double errorA = HeadingError(latA, latTextA, lonTextA, distance);
            double errorB = HeadingError(latB, latTextB, lonTextB, distance);
            double error = Math.Max(errorA, errorB);
            Add("Precision of the heading, from the digits of the coordinates", $"up to ±{Number(error)}°", null, error > 0.1 ? Amber : null,
                error > 0.1 ? "Too rough: 0.1° is about 48 m of lateral error at 15 NM. Use more decimals (6 or more, or seconds with 2 decimals)." : "Good: the rounding of the coordinates does not matter.");

            // Magnetic variation now.
            double year = WorldMagneticModel.DecimalYear(ParseDate(dateBox.Text));
            double midLat = (latA + latB) / 2;
            double midLon = Math.Abs(lonA - lonB) > 180 ? (lonA + lonB + 360) / 2 % 360 : (lonA + lonB) / 2;
            double declination = WorldMagneticModel.Field(midLat, midLon, 0, year).Declination;
            double change = WorldMagneticModel.Field(midLat, midLon, 0, year + 1).Declination - declination;
            string validity = year < WorldMagneticModel.Epoch || year >= WorldMagneticModel.ValidUntil
                ? "The date is outside the validity of the model (2025-2029): the value is only an extrapolation."
                : "World Magnetic Model WMM2025, accuracy about 0.3°. The value on the charts is updated only now and then, so it may differ.";
            Add("Magnetic variation now at the runway (calculated)", $"{Variation(declination)}   ({(change >= 0 ? "+" : "-")}{Number(Math.Abs(change))}° a year)",
                Number(declination, "0.0") + (declination >= 0 ? "E" : "W"), null, validity);

            // Final course.
            int calculatedA = MagneticVariation.FinalCourse(heading, declination);
            int calculatedB = MagneticVariation.FinalCourse(reverse, declination);
            string course = $"Runway A: {calculatedA:000}    Runway B: {calculatedB:000}";
            string? courseNote = "Magnetic final course (true heading − variation, rounded) with the calculated variation.";
            if (MagneticVariation.TryParse(variationBox.Text, out double publishedVariation))
            {
                int publishedA = MagneticVariation.FinalCourse(heading, publishedVariation);
                int publishedB = MagneticVariation.FinalCourse(reverse, publishedVariation);
                courseNote += $" With the published variation ({Variation(publishedVariation)}): A {publishedA:000}, B {publishedB:000}"
                    + (publishedA == calculatedA && publishedB == calculatedB ? " (the same)." : " (different: check which one the approach chart uses).");
            }
            else if (!string.IsNullOrWhiteSpace(variationBox.Text))
            {
                courseNote += " The published variation was not understood (write for example 3E or 2.5W).";
            }
            Add("Final course (CRS), magnetic", course, null, null, courseNote);
        }

        /// <summary>Reads one coordinate box (red when written but not valid).</summary>
        private static bool Coordinate(TextBox box, bool latitude, out double value)
        {
            bool ok = CoordinateParser.TryParse(box.Text, latitude, out value);
            box.Background = ok || string.IsNullOrWhiteSpace(box.Text) ? Brushes.White : BadInput;
            return ok;
        }

        private static double HeadingError(double latitude, string latText, string lonText, double distance)
        {
            return CoordinatePrecision.HeadingErrorDegrees(latitude, CoordinatePrecision.MaxErrorDegrees(latText, true),
                CoordinatePrecision.MaxErrorDegrees(lonText, false), distance);
        }

        private static DateTime ParseDate(string text)
        {
            return DateTime.TryParse(text, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out DateTime date)
                ? date : DateTime.UtcNow;
        }

        /// <summary>Length in metres from a text such as 3000, 3000m, 9843ft.</summary>
        private static bool TryLength(string text, out double metres)
        {
            metres = 0;
            string t = text.Trim().ToLowerInvariant().Replace(',', '.').Replace(" ", "");
            double factor = 1;
            if (t.EndsWith("ft"))
            {
                t = t[..^2];
                factor = 0.3048;
            }
            else if (t.EndsWith('m'))
            {
                t = t[..^1];
            }
            if (!double.TryParse(t, NumberStyles.Float, CultureInfo.InvariantCulture, out double value) || value <= 0) return false;
            metres = value * factor;
            return true;
        }
    }
}
