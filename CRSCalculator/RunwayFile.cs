using System.Globalization;
using System.IO;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace AuroraPAR
{
    /// <summary>One runway of runways.par paired with its opposite end, with the heading and length that follow from the two thresholds.</summary>
    internal sealed class RunwayCheck
    {
        public string Name = "";
        /// <summary>Lines of the file (0-based) that belong to this runway (one per approach).</summary>
        public List<int> Lines = [];
        public double OldHeading;
        public double OldLength;
        /// <summary>Heading for AuroraPAR (sphere) and distance between the two thresholds (ellipsoid).</summary>
        public double NewHeading;
        public double NewLength;
        public bool Heading => Math.Abs(Delta(NewHeading, OldHeading)) > 0.005;
        public bool Length => Math.Abs(NewLength - OldLength) >= 1;
        public static double Delta(double a, double b) => ((a - b + 540) % 360 + 360) % 360 - 180;
    }

    /// <summary>Reads runways.par, pairs the runways with their opposite ends and rewrites headings and lengths.</summary>
    internal static class RunwayFile
    {
        private sealed class Entry
        {
            public int Line;
            public string Icao = "";
            public string Base = "";
            public double Latitude, Longitude, Heading, Length;
        }

        /// <summary>First word of the designator ("16L 3.0" is runway 16L).</summary>
        private static string BaseDesignator(string designator) => designator.Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault()?.ToUpperInvariant() ?? "";

        /// <summary>Opposite end: 16L ↔ 34R, 09 ↔ 27, 36 ↔ 18, C ↔ C; null when the name is not a runway number.</summary>
        public static string? Opposite(string name)
        {
            string digits = new(name.TakeWhile(char.IsDigit).ToArray());
            string suffix = name[digits.Length..];
            if (digits.Length is 0 or > 2 || !int.TryParse(digits, out int number) || number is < 1 or > 36) return null;
            if (suffix.Length > 1 || (suffix.Length == 1 && suffix[0] is not ('L' or 'R' or 'C'))) return null;
            int other = (number + 18 - 1) % 36 + 1;
            string side = suffix switch { "L" => "R", "R" => "L", _ => suffix };
            return other.ToString("00", CultureInfo.InvariantCulture) + side;
        }

        /// <summary>The runways of the file whose opposite end is also in the file.</summary>
        public static List<RunwayCheck> Analyze(IReadOnlyList<string> lines, out List<string> unpaired)
        {
            List<Entry> entries = [];
            for (int i = 0; i < lines.Count; i++)
            {
                string text = lines[i].TrimEnd('\r');
                if (text.TrimStart().StartsWith('#') || string.IsNullOrWhiteSpace(text)) continue;
                string[] f = text.Split(';');
                if (f.Length < 12) continue;
                if (!CoordinateParser.TryParse(f[4], true, out double lat) || !CoordinateParser.TryParse(f[5], false, out double lon)) continue;
                if (!double.TryParse(f[2], NumberStyles.Float, CultureInfo.InvariantCulture, out double heading)) continue;
                double.TryParse(f[6], NumberStyles.Float, CultureInfo.InvariantCulture, out double length);
                entries.Add(new Entry { Line = i, Icao = f[0].Trim().ToUpperInvariant(), Base = BaseDesignator(f[1]), Latitude = lat, Longitude = lon, Heading = heading, Length = length });
            }
            List<RunwayCheck> result = [];
            unpaired = [];
            foreach (var group in entries.GroupBy(e => (e.Icao, e.Base)))
            {
                Entry first = group.First();
                string? opposite = Opposite(first.Base);
                Entry? other = opposite == null ? null : entries.FirstOrDefault(e => e.Icao == first.Icao && e.Base == opposite);
                if (other == null)
                {
                    unpaired.Add($"{first.Icao} {first.Base}");
                    continue;
                }
                (double distance, _, _) = Geodesic.Inverse(first.Latitude, first.Longitude, other.Latitude, other.Longitude);
                if (double.IsNaN(distance) || distance < 1)
                {
                    unpaired.Add($"{first.Icao} {first.Base} (same point as {opposite})");
                    continue;
                }
                result.Add(new RunwayCheck
                {
                    Name = $"{first.Icao} {first.Base}",
                    Lines = group.Select(e => e.Line).ToList(),
                    OldHeading = first.Heading,
                    OldLength = first.Length,
                    NewHeading = Math.Round(Geodesic.SphericalBearing(first.Latitude, first.Longitude, other.Latitude, other.Longitude), 2),
                    NewLength = Math.Round(distance)
                });
            }
            return result;
        }

        /// <summary>Writes the chosen values (only the heading and length fields of the lines; all the rest stays as it is).</summary>
        public static void Apply(List<string> lines, IEnumerable<(RunwayCheck Runway, bool Heading, bool Length)> choices)
        {
            foreach ((RunwayCheck runway, bool heading, bool length) in choices)
            {
                foreach (int index in runway.Lines)
                {
                    string text = lines[index];
                    string ending = text.EndsWith('\r') ? "\r" : "";
                    string[] f = text.TrimEnd('\r').Split(';');
                    if (heading) f[2] = runway.NewHeading.ToString("0.##", CultureInfo.InvariantCulture);
                    if (length) f[6] = runway.NewLength.ToString("0", CultureInfo.InvariantCulture);
                    lines[index] = string.Join(";", f) + ending;
                }
            }
        }
    }

    /// <summary>Window: the runways of a runways.par file with the heading and length calculated from the thresholds.</summary>
    internal sealed class RunwayFileWindow : Window
    {
        private readonly string path;
        private readonly List<string> lines;
        private readonly bool bom;
        private readonly List<(RunwayCheck Runway, CheckBox Heading, CheckBox Length)> rows = [];

        public RunwayFileWindow(string path)
        {
            this.path = path;
            byte[] bytes = File.ReadAllBytes(path);
            bom = bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF;
            lines = [.. new UTF8Encoding(false).GetString(bom ? bytes[3..] : bytes).Split('\n')];
            Title = "Check of runways.par - " + Path.GetFileName(path);
            Owner = Application.Current.MainWindow;
            WindowStartupLocation = WindowStartupLocation.CenterOwner;
            SizeToContent = SizeToContent.Width;
            Height = 560;
            MinWidth = 640;
            List<RunwayCheck> runways = RunwayFile.Analyze(lines, out List<string> unpaired);
            DockPanel root = new() { Margin = new Thickness(12) };
            TextBlock note = new()
            {
                TextWrapping = TextWrapping.Wrap,
                MaxWidth = 700,
                Foreground = Brushes.Gray,
                FontSize = 11.5,
                Margin = new Thickness(0, 0, 0, 8),
                Text = "Each runway is paired with its opposite end in the same file (16L ↔ 34R). The heading is the one for AuroraPAR (sphere), the length is the distance between the two thresholds: it is the runway length only if the thresholds are not displaced. " +
                       "Tick what to write; a heading that differs by more than 1° from the file is not ticked (wrong coordinate?). The old file is kept as runways.par.bak."
            };
            DockPanel.SetDock(note, Dock.Top);
            root.Children.Add(note);
            Button apply = new() { Content = "Write the file", Width = 120, Height = 28, Margin = new Thickness(0, 10, 0, 0), HorizontalAlignment = HorizontalAlignment.Right };
            apply.Click += (s, e) => Write();
            DockPanel.SetDock(apply, Dock.Bottom);
            root.Children.Add(apply);
            if (unpaired.Count > 0)
            {
                TextBlock skipped = new()
                {
                    TextWrapping = TextWrapping.Wrap,
                    MaxWidth = 700,
                    FontSize = 11.5,
                    Foreground = new SolidColorBrush(Color.FromRgb(0xB0, 0x60, 0x00)),
                    Margin = new Thickness(0, 6, 0, 0),
                    Text = $"Not checked (the opposite end is not in the file): {string.Join(", ", unpaired)}."
                };
                DockPanel.SetDock(skipped, Dock.Bottom);
                root.Children.Add(skipped);
            }
            Grid grid = new();
            foreach (double width in new[] { 110, 120, 120, 110, 110, 70, 70 }) grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(width) });
            string[] titles = ["Runway", "Heading in the file", "Heading calculated", "Length in the file", "Length calculated", "Heading", "Length"];
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            for (int c = 0; c < titles.Length; c++)
            {
                TextBlock header = new() { Text = titles[c], FontWeight = FontWeights.SemiBold, Margin = new Thickness(2, 0, 2, 4), TextWrapping = TextWrapping.Wrap };
                Grid.SetColumn(header, c);
                grid.Children.Add(header);
            }
            Brush orange = new SolidColorBrush(Color.FromRgb(0xB0, 0x60, 0x00));
            foreach (RunwayCheck r in runways.OrderBy(r => r.Name, StringComparer.Ordinal))
            {
                int row = grid.RowDefinitions.Count;
                grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
                double headingDelta = Math.Abs(RunwayCheck.Delta(r.NewHeading, r.OldHeading));
                CheckBox heading = new() { IsChecked = r.Heading && headingDelta <= 1, IsEnabled = r.Heading, HorizontalAlignment = HorizontalAlignment.Center };
                CheckBox length = new() { IsChecked = r.Length, IsEnabled = r.Length, HorizontalAlignment = HorizontalAlignment.Center };
                string[] texts =
                [
                    r.Name,
                    r.OldHeading.ToString("0.##", CultureInfo.InvariantCulture) + "°",
                    r.NewHeading.ToString("0.00", CultureInfo.InvariantCulture) + "°" + (headingDelta > 1 ? "  ⚠" : ""),
                    r.OldLength.ToString("0", CultureInfo.InvariantCulture) + " m",
                    r.NewLength.ToString("0", CultureInfo.InvariantCulture) + " m"
                ];
                for (int c = 0; c < texts.Length; c++)
                {
                    TextBlock t = new() { Text = texts[c], Margin = new Thickness(2, 3, 2, 3) };
                    if (c == 2 && headingDelta > 1) t.Foreground = orange;
                    Grid.SetRow(t, row);
                    Grid.SetColumn(t, c);
                    grid.Children.Add(t);
                }
                foreach ((CheckBox box, int c) in new[] { (heading, 5), (length, 6) })
                {
                    Grid.SetRow(box, row);
                    Grid.SetColumn(box, c);
                    box.VerticalAlignment = VerticalAlignment.Center;
                    grid.Children.Add(box);
                }
                rows.Add((r, heading, length));
            }
            root.Children.Add(new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Content = grid });
            Content = root;
            if (runways.Count == 0) note.Text = "No runway with its opposite end was found in this file.";
        }

        private void Write()
        {
            var choices = rows.Select(r => (r.Runway, r.Heading.IsChecked == true, r.Length.IsChecked == true)).Where(c => c.Item2 || c.Item3).ToList();
            if (choices.Count == 0)
            {
                MessageBox.Show(this, "Nothing is ticked.", "CRSCalculator");
                return;
            }
            try
            {
                List<string> output = [.. lines];
                RunwayFile.Apply(output, choices);
                File.Copy(path, path + ".bak", overwrite: true);
                string text = string.Join("\n", output);
                File.WriteAllBytes(path, [.. (bom ? new byte[] { 0xEF, 0xBB, 0xBF } : []), .. new UTF8Encoding(false).GetBytes(text)]);
                MessageBox.Show(this, $"Written: {choices.Count} runways changed. The previous file is {Path.GetFileName(path)}.bak.", "CRSCalculator");
                Close();
            }
            catch (Exception error)
            {
                MessageBox.Show(this, "Cannot write the file: " + error.Message, "CRSCalculator", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }
    }
}
