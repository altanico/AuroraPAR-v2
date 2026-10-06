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
            RemindersHost.Content = new ReminderEditor(() => Active.Reminders, commit);
            MarkersAboveRadio.IsChecked = !Active.RangeTextBelowHorizon;
            MarkersBelowRadio.IsChecked = Active.RangeTextBelowHorizon;
            MarkersAboveRadio.Checked += (s, e) => SetTextBelowHorizon(false);
            MarkersBelowRadio.Checked += (s, e) => SetTextBelowHorizon(true);
        }

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
                AddCell(grid, ColorPicker.Create(style.Color, ColorPicker.StandardColors, hex => Change(s => s.Color = hex)), r, 1);
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
            FrameworkElement picker = ColorPicker.Create(Active.AnalogColor, ColorPicker.Phosphors, hex =>
            {
                Active.AnalogColor = hex;
                commit();
            });
            picker.Margin = new Thickness(8, 0, 0, 0);
            parent.Children.Insert(index, picker);
        }


        private Profile Active => settings.Active;

        /// <summary>Distance text below the horizon line (markers above) or above it (markers below).</summary>
        private void SetTextBelowHorizon(bool below)
        {
            if (Active.RangeTextBelowHorizon == below) return;
            Active.RangeTextBelowHorizon = below;
            commit();
        }

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
            // Two header rows: "Marks every" over the five columns, then the interval of each column.
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            TextBlock marksEvery = Header("Marks every");
            Grid.SetColumnSpan(marksEvery, MarkIntervals.All.Length);
            AddCell(grid, marksEvery, 0, 1);
            AddCell(grid, Header("Range"), 1, 0);
            for (int c = 0; c < MarkIntervals.All.Length; c++)
            {
                AddCell(grid, Header(MarkIntervals.Name(MarkIntervals.All[c])), 1, c + 1);
            }
            TextBlock written = Header("Distance written on");
            written.TextAlignment = TextAlignment.Left;
            written.Margin = new Thickness(8, 0, 0, 4);
            AddCell(grid, written, 1, MarkIntervals.All.Length + 1);

            List<string> textChoices = ["none", .. MarkIntervals.All.Select(i => "every " + MarkIntervals.Name(i))];
            int r = 2;
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
