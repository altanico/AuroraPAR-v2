using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
using System.Windows.Threading;

namespace AuroraPAR
{
    /// <summary>Colour choice used in the settings windows.</summary>
    internal static class ColorPicker
    {
        /// <summary>Standard colours offered for the elements (any other colour can be typed as #RRGGBB).</summary>
        public static readonly (string Name, string Hex)[] StandardColors =
        [
            ("Green", "#008000"), ("Light green", "#00FF00"), ("Dark green", "#004D00"), ("Yellow", "#FFFF00"),
            ("Amber", "#FFB000"), ("Orange", "#FF8000"), ("Red", "#FF0000"), ("Dark red", "#A00000"),
            ("Cadet blue", "#5F9EA0"), ("Cyan", "#00FFFF"), ("Blue", "#4080FF"), ("Magenta", "#FF00FF"),
            ("White", "#FFFFFF"), ("Light grey", "#C0C0C0"), ("Grey", "#808080"), ("Dark grey", "#404040"), ("Black", "#000000")
        ];

        /// <summary>Phosphors of the analog scope, as on real radar screens.</summary>
        public static readonly (string Name, string Hex)[] Phosphors =
        [
            ("Yellow-green (P39)", "#A8FF60"), ("Amber / yellow", "#FFB830"), ("Green (P1)", "#50FF50"),
            ("Orange", "#FF8C30"), ("Blue-white", "#C8E8FF")
        ];

        /// <summary>
        /// Colour picker: a list of named colours with their swatch, a box to type any colour as #RRGGBB and a
        /// button that opens the visual picker (<see cref="ColorPickerWindow"/>).
        /// </summary>
        public static FrameworkElement Create(string current, (string Name, string Hex)[] choices, Action<string> changed)
        {
            StackPanel panel = new() { Orientation = Orientation.Horizontal };
            ComboBox combo = new() { Width = 130, Height = 22 };
            TextBox hexBox = new()
            {
                Width = 70,
                Height = 22,
                Margin = new Thickness(4, 0, 0, 0),
                VerticalContentAlignment = VerticalAlignment.Center,
                ToolTip = "Any colour as #RRGGBB (Enter to apply)"
            };
            List<(string Name, string Hex)> items = choices.ToList();
            string normalized = ColorText.ToHex(ColorText.Parse(current, System.Windows.Media.Colors.White));
            if (!items.Any(i => string.Equals(i.Hex, normalized, StringComparison.OrdinalIgnoreCase)))
            {
                items.Add(("Custom", normalized));
            }
            foreach ((string name, string hex) in items)
            {
                StackPanel row = new() { Orientation = Orientation.Horizontal, Tag = hex };
                row.Children.Add(new Rectangle
                {
                    Width = 14,
                    Height = 14,
                    Fill = new SolidColorBrush(ColorText.Parse(hex, System.Windows.Media.Colors.White)),
                    Stroke = Brushes.Gray,
                    StrokeThickness = 1,
                    Margin = new Thickness(0, 0, 6, 0)
                });
                row.Children.Add(new TextBlock { Text = name, VerticalAlignment = VerticalAlignment.Center });
                combo.Items.Add(row);
            }
            bool updating = true;
            combo.SelectedIndex = items.FindIndex(i => string.Equals(i.Hex, normalized, StringComparison.OrdinalIgnoreCase));
            hexBox.Text = normalized;
            updating = false;
            combo.SelectionChanged += (s, e) =>
            {
                if (updating || combo.SelectedItem is not FrameworkElement item || item.Tag is not string hex) return;
                hexBox.Text = hex;
                changed(hex);
            };
            void ApplyText()
            {
                if (!ColorText.TryParse(hexBox.Text, out Color color))
                {
                    hexBox.Background = new SolidColorBrush(Color.FromRgb(255, 215, 215));
                    return;
                }
                hexBox.ClearValue(TextBox.BackgroundProperty);
                string hex = ColorText.ToHex(color);
                hexBox.Text = hex;
                int index = items.FindIndex(i => string.Equals(i.Hex, hex, StringComparison.OrdinalIgnoreCase));
                updating = true;
                combo.SelectedIndex = index;
                updating = false;
                changed(hex);
            }
            hexBox.KeyDown += (s, e) =>
            {
                if (e.Key == Key.Enter) ApplyText();
            };
            hexBox.LostFocus += (s, e) =>
            {
                if (!string.Equals(hexBox.Text, normalized, StringComparison.OrdinalIgnoreCase)) ApplyText();
            };
            Border swatch = new()
            {
                Width = 14,
                Height = 14,
                BorderBrush = Brushes.Gray,
                BorderThickness = new Thickness(1),
                Background = new SolidColorBrush(ColorText.Parse(normalized, System.Windows.Media.Colors.White))
            };
            Button pick = new()
            {
                Width = 22,
                Height = 22,
                Margin = new Thickness(4, 0, 0, 0),
                Content = swatch,
                ToolTip = "Choose any colour on a colour square (with live preview)"
            };
            hexBox.TextChanged += (s, e) =>
            {
                if (ColorText.TryParse(hexBox.Text, out Color color)) swatch.Background = new SolidColorBrush(color);
            };
            pick.Click += (s, e) =>
            {
                Color start = ColorText.Parse(hexBox.Text, ColorText.Parse(normalized, System.Windows.Media.Colors.White));
                ColorPickerWindow window = new(start, changed) { Owner = Window.GetWindow(panel) };
                if (window.ShowDialog() == true && window.Result is Color chosen)
                {
                    hexBox.Text = ColorText.ToHex(chosen);
                    ApplyText();
                }
            };
            panel.Children.Add(combo);
            panel.Children.Add(hexBox);
            panel.Children.Add(pick);
            return panel;
        }
    }
}

namespace AuroraPAR
{
    /// <summary>
    /// Visual colour picker: a square with the hue (across) and the saturation (down), a brightness bar, the old and
    /// the new colour, the #RRGGBB code and the colours used recently. While choosing, the colour is applied at
    /// once (live preview); Cancel puts the old one back.
    /// </summary>
    internal sealed class ColorPickerWindow : Window
    {
        private const double SquareWidth = 260;
        private const double SquareHeight = 180;
        private const int MaxRecent = 12;

        private readonly Color original;
        private readonly Action<string> preview;
        private readonly DispatcherTimer previewTimer = new() { Interval = TimeSpan.FromMilliseconds(80) };
        private bool previewPending;
        private bool previewed;
        private double hue, saturation, value;

        private readonly Rectangle valueBar = new() { Width = 22, Height = SquareHeight, Stroke = Brushes.Gray, StrokeThickness = 1 };
        private readonly Canvas squareMarks = new() { Width = SquareWidth, Height = SquareHeight, ClipToBounds = true };
        private readonly Canvas barMarks = new() { Width = 30, Height = SquareHeight };
        private readonly Ellipse squareMarker = new() { Width = 12, Height = 12, Stroke = Brushes.White, StrokeThickness = 2, IsHitTestVisible = false };
        private readonly Ellipse squareMarkerInner = new() { Width = 8, Height = 8, Stroke = Brushes.Black, StrokeThickness = 1, IsHitTestVisible = false };
        private readonly Polygon barMarker = new() { Fill = Brushes.Black, Points = new PointCollection { new Point(0, -5), new Point(6, 0), new Point(0, 5) }, IsHitTestVisible = false };
        private readonly Border newSwatch = new() { Width = 70, Height = 34, BorderBrush = Brushes.Gray, BorderThickness = new Thickness(1) };
        private readonly TextBox hexBox = new() { Width = 70, Height = 22, VerticalContentAlignment = VerticalAlignment.Center };
        private bool updatingText;

        /// <summary>Chosen colour (after OK).</summary>
        public Color? Result { get; private set; }

        public ColorPickerWindow(Color start, Action<string> preview)
        {
            original = Color.FromRgb(start.R, start.G, start.B);
            this.preview = preview;
            Title = "Choose a colour";
            SizeToContent = SizeToContent.WidthAndHeight;
            ResizeMode = ResizeMode.NoResize;
            WindowStartupLocation = WindowStartupLocation.CenterOwner;
            ShowInTaskbar = false;
            (hue, saturation, value) = ToHsv(original);
            previewTimer.Tick += (s, e) =>
            {
                previewTimer.Stop();
                SendPreview();
            };

            Grid layout = new() { Margin = new Thickness(12) };
            layout.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            layout.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            layout.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            layout.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            layout.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            layout.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

            layout.Children.Add(BuildSquare());
            FrameworkElement bar = BuildBar();
            Grid.SetColumn(bar, 1);
            layout.Children.Add(bar);
            FrameworkElement side = BuildSide();
            Grid.SetColumn(side, 2);
            layout.Children.Add(side);
            FrameworkElement recent = BuildRecent();
            Grid.SetRow(recent, 1);
            Grid.SetColumnSpan(recent, 3);
            layout.Children.Add(recent);
            FrameworkElement buttons = BuildButtons();
            Grid.SetRow(buttons, 2);
            Grid.SetColumnSpan(buttons, 3);
            layout.Children.Add(buttons);
            Content = layout;

            Closed += (s, e) =>
            {
                previewTimer.Stop();
                // Closed without OK: the colour shown during the preview goes back to the old one.
                if (Result == null && previewed) preview(ColorText.ToHex(original));
            };
            Update(updateText: true);
            // Nothing to preview until the colour is changed.
            previewPending = false;
            previewTimer.Stop();
        }

        private Color Current => FromHsv(hue, saturation, value);

        private FrameworkElement BuildSquare()
        {
            Grid square = new() { Width = SquareWidth, Height = SquareHeight, Cursor = Cursors.Cross };
            GradientStopCollection hues = new();
            for (int i = 0; i <= 6; i++)
            {
                hues.Add(new GradientStop(FromHsv(i * 60 % 360, 1, 1), i / 6.0));
            }
            square.Children.Add(new Rectangle { Fill = new LinearGradientBrush(hues, new Point(0, 0), new Point(1, 0)) });
            // Saturation: full at the top, white (no colour) at the bottom.
            square.Children.Add(new Rectangle
            {
                Fill = new LinearGradientBrush(Color.FromArgb(0, 255, 255, 255), System.Windows.Media.Colors.White, new Point(0, 0), new Point(0, 1))
            });
            square.Children.Add(new Rectangle { Stroke = Brushes.Gray, StrokeThickness = 1 });
            squareMarks.Children.Add(squareMarker);
            squareMarks.Children.Add(squareMarkerInner);
            square.Children.Add(squareMarks);
            void Pick(Point p)
            {
                hue = Math.Clamp(p.X / SquareWidth, 0, 1) * 359.999;
                saturation = 1 - Math.Clamp(p.Y / SquareHeight, 0, 1);
                // Choosing on the square with the brightness at zero would change nothing visible.
                if (value < 0.05) value = 1;
                Update(updateText: true);
            }
            Drag(square, Pick);
            return square;
        }

        private FrameworkElement BuildBar()
        {
            Grid bar = new() { Margin = new Thickness(10, 0, 0, 0), Cursor = Cursors.Hand, Background = Brushes.Transparent };
            bar.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            bar.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(8) });
            bar.Children.Add(valueBar);
            Grid.SetColumn(barMarks, 1);
            barMarks.Width = 8;
            barMarks.Children.Add(barMarker);
            bar.Children.Add(barMarks);
            bar.ToolTip = "Brightness";
            Drag(bar, p =>
            {
                value = 1 - Math.Clamp(p.Y / SquareHeight, 0, 1);
                Update(updateText: true);
            });
            return bar;
        }

        private FrameworkElement BuildSide()
        {
            StackPanel side = new() { Margin = new Thickness(14, 0, 0, 0) };
            side.Children.Add(new TextBlock { Text = "New", Foreground = Brushes.DimGray });
            side.Children.Add(newSwatch);
            side.Children.Add(new TextBlock { Text = "Old", Foreground = Brushes.DimGray, Margin = new Thickness(0, 6, 0, 0) });
            Border oldSwatch = new()
            {
                Width = 70,
                Height = 34,
                BorderBrush = Brushes.Gray,
                BorderThickness = new Thickness(1),
                Background = new SolidColorBrush(original),
                Cursor = Cursors.Hand,
                ToolTip = "Click to go back to the old colour"
            };
            oldSwatch.MouseLeftButtonDown += (s, e) => SetColor(original);
            side.Children.Add(oldSwatch);
            side.Children.Add(new TextBlock { Text = "Code", Foreground = Brushes.DimGray, Margin = new Thickness(0, 10, 0, 0) });
            hexBox.ToolTip = "#RRGGBB";
            hexBox.TextChanged += (s, e) =>
            {
                if (updatingText) return;
                if (ColorText.TryParse(hexBox.Text, out Color color))
                {
                    hexBox.ClearValue(TextBox.BackgroundProperty);
                    (hue, saturation, value) = ToHsv(color);
                    Update(updateText: false);
                }
                else
                {
                    hexBox.Background = new SolidColorBrush(Color.FromRgb(255, 215, 215));
                }
            };
            side.Children.Add(hexBox);
            return side;
        }

        private FrameworkElement BuildRecent()
        {
            StackPanel panel = new() { Margin = new Thickness(0, 12, 0, 0) };
            List<Color> recent = RecentColors.Load();
            panel.Children.Add(new TextBlock
            {
                Text = recent.Count > 0 ? "Recently used:" : "Recently used: (none yet)",
                Foreground = Brushes.DimGray,
                Margin = new Thickness(0, 0, 0, 4)
            });
            WrapPanel swatches = new() { MaxWidth = SquareWidth + 130 };
            foreach (Color color in recent)
            {
                Border swatch = new()
                {
                    Width = 22,
                    Height = 22,
                    Margin = new Thickness(0, 0, 6, 0),
                    BorderBrush = Brushes.Gray,
                    BorderThickness = new Thickness(1),
                    Background = new SolidColorBrush(color),
                    Cursor = Cursors.Hand,
                    ToolTip = ColorText.ToHex(color)
                };
                swatch.MouseLeftButtonDown += (s, e) => SetColor(color);
                swatches.Children.Add(swatch);
            }
            panel.Children.Add(swatches);
            return panel;
        }

        private FrameworkElement BuildButtons()
        {
            StackPanel buttons = new() { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 14, 0, 0) };
            Button ok = new() { Content = "OK", Width = 80, Height = 26, IsDefault = true };
            Button cancel = new() { Content = "Cancel", Width = 80, Height = 26, Margin = new Thickness(8, 0, 0, 0), IsCancel = true };
            ok.Click += (s, e) =>
            {
                previewTimer.Stop();
                Result = Current;
                RecentColors.Add(Current);
                DialogResult = true;
            };
            buttons.Children.Add(ok);
            buttons.Children.Add(cancel);
            return buttons;
        }

        private void SetColor(Color color)
        {
            (hue, saturation, value) = ToHsv(color);
            Update(updateText: true);
        }

        /// <summary>Markers, swatch, code and brightness bar for the current colour; the preview follows shortly.</summary>
        private void Update(bool updateText)
        {
            Color color = Current;
            newSwatch.Background = new SolidColorBrush(color);
            valueBar.Fill = new LinearGradientBrush(FromHsv(hue, saturation, 1), System.Windows.Media.Colors.Black, new Point(0, 0), new Point(0, 1));
            double x = hue / 360 * SquareWidth;
            double y = (1 - saturation) * SquareHeight;
            Canvas.SetLeft(squareMarker, x - squareMarker.Width / 2);
            Canvas.SetTop(squareMarker, y - squareMarker.Height / 2);
            Canvas.SetLeft(squareMarkerInner, x - squareMarkerInner.Width / 2);
            Canvas.SetTop(squareMarkerInner, y - squareMarkerInner.Height / 2);
            Canvas.SetLeft(barMarker, 1);
            Canvas.SetTop(barMarker, (1 - value) * SquareHeight);
            if (updateText)
            {
                updatingText = true;
                hexBox.Text = ColorText.ToHex(color);
                hexBox.ClearValue(TextBox.BackgroundProperty);
                updatingText = false;
            }
            previewPending = true;
            if (!previewTimer.IsEnabled) previewTimer.Start();
        }

        private void SendPreview()
        {
            if (!previewPending || !IsLoaded) return;
            previewPending = false;
            previewed = true;
            preview(ColorText.ToHex(Current));
        }

        /// <summary>Click or drag with the left button on an element.</summary>
        private static void Drag(FrameworkElement element, Action<Point> pick)
        {
            element.MouseLeftButtonDown += (s, e) =>
            {
                element.CaptureMouse();
                pick(e.GetPosition(element));
                e.Handled = true;
            };
            element.MouseMove += (s, e) =>
            {
                if (element.IsMouseCaptured) pick(e.GetPosition(element));
            };
            element.MouseLeftButtonUp += (s, e) => element.ReleaseMouseCapture();
        }

        private static (double H, double S, double V) ToHsv(Color color)
        {
            double r = color.R / 255.0, g = color.G / 255.0, b = color.B / 255.0;
            double max = Math.Max(r, Math.Max(g, b));
            double min = Math.Min(r, Math.Min(g, b));
            double delta = max - min;
            double h = 0;
            if (delta > 0)
            {
                if (max == r) h = 60 * (((g - b) / delta) % 6);
                else if (max == g) h = 60 * ((b - r) / delta + 2);
                else h = 60 * ((r - g) / delta + 4);
            }
            if (h < 0) h += 360;
            return (h, max == 0 ? 0 : delta / max, max);
        }

        private static Color FromHsv(double h, double s, double v)
        {
            h = (h % 360 + 360) % 360;
            double c = v * s;
            double x = c * (1 - Math.Abs(h / 60 % 2 - 1));
            double m = v - c;
            (double r, double g, double b) = (int)(h / 60) switch
            {
                0 => (c, x, 0.0),
                1 => (x, c, 0.0),
                2 => (0.0, c, x),
                3 => (0.0, x, c),
                4 => (x, 0.0, c),
                _ => (c, 0.0, x)
            };
            return Color.FromRgb((byte)Math.Round((r + m) * 255), (byte)Math.Round((g + m) * 255), (byte)Math.Round((b + m) * 255));
        }

        /// <summary>
        /// Colours chosen recently in the visual picker, shared by AuroraPAR and AuroraCoord: in a small file next to
        /// the program if it exists there (portable), otherwise in %AppData%\AuroraPAR.
        /// </summary>
        private static class RecentColors
        {
            private const string FileName = "recent-colours.txt";

            private static string FilePath
            {
                get
                {
                    string portable = System.IO.Path.Combine(AppContext.BaseDirectory, FileName);
                    if (File.Exists(portable)) return portable;
                    return System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "AuroraPAR", FileName);
                }
            }

            public static List<Color> Load()
            {
                List<Color> colors = [];
                try
                {
                    if (!File.Exists(FilePath)) return colors;
                    foreach (string line in File.ReadAllLines(FilePath))
                    {
                        if (ColorText.TryParse(line.Trim(), out Color color) && !colors.Contains(color)) colors.Add(color);
                        if (colors.Count >= MaxRecent) break;
                    }
                }
                catch (Exception)
                {
                    // Unreadable file: no recent colours.
                }
                return colors;
            }

            public static void Add(Color color)
            {
                try
                {
                    List<Color> colors = Load();
                    color = Color.FromRgb(color.R, color.G, color.B);
                    colors.Remove(color);
                    colors.Insert(0, color);
                    string path = FilePath;
                    Directory.CreateDirectory(System.IO.Path.GetDirectoryName(path)!);
                    File.WriteAllLines(path, colors.Take(MaxRecent).Select(ColorText.ToHex));
                }
                catch (Exception)
                {
                    // Not saved: only the list of recent colours is lost.
                }
            }
        }
    }
}
