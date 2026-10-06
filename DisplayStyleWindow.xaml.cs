using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;

namespace AuroraPAR
{
    /// <summary>
    /// Display style of the active profile: range marks at each range (and, in the next tabs, colours and lines).
    /// Every change is applied and saved at once.
    /// </summary>
    public partial class DisplayStyleWindow : Window
    {
        private readonly AppSettings settings;
        private readonly Action commit;
        private bool building;

        internal DisplayStyleWindow(AppSettings settings, Action commit)
        {
            InitializeComponent();
            this.settings = settings;
            this.commit = commit;
            CloseButton.Click += (s, e) => Close();
            RangeMarksDefaultButton.Click += (s, e) =>
            {
                Active.RangeMarks = RangeMarkSettings.Default();
                commit();
                BuildRangeMarks();
            };
            BuildRangeMarks();
            StyleDefaultButton.Click += (s, e) =>
            {
                Active.Style = DisplayStyleSettings.CreateDefault();
                commit();
                BuildStyles();
            };
            BuildStyles();
            BuildPhosphor();
        }

        /// <summary>Standard colours offered for the elements (any other colour can be typed as #RRGGBB).</summary>
        private static readonly (string Name, string Hex)[] StandardColors =
        [
            ("Green", "#008000"), ("Light green", "#00FF00"), ("Dark green", "#004D00"), ("Yellow", "#FFFF00"),
            ("Amber", "#FFB000"), ("Orange", "#FF8000"), ("Red", "#FF0000"), ("Dark red", "#A00000"),
            ("Cadet blue", "#5F9EA0"), ("Cyan", "#00FFFF"), ("Blue", "#4080FF"), ("Magenta", "#FF00FF"),
            ("White", "#FFFFFF"), ("Light grey", "#C0C0C0"), ("Grey", "#808080"), ("Dark grey", "#404040"), ("Black", "#000000")
        ];

        /// <summary>Phosphors of the analog scope, as on real radar screens.</summary>
        private static readonly (string Name, string Hex)[] Phosphors =
        [
            ("Yellow-green (P39)", "#A8FF60"), ("Amber / yellow", "#FFB830"), ("Green (P1)", "#50FF50"),
            ("Orange", "#FF8C30"), ("Blue-white", "#C8E8FF")
        ];

        private static readonly string[] DashNames = ["Solid", "Dashed", "Dash-dot", "Dotted"];

        /// <summary>
        /// Table of the elements of the modern display: colour, line style, width and a preview.
        /// </summary>
        private void BuildStyles()
        {
            building = true;
            Grid grid = StyleGrid;
            grid.Children.Clear();
            grid.RowDefinitions.Clear();
            grid.ColumnDefinitions.Clear();
            foreach (double width in new double[] { 200, 250, 100, 70, 80 })
            {
                grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(width) });
            }
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            string[] headers = ["Element", "Colour", "Line", "Width", "Preview"];
            for (int c = 0; c < headers.Length; c++)
            {
                TextBlock header = Header(headers[c]);
                header.TextAlignment = TextAlignment.Left;
                AddCell(grid, header, 0, c);
            }
            int r = 1;
            foreach (StyleElement element in Enum.GetValues<StyleElement>())
            {
                LineStyle style = Active.Style.Get(element);
                bool isLine = DisplayStyleSettings.IsLine(element);
                grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(30) });
                AddCell(grid, new TextBlock { Text = DisplayStyleSettings.Name(element), VerticalAlignment = VerticalAlignment.Center }, r, 0);
                Border preview = new() { Background = Brushes.Black, Height = 20, Width = 70, HorizontalAlignment = HorizontalAlignment.Left };
                void UpdatePreview()
                {
                    LineStyle current = Active.Style.Get(element);
                    Brush brush = new SolidColorBrush(ColorText.Parse(current.Color, System.Windows.Media.Colors.White));
                    Brush background = new SolidColorBrush(ColorText.Parse(Active.Style.Get(StyleElement.Background).Color, System.Windows.Media.Colors.Black));
                    preview.Background = element == StyleElement.Background ? brush : background;
                    preview.Child = element == StyleElement.Background ? null
                        : isLine ? new Line
                        {
                            X1 = 6, Y1 = 10, X2 = 64, Y2 = 10,
                            Stroke = brush,
                            StrokeThickness = current.Width,
                            StrokeDashArray = Theme.DashArray(current.Dash)
                        }
                        : element == StyleElement.RangeText || element == StyleElement.LabelText
                            ? new TextBlock { Text = "4NM", Foreground = brush, FontSize = 12, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center }
                            : new Ellipse { Width = 10, Height = 10, Fill = brush, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
                }
                void Change(Action<LineStyle> set)
                {
                    if (building) return;
                    LineStyle current = Active.Style.Get(element).Copy();
                    set(current);
                    Active.Style.Elements[element] = current;
                    commit();
                    // The background is in every preview.
                    if (element == StyleElement.Background) BuildStyles(); else UpdatePreview();
                }
                AddCell(grid, CreateColorPicker(style.Color, StandardColors, hex => Change(s => s.Color = hex)), r, 1);
                if (isLine)
                {
                    ComboBox dash = new() { ItemsSource = DashNames, SelectedIndex = (int)style.Dash, Width = 90, Height = 22, HorizontalAlignment = HorizontalAlignment.Left };
                    dash.SelectionChanged += (s, e) => Change(st => st.Dash = (LineDash)Math.Max(0, dash.SelectedIndex));
                    AddCell(grid, dash, r, 2);
                    ComboBox width = new()
                    {
                        ItemsSource = Enumerable.Range(1, 6).Select(w => $"{w} px").ToList(),
                        SelectedIndex = Math.Clamp((int)Math.Round(style.Width) - 1, 0, 5),
                        Width = 60,
                        Height = 22,
                        HorizontalAlignment = HorizontalAlignment.Left
                    };
                    width.SelectionChanged += (s, e) => Change(st => st.Width = Math.Max(0, width.SelectedIndex) + 1);
                    AddCell(grid, width, r, 3);
                }
                UpdatePreview();
                AddCell(grid, preview, r, 4);
                r++;
            }
            building = false;
        }

        private void BuildPhosphor()
        {
            StackPanel parent = (StackPanel)PhosphorComboBox.Parent;
            int index = parent.Children.IndexOf(PhosphorComboBox);
            parent.Children.RemoveAt(index);
            FrameworkElement picker = CreateColorPicker(Active.AnalogColor, Phosphors, hex =>
            {
                Active.AnalogColor = hex;
                commit();
            });
            picker.Margin = new Thickness(8, 0, 0, 0);
            parent.Children.Insert(index, picker);
        }

        /// <summary>
        /// Colour picker: a list of named colours with their swatch, and a box to type any colour as #RRGGBB.
        /// </summary>
        private FrameworkElement CreateColorPicker(string current, (string Name, string Hex)[] choices, Action<string> changed)
        {
            StackPanel panel = new() { Orientation = Orientation.Horizontal };
            ComboBox combo = new() { Width = 160, Height = 22 };
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
            panel.Children.Add(combo);
            panel.Children.Add(hexBox);
            return panel;
        }

        private Profile Active => settings.Active;

        /// <summary>
        /// Table of the range marks: one row per display range, a check box per type of mark and the marks
        /// where the distance is written.
        /// </summary>
        private void BuildRangeMarks()
        {
            building = true;
            Grid grid = RangeMarkGrid;
            grid.Children.Clear();
            grid.RowDefinitions.Clear();
            grid.ColumnDefinitions.Clear();
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(80) });
            foreach (MarkInterval _ in MarkIntervals.All)
            {
                grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(64) });
            }
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            AddCell(grid, Header("Range"), 0, 0);
            for (int c = 0; c < MarkIntervals.All.Length; c++)
            {
                AddCell(grid, Header("every " + MarkIntervals.Name(MarkIntervals.All[c])), 0, c + 1);
            }
            AddCell(grid, Header("Distance written on"), 0, MarkIntervals.All.Length + 1);

            List<string> textChoices = ["none", .. MarkIntervals.All.Select(i => "every " + MarkIntervals.Name(i))];
            int r = 1;
            foreach (RangeMarkRow row in Active.RangeMarks.Rows.OrderByDescending(x => x.Range))
            {
                grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(28) });
                AddCell(grid, new TextBlock
                {
                    Text = row.Range.ToString("0.#", CultureInfo.InvariantCulture) + " NM",
                    VerticalAlignment = VerticalAlignment.Center,
                    FontWeight = FontWeights.SemiBold
                }, r, 0);
                for (int c = 0; c < MarkIntervals.All.Length; c++)
                {
                    MarkInterval interval = MarkIntervals.All[c];
                    // Marks larger than the range would never be drawn.
                    bool possible = MarkIntervals.Nm(interval) <= row.Range + 1e-6;
                    CheckBox check = new()
                    {
                        IsChecked = row.Lines.Contains(interval),
                        IsEnabled = possible,
                        HorizontalAlignment = HorizontalAlignment.Center,
                        VerticalAlignment = VerticalAlignment.Center
                    };
                    RangeMarkRow target = row;
                    check.Checked += (s, e) => SetLine(target, interval, true);
                    check.Unchecked += (s, e) => SetLine(target, interval, false);
                    AddCell(grid, check, r, c + 1);
                }
                ComboBox text = new()
                {
                    ItemsSource = textChoices,
                    SelectedIndex = row.Text is MarkInterval t ? Array.IndexOf(MarkIntervals.All, t) + 1 : 0,
                    Width = 110,
                    Height = 22,
                    Margin = new Thickness(8, 0, 0, 0)
                };
                RangeMarkRow textTarget = row;
                text.SelectionChanged += (s, e) =>
                {
                    if (building) return;
                    textTarget.Text = text.SelectedIndex <= 0 ? null : MarkIntervals.All[text.SelectedIndex - 1];
                    commit();
                };
                AddCell(grid, text, r, MarkIntervals.All.Length + 1);
                r++;
            }
            building = false;
        }

        private void SetLine(RangeMarkRow row, MarkInterval interval, bool on)
        {
            if (building) return;
            if (on && !row.Lines.Contains(interval)) row.Lines.Add(interval);
            if (!on) row.Lines.Remove(interval);
            row.Lines.Sort();
            commit();
        }

        private static TextBlock Header(string text)
        {
            return new TextBlock
            {
                Text = text,
                FontWeight = FontWeights.SemiBold,
                Foreground = System.Windows.Media.Brushes.DimGray,
                TextAlignment = TextAlignment.Center,
                Margin = new Thickness(0, 0, 0, 4),
                VerticalAlignment = VerticalAlignment.Bottom
            };
        }

        private static void AddCell(Grid grid, UIElement element, int row, int column)
        {
            Grid.SetRow(element, row);
            Grid.SetColumn(element, column);
            grid.Children.Add(element);
        }
    }
}
